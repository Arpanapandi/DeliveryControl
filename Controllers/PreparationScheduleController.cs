using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels; // Assumed namespace for view models
using ClosedXML.Excel; // Required for Excel
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using System.IO;

namespace DeliveryControl.Controllers
{
    public class PreparationScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public PreparationScheduleController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index(DateTime? filterDate)
        {
            var dateToFilter = filterDate ?? DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.DeliveryItems.Any()); // Hide Ghost Data

            // Default (Today): Hide Completed/Cancelled to keep To-Do list clean
            // Historical/Specific Date: Show everything
            if (!filterDate.HasValue || filterDate.Value == DateTime.Today)
            {
                query = query.Where(s => s.Status != "Cancelled" && s.Status != "Completed");
            }

            var schedules = await query
                .Where(s => s.ScheduledDate == dateToFilter)
                .OrderBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();

            ViewBag.FilterDate = dateToFilter.ToString("yyyy-MM-dd");
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
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deliverySchedule = await _context.DeliverySchedules.FindAsync(id);
            if (deliverySchedule != null)
            {
                _context.DeliverySchedules.Remove(deliverySchedule);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
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
                        TempData["ErrorMessage"] = "Mode otomatis hanya berlaku untuk hari Senin–Jumat.";
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

                    if (enterDockTime.HasValue && pickupTime.HasValue && enterDockTime.Value.TimeOfDay > pickupTime.Value.TimeOfDay)
                        enterDockTime = enterDockTime.Value.AddDays(-1);

                    if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value.TimeOfDay < pickupTime.Value.TimeOfDay)
                        etdTime = etdTime.Value.AddDays(1);
                    
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
                    _context.DeliverySchedules.AddRange(schedules);
                    await _context.SaveChangesAsync();
                     await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new { Action = "bulk_create", Message = $"Created {schedules.Count} schedules" });

                    TempData["SuccessMessage"] = $"Berhasil membuat {schedules.Count} schedule!";
                    return RedirectToAction(nameof(Index));
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

                // Headers sesuai gambar User (Ex: 8 Kolom)
                var headers = new[] { 
                    "MANIFESTING", "DOCK", "KODE CUSTOMER", "NAMA CUSTOMER", 
                    "ROUTE", "CYCLE", "PART NO / VIN", "QTY (PCS)" 
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

                // Example data (Row 2) - Use REAL DATA from Items/Customers if available
                var sampleItem = _context.Items.Where(i => !string.IsNullOrEmpty(i.CustomerPartNumber)).FirstOrDefault();
                var sampleCust = _context.Customers.FirstOrDefault();

                worksheet.Cell(2, 1).Value = "MAN-001"; // Manifesting
                worksheet.Cell(2, 2).Value = sampleCust?.Docking ?? "DOCK-A"; // Dock (Location Code)
                worksheet.Cell(2, 3).Value = sampleCust?.CustomerCode ?? "CUST001"; // Kode Customer
                worksheet.Cell(2, 4).Value = sampleCust?.CustomerName ?? "PT. CONTOH"; // Nama Customer
                worksheet.Cell(2, 5).Value = sampleCust?.Route ?? "R1"; // Route
                worksheet.Cell(2, 6).Value = sampleCust?.Cycle ?? "C1"; // Cycle
                worksheet.Cell(2, 7).Value = sampleItem?.CustomerPartNumber ?? "PART-001"; // Part No / VIN
                worksheet.Cell(2, 8).Value = 500; // Qty (Pcs)

                // Border
                headerRange.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Columns().AdjustToContents();

                // Instructions
                worksheet.Cell(4, 2).Value = "CATATAN PENGISIAN:";
                worksheet.Cell(5, 2).Value = "• Kolom DOCK berisi Kode Lokasi Dock.";
                worksheet.Cell(6, 2).Value = "• Kolom KODE CUSTOMER dan PART NO / VIN Wajib diisi.";
                worksheet.Cell(7, 2).Value = "• Qty/Lot dan Kanban otomatis terisi dari Master Item.";

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
        public async Task<IActionResult> ImportExcel([FromForm] IFormFile file)
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
            int errorCount = 0;
            var errorSamples = new List<string>();

            try
            {
                // 1. Load Customers for Lookup (Optimized for multiple matching strategies)
                var allCustomers = await _context.Customers.AsNoTracking().ToListAsync();
                
                // Use GroupBy to handle duplicates in DB gracefully
                var customersByCode = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode))
                    .GroupBy(c => c.CustomerCode.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.First());

                var customersByName = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerName))
                    .GroupBy(c => NormalizeHeader(c.CustomerName))
                    .ToDictionary(g => g.Key, g => g.First());

                // 2. Load Items for "Translation" (Manifest -> VIN)
                var allItems = await _context.Items.AsNoTracking().Where(i => i.IsActive).ToListAsync();

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
                        
                            if (currentScore >= 3)
                            {
                                headerRow = testRow;
                                break;
                            }
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
                            foreach (var kw in keywords)
                            {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanedHeaders.ContainsKey(cleanKw)) return cleanedHeaders[cleanKw];
                                var match = cleanedHeaders.Keys.FirstOrDefault(k => k.Contains(cleanKw));
                                if (match != null) return cleanedHeaders[match];
                            }
                            return -1;
                        }

                        // Local helper for normalization in matching
                        string SafeNormalize(string? input) => NormalizeHeader(input ?? "");

                        // Column Mapping (Updated to match Image: 8 Col)
                        var colMap = new
                        {
                            Manifesting = FindCol("MANIFESTING", "MANIFEST"),
                            Dock = FindCol("DOCK"),
                            Cust = FindCol("KODE CUSTOMER", "KODE", "CUST"),
                            CustName = FindCol("NAMA CUSTOMER", "NAMA"),
                            Route = FindCol("ROUTE", "RUTE"),
                            Cycle = FindCol("CYCLE", "SIKLUS"),
                            ItemPartNo = FindCol("PART NO / VIN", "PART NO", "ITEM", "PART", "VIN"),
                            QtyPcs = FindCol("QTY (PCS)", "QTY PCS", "QTY"),
                            Pickup = FindCol("PICKUP", "PENJEMPUTAN"),
                            Etd = FindCol("ETD")
                        };

                        if (colMap.Cust == -1)
                        {
                            TempData["ErrorMessage"] = "Header 'KODE CUSTOMER' tidak ditemukan. Pastikan file Excel sesuai template.";
                            return RedirectToAction(nameof(Index));
                        }

                        // --- PHASE 1: Collect All Valid Data Rows ---
                        var validRowsData = new List<ExcelRowData>();
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber());
                        
                        // Fill-down states
                        string lastCustCode = "";
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
                                string custCodeCell = GetSafeString(row.Cell(colMap.Cust)).Trim().ToUpper();
                                string custNameCell = colMap.CustName != -1 ? GetSafeString(row.Cell(colMap.CustName)).Trim() : "";
                                string manifest = colMap.Manifesting != -1 ? GetSafeString(row.Cell(colMap.Manifesting)).Trim().ToUpper() : "";
                                string itemCode = colMap.ItemPartNo != -1 ? GetSafeString(row.Cell(colMap.ItemPartNo)).Trim().ToUpper() : "";
                                string dockLocation = colMap.Dock != -1 ? GetSafeString(row.Cell(colMap.Dock)).Trim() : "";
                                string route = colMap.Route != -1 ? GetSafeString(row.Cell(colMap.Route)).Trim() : "";
                                string cycle = colMap.Cycle != -1 ? GetSafeString(row.Cell(colMap.Cycle)).Trim() : "";
                                int qty = colMap.QtyPcs != -1 ? GetSafeInt(row.Cell(colMap.QtyPcs)) : 0;

                                // --- Fill Down Logic ---
                                if (string.IsNullOrEmpty(custCodeCell)) custCodeCell = lastCustCode; else lastCustCode = custCodeCell;
                                if (string.IsNullOrEmpty(custNameCell)) custNameCell = lastCustName; else lastCustName = custNameCell;
                                if (string.IsNullOrEmpty(manifest)) manifest = lastManifest; else lastManifest = manifest;
                                if (string.IsNullOrEmpty(dockLocation)) dockLocation = lastDock; else lastDock = dockLocation;
                                if (string.IsNullOrEmpty(route)) route = lastRoute; else lastRoute = route;
                                if (string.IsNullOrEmpty(cycle)) cycle = lastCycle; else lastCycle = cycle;

                                if (string.IsNullOrEmpty(custCodeCell) && string.IsNullOrEmpty(custNameCell)) continue; 
                                if (string.IsNullOrEmpty(itemCode)) continue; 
                                
                                // --- FLEXIBLE CUSTOMER LOOKUP (Triple-Link Step A) ---
                                Customer? customer = null;
                                if (!string.IsNullOrEmpty(custCodeCell) && customersByCode.TryGetValue(custCodeCell, out var cByCode)) 
                                    customer = cByCode;
                                else if (!string.IsNullOrEmpty(custNameCell) && customersByName.TryGetValue(NormalizeHeader(custNameCell), out var cByName)) 
                                    customer = cByName;
                                else if (!string.IsNullOrEmpty(custCodeCell) && customersByName.TryGetValue(NormalizeHeader(custCodeCell), out var cByCodeAsName))
                                    customer = cByCodeAsName; // Case where code is actually the name

                                if (customer == null)
                                {
                                    errorCount++;
                                    if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: Customer '{custCodeCell}/{custNameCell}' tidak ditemukan di Master Customer.");
                                    continue;
                                }

                                // --- ROBUST ITEM MATCHING (Triple-Link Step B) ---
                                string normalizedItemCode = SafeNormalize(itemCode);
                                string normalizedCustContext = SafeNormalize(custCodeCell);

                                // High Priority: Match Part + Customer Context (Kamus Integration)
                                var matchedItem = allItems.FirstOrDefault(i => 
                                    (SafeNormalize(i.CustomerPartNumber) == normalizedItemCode || SafeNormalize(i.VIN) == normalizedItemCode) &&
                                    (SafeNormalize(i.Customer) == normalizedCustContext || 
                                     SafeNormalize(i.Customer).Contains(normalizedCustContext) || 
                                     normalizedCustContext.Contains(SafeNormalize(i.Customer)))
                                );

                                // Medium Priority: Global Match (If only 1 item exists for this part globally)
                                if (matchedItem == null)
                                {
                                    var potentialMatches = allItems.Where(i => 
                                        SafeNormalize(i.CustomerPartNumber) == normalizedItemCode || SafeNormalize(i.VIN) == normalizedItemCode).ToList();
                                    
                                    if (potentialMatches.Count == 1)
                                    {
                                        matchedItem = potentialMatches.First();
                                    }
                                    else if (potentialMatches.Count > 1)
                                    {
                                        // Try to pick one that has ANY customer info matching our target customer name/code
                                        matchedItem = potentialMatches.FirstOrDefault(i => 
                                            !string.IsNullOrEmpty(i.Customer) && 
                                            (SafeNormalize(customer.CustomerName).Contains(SafeNormalize(i.Customer)) || 
                                             SafeNormalize(customer.CustomerCode).Contains(SafeNormalize(i.Customer))));
                                        
                                        if (matchedItem == null) matchedItem = potentialMatches.First();
                                    }
                                }

                                // Low Priority: Item Name Fallback
                                if (matchedItem == null)
                                {
                                    matchedItem = allItems.FirstOrDefault(i => SafeNormalize(i.ItemName) == normalizedItemCode);
                                }

                                if (matchedItem == null)
                                {
                                    errorCount++;
                                    if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: Part '{itemCode}' tidak terdaftar untuk customer '{custCodeCell}'.");
                                    continue;
                                }

                                validRowsData.Add(new ExcelRowData {
                                    RowNumber = row.RowNumber(),
                                    Customer = customer,
                                    ManifestNum = manifest,
                                    ItemCode = itemCode,
                                    DockLocation = dockLocation,
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

                        // --- PHASE 2: Grouping Logic (The "Perfect" Part) ---
                        var scheduledDate = DateTime.Today;
                        int sequenceNumber = await GetNextSequenceInternal(scheduledDate);
                        var schedules = new List<DeliverySchedule>();

                        // Group by Manifest & Customer (Primary Grouping)
                        var manifestGroups = validRowsData.GroupBy(r => new { r.ManifestNum, r.Customer.CustomerId });

                        foreach (var group in manifestGroups)
                        {
                            var first = group.First();
                            string finalScheduleNumber = group.Key.ManifestNum;
                            
                            if (string.IsNullOrEmpty(finalScheduleNumber))
                            {
                                var prefix = $"SCH-{scheduledDate:yyyyMMdd}";
                                finalScheduleNumber = $"{prefix}{sequenceNumber:D3}";
                                sequenceNumber++;
                            }

                            // Secondary Logic: Times use first row of group
                            var enterDockTime = colMap.Dock != -1 ? GetSafeTime(first.Row.Cell(colMap.Dock), scheduledDate) : ParseTimeToDateTime(first.Customer.Docking, scheduledDate);
                            var pickupTime = colMap.Pickup != -1 ? GetSafeTime(first.Row.Cell(colMap.Pickup), scheduledDate) : ParseTimeToDateTime(first.Customer.Pickup, scheduledDate);
                            var etdTime = colMap.Etd != -1 ? GetSafeTime(first.Row.Cell(colMap.Etd), scheduledDate) : ParseTimeToDateTime(first.Customer.ETD, scheduledDate);
                            string? route = colMap.Route != -1 ? GetSafeString(first.Row.Cell(colMap.Route)) : first.Customer.Route;
                            string? cycle = colMap.Cycle != -1 ? GetSafeString(first.Row.Cell(colMap.Cycle)) : first.Customer.Cycle;

                             var schedule = new DeliverySchedule
                            {
                                ScheduleNumber = finalScheduleNumber,
                                CustomerId = group.Key.CustomerId,
                                ScheduledDate = scheduledDate,
                                Route = route,
                                Cycle = cycle,
                                Area = first.DockLocation, // Map DOCK string to Area property
                                EnterDockTime = enterDockTime,
                                PickupTime = pickupTime,
                                ETD = etdTime,
                                Status = "Scheduled",
                                CreatedDate = DateTime.Now,
                                CreatedBy = User.Identity?.Name ?? "ImportExcel"
                            };

                            // Group items within this manifest to combine duplicates
                            var itemGroups = group.Where(g => g.MatchedItem != null)
                                                 .GroupBy(g => g.MatchedItem!.ItemId);

                            foreach (var itemGroup in itemGroups)
                            {
                                var totalQty = itemGroup.Sum(ig => ig.Quantity);
                                
                                schedule.DeliveryItems.Add(new DeliveryItem {
                                    ItemId = itemGroup.Key,
                                    Quantity = totalQty,
                                    ActualQuantity = 0,
                                    CreatedDate = DateTime.Now
                                });
                            }

                            // Handling orphan items
                            var unknownItems = group.Where(g => g.MatchedItem == null && !string.IsNullOrEmpty(g.ItemCode))
                                                   .Select(g => g.ItemCode)
                                                   .Distinct();
                            
                            if (unknownItems.Any())
                            {
                                schedule.Notes = "[Warning] Part(s) tidak dikenal: " + string.Join(", ", unknownItems);
                            }

                            if (schedule.DeliveryItems.Any())
                            {
                                schedules.Add(schedule);
                                successCount++;
                            }
                            else
                            {
                                errorCount++;
                                if (errorSamples.Count < 5 && unknownItems.Any()) 
                                    errorSamples.Add($"Manifest {finalScheduleNumber}: Semua part tidak dikenal ({string.Join(", ", unknownItems)})");
                                else if (errorSamples.Count < 5)
                                    errorSamples.Add($"Manifest {finalScheduleNumber}: Tidak ada data item yang valid.");
                            }
                        }

                        if (schedules.Any())
                        {
                            _context.DeliverySchedules.AddRange(schedules);
                            await _context.SaveChangesAsync();
                        }
                    }
                }

                if (successCount > 0)
                {
                    TempData["SuccessMessage"] = $"✅ Berhasil import {successCount} schedule dengan Items!";
                }
                
                if (errorCount > 0)
                {
                     TempData["ErrorMessage"] = $"⚠️ {errorCount} baris gagal. Contoh: {string.Join(", ", errorSamples)}";
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
        public string? DockLocation { get; set; } // Added for DOCK location support
        public Item? MatchedItem { get; set; }
        public int Quantity { get; set; }
        public IXLRow Row { get; set; } = null!;
    }
}
