using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels; // Assumed namespace for view models
using ClosedXML.Excel; // Required for Excel
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using System.IO;
using DeliveryControl.Helpers;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeAdmin]
    public class PreparationScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public PreparationScheduleController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index(DateTime? filterDate, int pageNumber = 1)
        {
            var dateToFilter = filterDate ?? DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .AsQueryable();

            // Default (Today): Hide Completed/Cancelled to keep To-Do list clean
            // Historical/Specific Date: Show everything
            if (!filterDate.HasValue || filterDate.Value == DateTime.Today)
            {
                query = query.Where(s => s.Status != "Cancelled");
            }

            var filteredQuery = query.Where(s => s.ScheduledDate == dateToFilter);
            
            var rawSchedules = await filteredQuery.ToListAsync();

            // Self-Correct Status for display & sorting (Old data fix)
            foreach (var s in rawSchedules)
            {
                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.PreparationStatus != "Prepared")
                    {
                        s.PreparationStatus = "Prepared";
                    }

                    if (s.ActualEndTime.HasValue)
                    {
                        s.Status = "Completed";
                    }
                    else if (s.ActualEnterDockTime.HasValue || s.ActualStartTime.HasValue)
                    {
                        s.Status = "In Progress";
                    }
                    else
                    {
                        // 100% scan but not entered dock yet -> Status remains Scheduled/In Progress but PrepStatus is Prepared
                        // In dashboard logic, we might want to keep it as In Progress if it was already marked as such
                    }
                }
            }

            // Build flat item rows for pagination (view renders 1 row per DeliveryItem)
            // Each schedule without items still counts as 1 row (ghost schedule)
            var flatRows = rawSchedules
                .OrderBy(s =>
                {
                    if (s.PreparationStatus == "Prepared") return 2;
                    if (s.Status == "Completed" || s.ActualEndTime.HasValue) return 3;
                    if (s.PreparationStatus == "In Progress" || s.Status == "In Progress") return 0;
                    return 1;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ThenBy(s => s.ScheduleId)
                .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.Select(di => (Schedule: s, Item: di))
                    : new[] { (Schedule: s, Item: (DeliveryItem)null!) })
                .ToList();

            var totalItems = flatRows.Count;
            int pageSize = 20;

            // Take the page slice of item rows, then reconstruct schedule list preserving order
            var pageRows = flatRows
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Rebuild schedule list: keep only items visible on this page, in original order
            var schedulesOnPage = pageRows
                .GroupBy(r => r.Schedule.ScheduleId)
                .Select(g =>
                {
                    var sched = g.First().Schedule;
                    sched.DeliveryItems = g
                        .Where(r => r.Item != null)
                        .Select(r => r.Item)
                        .ToList();
                    return sched;
                })
                .ToList();

            var schedules = schedulesOnPage;

            var customerItems = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode)
                .Select(c => new {
                    c.CustomerId,
                    DisplayName = $"[{c.CustomerName}] {c.CustomerCode} | Rute: {c.Route ?? "-"} | Cycle: {c.Cycle ?? "-"}"
                })
                .ToListAsync();

            ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(customerItems, "CustomerId", "DisplayName");
            ViewBag.FilterDate = dateToFilter.ToString("yyyy-MM-dd");
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { { "filterDate", dateToFilter.ToString("yyyy-MM-dd") } };

            return View(schedules);
        }

        public async Task<IActionResult> Process(int id)
        {
             var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

             if(schedule == null) return NotFound();

             return View(schedule);
        }

        // GET: PreparationSchedule/Create
        public async Task<IActionResult> Create()
        {
            ViewData["CustomerId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Customers, "CustomerId", "CustomerName");
            
            var itemList = _context.Items
                .OrderBy(i => i.ItemName)
                .Select(i => new {
                    ItemId = i.ItemId,
                    DisplayName = $"{i.ItemName} | Rack: {i.Rack}-{i.NoRack} | VIN: {i.VIN}"
                })
                .ToList();

            ViewData["Items"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(itemList, "ItemId", "DisplayName");

            // Pre-populate ScheduledDate and ScheduleNumber
            var today = DateTime.Today;
            var seq = await GetNextSequenceInternal(today);
            var schedule = new DeliverySchedule
            {
                ScheduledDate = today,
                ScheduleNumber = $"SCH-{today:yyyyMMdd}{seq:D3}",
                Status = "Scheduled"
            };

            return View(schedule);
        }

        // POST: PreparationSchedule/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(List<int> itemIds, List<decimal> itemQtys, [Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,PickupTime,ETD,Range,SKID,Area,TotalTargetQuantity,Notes,Status")] DeliverySchedule deliverySchedule)
        {
            if (ModelState.IsValid)
            {
                if (itemIds == null || !itemIds.Any())
                {
                    ModelState.AddModelError("", "Setidaknya harus ada satu item dalam jadwal.");
                }
                else
                {
                    // Auto-generate schedule number ONLY if empty
                    if (string.IsNullOrEmpty(deliverySchedule.ScheduleNumber))
                    {
                        int sequence = await GetNextSequenceInternal(deliverySchedule.ScheduledDate);
                        deliverySchedule.ScheduleNumber = $"SCH-{deliverySchedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }
                    
                    deliverySchedule.CreatedDate = DateTime.Now;
                    deliverySchedule.CreatedBy = User.Identity?.Name ?? "PreparationPortal";

                    _context.Add(deliverySchedule);
                    
                    // Create DeliveryItems from lists
                    for (int i = 0; i < itemIds.Count; i++)
                    {
                        if (itemIds[i] > 0 && itemQtys.Count > i && itemQtys[i] > 0)
                        {
                            var dItem = new DeliveryItem
                            {
                                DeliverySchedule = deliverySchedule,
                                ItemId = itemIds[i],
                                Quantity = itemQtys[i],
                                ActualQuantity = 0,
                                CreatedDate = DateTime.Now
                            };
                            deliverySchedule.DeliveryItems.Add(dItem);
                            _context.DeliveryItems.Add(dItem);
                        }
                    }

                    // Update total target qty from sum of items
                    deliverySchedule.TotalTargetQuantity = (int)deliverySchedule.DeliveryItems.Sum(di => di.Quantity);

                    await _context.SaveChangesAsync();

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        action = "create",
                        message = $"New schedule {deliverySchedule.ScheduleNumber} created manually.",
                        timestamp = DateTime.Now
                    });

                    return RedirectToAction(nameof(Index));
                }
            }
            
            // On failure, reload view data
            ViewData["CustomerId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Customers, "CustomerId", "CustomerName", deliverySchedule.CustomerId);
            var itemList = await _context.Items.OrderBy(i => i.ItemName)
                .Select(i => new {
                    ItemId = i.ItemId,
                    DisplayName = $"{i.ItemName} | Rack: {i.Rack}-{i.NoRack} | VIN: {i.VIN}"
                }).ToListAsync();
            ViewData["Items"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(itemList, "ItemId", "DisplayName");

            return View(deliverySchedule);
        }

        // POST: PreparationSchedule/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, int pageNumber = 1, DateTime? filterDate = null)
        {
            var deliverySchedule = await _context.DeliverySchedules.FindAsync(id);
            if (deliverySchedule != null)
            {
                _context.DeliverySchedules.Remove(deliverySchedule);
                await _context.SaveChangesAsync();

                // Notify Dashboard via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "delete",
                    message = $"Preparation schedule {deliverySchedule.ScheduleNumber} deleted.",
                    timestamp = DateTime.Now
                });
            }
            return RedirectToAction(nameof(Index), new { pageNumber = pageNumber, filterDate = filterDate });
        }

        // GET: PreparationSchedule/BulkCreate
        public async Task<IActionResult> BulkCreate(DateTime? selectedDate)
        {
            var scheduledDate = selectedDate ?? DateTime.Today;
            
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode)
                .ToListAsync();
            
            var customerItems = customers.Select(c => new CustomerScheduleItem
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Route = c.Route,
                Cycle = c.Cycle,
                Docking = c.Docking,
                Pickup = c.Pickup,
                ETD = c.ETD,
                IsSelected = false,
                IsMatchingDay = ShouldScheduleCustomerOnDate(c, scheduledDate)
            }).ToList();
            
            var viewModel = new BulkScheduleViewModel
            {
                ScheduledDate = scheduledDate,
                AvailableCustomers = customerItems,
                IsAutoGenerate = true
            };
            
            return View(viewModel);
        }

        // POST: PreparationSchedule/BulkCreate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkCreate(BulkScheduleViewModel model)
        {
            try
            {
                var schedules = new List<DeliverySchedule>();
                List<Customer> customers;

                if (model.IsAutoGenerate)
                {
                    if (model.ScheduledDate.DayOfWeek == DayOfWeek.Saturday || model.ScheduledDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        TempData["ErrorMessage"] = "Mode otomatis hanya berlaku untuk hari Seninâ€“Jumat.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers.Where(c => c.IsActive).ToListAsync();
                    customers = customers.Where(c => ShouldScheduleCustomerOnDate(c, model.ScheduledDate)).ToList();

                    if (!customers.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang memenuhi kriteria Cycle.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }
                }
                else
                {
                    if (model.SelectedCustomerIds == null || !model.SelectedCustomerIds.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang dipilih!";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .Where(c => model.SelectedCustomerIds.Contains(c.CustomerId))
                        .ToListAsync();
                }
                
                int sequenceNumber = await GetNextSequenceInternal(model.ScheduledDate);
                
                foreach (var customer in customers)
                {
                    var prefix = $"SCH-{model.ScheduledDate:yyyyMMdd}";
                    var scheduleNumber = $"{prefix}{sequenceNumber:D3}";
                    sequenceNumber++;

                    var pickupTime = ParseTimeToDateTime(customer.Pickup, model.ScheduledDate);
                    var enterDockTime = ParseTimeToDateTime(customer.Docking, model.ScheduledDate);
                    var etdTime = ParseTimeToDateTime(customer.ETD, model.ScheduledDate);

                    // Gunakan StartPrepare sebagai anchor untuk menentukan H+1
                    // Jika jam milestone < jam mulai persiapan, maka itu adalah besok harinya
                    var startPrepMinutes = customer.StartPrepareTime;
                    var startPrepTime = model.ScheduledDate.Date.AddMinutes(startPrepMinutes);

                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                    {
                        enterDockTime = enterDockTime.Value.AddDays(1);
                    }

                    if (pickupTime.HasValue && pickupTime.Value < startPrepTime)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }

                    if (etdTime.HasValue && etdTime.Value < startPrepTime)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }

                    // Double Checks: Pastikan urutan logis tetap terjaga (Dock -> Pickup -> ETD)
                    if (pickupTime.HasValue && enterDockTime.HasValue && pickupTime.Value < enterDockTime.Value)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }
                    if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value < pickupTime.Value)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }
                    
                    var schedule = new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = customer.CustomerId,
                        ScheduledDate = model.ScheduledDate,
                        Route = customer.Route,
                        Cycle = customer.Cycle,
                        EnterDockTime = enterDockTime,
                        PickupTime = pickupTime,
                        ETD = etdTime,
                        Range = customer.Range,
                        SKID = ParseSKID(customer.SKID),
                        Area = customer.Area,
                        Status = "Scheduled", // Default status
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "PreparationPortal"
                    };
                    
                    schedules.Add(schedule);
                }
                
                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "bulk_create", message = $"Created {schedules.Count} schedules", timestamp = DateTime.Now });

                        TempData["SuccessMessage"] = $"Berhasil membuat {schedules.Count} schedule!";
                        return RedirectToAction(nameof(Index));
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
            }
            
            return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Schedule");

                // Headers Minimalis (Penyederhanaan Novosibirsk 2.0)
                var headers = new[] { 
                    "MANIFESTING", "PART NO / VIN", "QTY (PCS)" 
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Example data (Row 2)
                var sampleItem = _context.Items.Where(i => !string.IsNullOrEmpty(i.CustomerPartNumber)).FirstOrDefault();

                worksheet.Cell(2, 1).Value = "MAN-202502-001"; // Manifesting
                worksheet.Cell(2, 2).Value = sampleItem?.CustomerPartNumber ?? "PART-XYZ-001"; // Part No / VIN
                worksheet.Cell(2, 3).Value = 100; // Qty (Pcs)

                // Border
                headerRange.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Columns().AdjustToContents();

                // Instructions
                worksheet.Cell(4, 1).Value = "PANDUAN:";
                worksheet.Cell(5, 1).Value = "1. Isi data Manifest, Part Number (atau VIN), dan Qty.";
                worksheet.Cell(6, 1).Value = "2. Detail Customer (Dock, Route, Cycle) dipilih melalui dropdown di aplikasi saat upload.";
                worksheet.Cell(7, 1).Value = "3. Pastikan kolom header tidak berubah nama.";

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Jadwal_Preparation.xlsx");
                }
            }
        }
        [HttpGet]
        public IActionResult ImportExcel()
        {
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportExcel([FromForm] IFormFile file, [FromForm] int? customerId)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid! Silakan pilih file Excel terlebih dahulu.";
                return RedirectToAction(nameof(Index));
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToAction(nameof(Index));
            }

            int successCount = 0;
            int itemCount = 0;
            int errorCount = 0;
            var errorSamples = new List<string>();

            try
            {
                // 1. Load Customers for Lookup 
                var allCustomers = await _context.Customers.AsNoTracking().ToListAsync();
                Customer? chosenCustomer = customerId.HasValue ? allCustomers.FirstOrDefault(c => c.CustomerId == customerId.Value) : null;
                
                // Index customers for fallback Excel matching
                var customersByContext = allCustomers
                    .GroupBy(c => $"{NormalizeHeader(c.CustomerName)}|{NormalizeHeader(c.Route)}|{NormalizeHeader(c.Cycle)}")
                    .ToDictionary(g => g.Key, g => g.First());

                var customersByName = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode))
                    .GroupBy(c => NormalizeHeader(c.CustomerCode))
                    .ToDictionary(g => g.Key, g => g.First());

                                // 2. Load Items & Mappings for "Translation" 
                // v12.0: SEPARATION OF CONCERNS
                // - Mappings: Part No -> VIN
                // - Items: VIN -> Stock/Loc/QPC
                var allItems = await _context.Items.Where(i => i.IsActive).ToListAsync();
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);

                        // --- Robust Header Detection ---
                        var headerRow = worksheet.Row(1);
                        var targetKeywords = new[] { 
                            "MANIFESTING", "DOCK", "KODE", "CUSTOMER", "ROUTE", 
                            "CYCLE", "ITEM", "PART", "QTY", "KANBAN" 
                        };

                        for (int r = 1; r <= 10; r++) // Scan first 10 rows
                        {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 20; c++)
                            {
                                var val = NormalizeHeader(GetSafeString(testRow.Cell(c)));
                                if (val.Contains("MANIFESTING") || val.Contains("DOCK") || val.Contains("CUSTOMER") || val.Contains("PART")) 
                                    currentScore++;
                            }
                            if (currentScore >= 3) { headerRow = testRow; break; }
                        }

                        // Map Headers accurately
                        var cleanedHeaders = new Dictionary<string, int>();
                        for (int col = 1; col <= worksheet.LastColumnUsed().ColumnNumber(); col++)
                        {
                            var cleaned = NormalizeHeader(GetSafeString(headerRow.Cell(col)));
                            if (!string.IsNullOrEmpty(cleaned) && !cleanedHeaders.ContainsKey(cleaned)) cleanedHeaders.Add(cleaned, col);
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords) {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanedHeaders.ContainsKey(cleanKw)) return cleanedHeaders[cleanKw];
                                var match = cleanedHeaders.Keys.FirstOrDefault(k => k.Contains(cleanKw));
                                if (match != null) return cleanedHeaders[match];
                            }
                            return -1;
                        }

                        string SafeNormalize(string? input) => NormalizeHeader(input ?? "");

                        var colMap = new {
                            Manifesting = FindCol("MANIFESTING", "MANIFEST"),
                            Dock = FindCol("DOCK"),
                            CustName = FindCol("NAMA CUSTOMER", "NAMA", "CUSTOMER"),
                            Route = FindCol("ROUTE", "RUTE"),
                            Cycle = FindCol("CYCLE", "SIKLUS"),
                            PartNo = FindCol("PART NO", "CUSTOMER PART", "PART"),
                            Vin = FindCol("VIN", "INTERNAL CODE", "INTERNAL"),
                            ItemPartNo = FindCol("PART NO", "PART NO / VIN", "ITEM", "PART", "VIN"), // Legacy/Combined
                            QtyPcs = FindCol("QTY (PCS)", "QTY PCS", "QTY"),
                            Pickup = FindCol("PICKUP", "PENJEMPUTAN"),
                            Etd = FindCol("ETD")
                        };

                        if (colMap.CustName == -1 && chosenCustomer == null) {
                            TempData["ErrorMessage"] = "Header 'NAMA CUSTOMER' tidak ditemukan. Pastikan file Excel sesuai template atau pilih Customer dari dropdown.";
                            return RedirectToAction(nameof(Index));
                        }

                        // --- PHASE 1: Collect All Valid Data Rows ---
                        var validRowsData = new List<ExcelRowData>();
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber());
                        
                        string lastCustName = "";
                        string lastManifest = "";
                        string lastDock = "";
                        string lastRoute = "";
                        string lastCycle = "";

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            try
                            {
                                string custNameCell = colMap.CustName != -1 ? GetSafeString(row.Cell(colMap.CustName)).Trim() : "";
                                string manifest = colMap.Manifesting != -1 ? GetSafeString(row.Cell(colMap.Manifesting)).Trim().ToUpper() : "";
                                
                                // v13.0: Separate Part No and VIN Support
                                string excelPartNo = colMap.PartNo != -1 ? GetSafeString(row.Cell(colMap.PartNo)).Trim().ToUpper() : "";
                                string excelVin = colMap.Vin != -1 ? GetSafeString(row.Cell(colMap.Vin)).Trim().ToUpper() : "";
                                string legacyItemCode = colMap.ItemPartNo != -1 ? GetSafeString(row.Cell(colMap.ItemPartNo)).Trim().ToUpper() : "";

                                string itemCode = !string.IsNullOrEmpty(excelPartNo) ? excelPartNo : (!string.IsNullOrEmpty(excelVin) ? excelVin : legacyItemCode);
                                
                                string dockLocation = colMap.Dock != -1 ? GetSafeString(row.Cell(colMap.Dock)).Trim() : "";
                                string route = colMap.Route != -1 ? GetSafeString(row.Cell(colMap.Route)).Trim() : "";
                                string cycle = colMap.Cycle != -1 ? GetSafeString(row.Cell(colMap.Cycle)).Trim() : "";
                                int qty = colMap.QtyPcs != -1 ? GetSafeInt(row.Cell(colMap.QtyPcs)) : 0;

                                // --- Fill Down Logic ---
                                if (string.IsNullOrEmpty(custNameCell)) custNameCell = lastCustName; else lastCustName = custNameCell;
                                if (string.IsNullOrEmpty(manifest)) manifest = lastManifest; else lastManifest = manifest;
                                if (string.IsNullOrEmpty(dockLocation)) dockLocation = lastDock; else lastDock = dockLocation;
                                if (string.IsNullOrEmpty(route)) route = lastRoute; else lastRoute = route;
                                if (string.IsNullOrEmpty(cycle)) cycle = lastCycle; else lastCycle = cycle;

                                if (string.IsNullOrEmpty(itemCode)) continue; 
                                
                                // --- MASTER CUSTOMER LOOKUP ---
                                Customer? customer = chosenCustomer; // PRIORITAS 1: Dropdown
                                
                                if (customer == null) // PRIORITAS 2: Excel Content
                                {
                                    string contextKey = $"{NormalizeHeader(dockLocation)}|{NormalizeHeader(route)}|{NormalizeHeader(cycle)}";
                                    if (customersByContext.TryGetValue(contextKey, out var cByContext)) {
                                        customer = cByContext;
                                    } else if (!string.IsNullOrEmpty(custNameCell) && customersByName.TryGetValue(NormalizeHeader(custNameCell), out var cByName)) {
                                        customer = cByName;
                                    }
                                }

                                if (customer == null) {
                                    errorCount++;
                                    if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: Customer tidak ditemukan (Pilih customer di dropdown atau lengkapi kolom Dock/Route/Cycle di Excel).");
                                    continue;
                                }

                            // --- EXHAUSTIVE ITEM MATCHING ---
                            string normalizedInput = SafeNormalize(itemCode);
                            Item? matchedItem = null;
                            string? finalVin = null;
                            ItemMapping? itemMap = null;

                            // PRIORITAS 1: Cari di Master Items berdasarkan CustomerPartNumber (Flexible Match for VIN-like codes)
                            matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.CustomerPartNumber, itemCode));
                            if (matchedItem != null) finalVin = matchedItem.VIN;

                            // PRIORITAS 2: Cari di ItemMappings berdasarkan CustomerPartNumber
                            if (matchedItem == null)
                            {
                                itemMap = allMappings.FirstOrDefault(m => VinHelper.IsMatch(m.CustomerPartNumber, itemCode));
                                if (itemMap != null)
                                {
                                    finalVin = itemMap.VIN;
                                    matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, finalVin));
                                }
                            }

                            // PRIORITAS 3: Cari di Master Items berdasarkan VIN (Flexible Match)
                            if (matchedItem == null)
                            {
                                matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, itemCode));
                                if (matchedItem != null) finalVin = matchedItem.VIN;
                            }

                            // PRIORITAS 4: Cari di ItemMappings berdasarkan VIN
                            if (matchedItem == null)
                            {
                                var itemMapByVin = allMappings.FirstOrDefault(m => VinHelper.IsMatch(m.VIN, itemCode));
                                if (itemMapByVin != null)
                                {
                                    finalVin = itemMapByVin.VIN;
                                    matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, finalVin));
                                    itemMap = itemMapByVin;
                                }
                            }

                            // PRIORITAS 5: Legacy/ItemCode Match
                            if (matchedItem == null)
                            {
                                matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.ItemCode, itemCode));
                                if (matchedItem != null) finalVin = matchedItem.VIN;
                            }

                            // Set targetVin for Phase 2
                            string targetVin = finalVin ?? normalizedInput;

                            if (matchedItem == null)
                            {
                                // v11.0: AUTO-CREATE Master Item to ensure sync (Draft Mode)
                                matchedItem = new Item
                                {
                                    ItemCode = targetVin, // Use VIN as Code
                                    VIN = targetVin,
                                    CustomerPartNumber = !string.IsNullOrEmpty(excelPartNo) ? excelPartNo : ((itemMap != null) ? itemMap.CustomerPartNumber : (targetVin != itemCode ? itemCode : null)),
                                    Customer = customer.CustomerName,
                                    ItemName = "Imported (" + itemCode + ")",
                                    Description = "Auto-created from Schedule Import",
                                    IsActive = true,
                                    CreatedDate = DateTime.Now,
                                    QtyLot = 1 // Default QPC
                                };
                                _context.Items.Add(matchedItem);
                                allItems.Add(matchedItem); 
                            }
                            else 
                            {
                                // v14.0: Aggressive Enrichment - Use the most complete Part Number available
                                string? suggestedPartNo = (itemMap != null) ? itemMap.CustomerPartNumber : 
                                                         (targetVin != itemCode && !string.IsNullOrEmpty(itemCode) ? itemCode : null);

                                if (!string.IsNullOrEmpty(suggestedPartNo))
                                {
                                    // Update jika kosong ATAU jika Part No baru lebih panjang (lebih lengkap)
                                    if (string.IsNullOrEmpty(matchedItem.CustomerPartNumber) || 
                                        suggestedPartNo.Length > matchedItem.CustomerPartNumber.Length)
                                    {
                                        matchedItem.CustomerPartNumber = suggestedPartNo;
                                        _context.Update(matchedItem);
                                    }
                                }
                            }

                                // v10.0: QTY/LOT (QPC) CONSOLIDATION
                                if (matchedItem.ItemId > 0 && (matchedItem.QtyLot == null || matchedItem.QtyLot == 0) && !string.IsNullOrEmpty(matchedItem.VIN))
                                {
                                    var referenceItem = allItems.FirstOrDefault(i => 
                                        i.ItemId != matchedItem.ItemId &&
                                        i.VIN == matchedItem.VIN && 
                                        i.QtyLot != null && 
                                        i.QtyLot > 0);
                                    
                                    if (referenceItem != null)
                                    {
                                        matchedItem.QtyLot = referenceItem.QtyLot;
                                        matchedItem.UpdatedDate = DateTime.Now;
                                        _context.Update(matchedItem);
                                    }
                                }


                                validRowsData.Add(new ExcelRowData {
                                    RowNumber = row.RowNumber(),
                                    Customer = customer,
                                    ManifestNum = manifest,
                                    ItemCode = itemCode,
                                    DockLocation = dockLocation,
                                    Route = route,
                                    Cycle = cycle,
                                    MatchedItem = matchedItem,
                                    Quantity = qty,
                                    Row = row
                                });
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: {ex.Message}");
                            }
                        }

                        // --- PHASE 2: 1 Baris Excel = 1 DeliverySchedule ---
                        var scheduledDate = DateTime.Today;
                        var importSession = DateTime.Now.ToString("HHmm");

                        int sequentialCounter = 1;
                        foreach (var rowData in validRowsData)
                        {
                            var customer = rowData.Customer;

                            var scheduleNumber = string.IsNullOrEmpty(rowData.ManifestNum)
                                ? $"SCH-{scheduledDate:yyyyMMdd}-{sequentialCounter++}"
                                : $"{rowData.ManifestNum}/{importSession}-{sequentialCounter++}";

                            var schedule = new DeliverySchedule
                            {
                                ScheduleNumber = scheduleNumber,
                                CustomerId = customer.CustomerId,
                                ScheduledDate = scheduledDate,
                                Status = "Scheduled",
                                CreatedDate = DateTime.Now,
                                CreatedBy = User.Identity?.Name ?? "ImportExcel",
                                Route = !string.IsNullOrEmpty(customer.Route) ? customer.Route : rowData.Route,
                                Cycle = !string.IsNullOrEmpty(customer.Cycle) ? customer.Cycle : rowData.Cycle,
                                Area = !string.IsNullOrEmpty(customer.Area) ? customer.Area : rowData.DockLocation,
                                StartPrepareTime = customer.StartPrepareTime,
                                StdPrepareTime = customer.StdPrepareTime,
                                Range = customer.Range,
                                SKID = customer.SKID
                            };

                            var enterDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                            var pickupTime = (colMap.Pickup != -1 ? GetSafeTime(rowData.Row.Cell(colMap.Pickup), scheduledDate) : null)
                                            ?? ParseTimeToDateTime(customer.Pickup, scheduledDate);
                            var etdTime = (colMap.Etd != -1 ? GetSafeTime(rowData.Row.Cell(colMap.Etd), scheduledDate) : null)
                                          ?? ParseTimeToDateTime(customer.ETD, scheduledDate);

                            var startPrepTime = scheduledDate.Date.AddMinutes(customer.StartPrepareTime);
                            if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime) enterDockTime = enterDockTime.Value.AddDays(1);
                            if (pickupTime.HasValue && pickupTime.Value < startPrepTime) pickupTime = pickupTime.Value.AddDays(1);
                            if (etdTime.HasValue && etdTime.Value < startPrepTime) etdTime = etdTime.Value.AddDays(1);
                            if (pickupTime.HasValue && enterDockTime.HasValue && pickupTime.Value < enterDockTime.Value) pickupTime = pickupTime.Value.AddDays(1);
                            if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value < pickupTime.Value) etdTime = etdTime.Value.AddDays(1);

                            schedule.EnterDockTime = enterDockTime;
                            schedule.PickupTime = pickupTime;
                            schedule.ETD = etdTime;

                            var deliveryItem = new DeliveryItem
                            {
                                Quantity = rowData.Quantity,
                                ActualQuantity = 0,
                                CreatedDate = DateTime.Now
                            };
                            if (rowData.MatchedItem.ItemId == 0)
                                deliveryItem.Item = rowData.MatchedItem;
                            else
                                deliveryItem.ItemId = rowData.MatchedItem.ItemId;

                            schedule.DeliveryItems.Add(deliveryItem);
                            schedule.TotalTargetQuantity = rowData.Quantity;
                            _context.DeliverySchedules.Add(schedule);
                            successCount++;
                            itemCount++;
                        }

                        // SAVE EVERYTHING
                        await _context.SaveChangesAsync();

                        // Notify Dashboard via SignalR
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                        {
                            action = "import",
                            message = $"Berhasil import {successCount} schedule preparation dari Excel.",
                            timestamp = DateTime.Now
                        });
                    }
                }

                if (successCount > 0)
                {
                    TempData["SuccessMessage"] = $"Berhasil import {successCount} schedule ({itemCount} item baris) dari Excel!";
                }
                
                if (errorCount > 0)
                {
                     TempData["ErrorMessage"] = $"{errorCount} baris gagal. Contoh: {string.Join(", ", errorSamples)}";
                }
            }
            catch (Exception ex)
            {
                 TempData["ErrorMessage"] = $"Fatal Error: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // --- Helpers ---

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return System.Text.RegularExpressions.Regex.Replace(header, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
        }

        private string GetSafeString(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "";
            try { return cell.Value.ToString().Trim(); } catch { return ""; }
        }

        private DateTime? GetSafeTime(IXLCell cell, DateTime baseDate)
        {
             if (cell == null || cell.IsEmpty()) return null;
             string raw = cell.Value.ToString();
             
             // Try parsing Time (HH:mm)
             if (TimeSpan.TryParse(raw, out TimeSpan ts)) return baseDate.Date.Add(ts);
             if (DateTime.TryParse(raw, out DateTime dt)) return baseDate.Date.Add(dt.TimeOfDay);

             return null;
        }

        private int GetSafeInt(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return 0;
            string raw = cell.Value.ToString();
             // Regex Extraction
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"[0-9]+");
            if (match.Success && int.TryParse(match.Value, out int val)) return val;
            return 0;
        }

        // Keep existing Helpers
        private bool ShouldScheduleCustomerOnDate(Customer customer, DateTime date)
        {
             if (string.IsNullOrWhiteSpace(customer.Cycle)) return true;
             var day = date.ToString("dddd", new System.Globalization.CultureInfo("id-ID")); // Senin, etc
             return customer.Cycle.Contains(day, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<int> GetNextSequenceInternal(DateTime date)
        {
            var prefix = $"SCH-{date:yyyyMMdd}";
            var lastSchedule = await _context.DeliverySchedules
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();

            if (lastSchedule == null) return 1;

            var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
            if (int.TryParse(lastSequence, out int lastNum))
            {
                return lastNum + 1;
            }
            return 1;
        }

        private DateTime? ParseTimeToDateTime(string? timeString, DateTime baseDate)
        {
            if (string.IsNullOrWhiteSpace(timeString)) return null;
            if (TimeSpan.TryParse(timeString, out TimeSpan time)) return baseDate.Date.Add(time);
            return null;
        }

        private string? ParseSKID(string? skid) => string.IsNullOrWhiteSpace(skid) ? null : skid.Trim();

        [HttpGet]
        public async Task<IActionResult> GetNextScheduleNumber(DateTime date)
        {
            var seq = await GetNextSequenceInternal(date);
            var number = $"SCH-{date:yyyyMMdd}{seq:D3}";
            return Json(new { number = number });
        }
    }

    public class ExcelRowData
    {
        public int RowNumber { get; set; }
        public Customer Customer { get; set; } = null!;
        public string ManifestNum { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string? DockLocation { get; set; }
        public string Route { get; set; } = "";
        public string Cycle { get; set; } = "";
        public Item? MatchedItem { get; set; }
        public int Quantity { get; set; }
        public IXLRow Row { get; set; } = null!;
    }
}
