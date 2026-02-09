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

        public async Task<IActionResult> Index()
        {
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .ThenInclude(di => di.Item)
                .Where(s => s.Status == "Scheduled" || s.Status == "In Progress")
                .OrderBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();

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
        public async Task<IActionResult> Create(int? SelectedItemId, decimal? SelectedItemQty, [Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,PickupTime,ETD,Range,SKID,Area,TotalTargetQuantity,Notes,Status")] DeliverySchedule deliverySchedule)
        {
            if (ModelState.IsValid)
            {
                // Auto-generate schedule number ONLY if empty (allows manual Manifest input)
                if (string.IsNullOrEmpty(deliverySchedule.ScheduleNumber))
                {
                    int sequence = await GetNextSequenceInternal(deliverySchedule.ScheduledDate);
                    deliverySchedule.ScheduleNumber = $"SCH-{deliverySchedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                }
                
                deliverySchedule.CreatedDate = DateTime.Now;
                deliverySchedule.CreatedBy = User.Identity?.Name ?? "PreparationPortal";

                _context.Add(deliverySchedule);
                
                // If Item is selected, create DeliveryItem
                if (SelectedItemId.HasValue && SelectedItemQty.HasValue)
                {
                    var dItem = new DeliveryItem
                    {
                        DeliverySchedule = deliverySchedule, // EF will link it
                        ItemId = SelectedItemId.Value,
                        Quantity = SelectedItemQty.Value,
                        ActualQuantity = 0,
                        CreatedDate = DateTime.Now
                    };
                    _context.DeliveryItems.Add(dItem);
                    
                    // Update total target quantity if not set
                    if (deliverySchedule.TotalTargetQuantity == 0)
                        deliverySchedule.TotalTargetQuantity = (int)SelectedItemQty.Value;
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            
            // Debug: Capture all validation errors
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (errors.Any())
            {
                TempData["ErrorMessage"] = "Validation Errors: " + string.Join(" | ", errors);
            }

            ViewData["CustomerId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Customers, "CustomerId", "CustomerName", deliverySchedule.CustomerId);
            
            var itemList = _context.Items
                .OrderBy(i => i.ItemName)
                .Select(i => new {
                    ItemId = i.ItemId,
                    DisplayName = $"{i.ItemName} | Rack: {i.Rack}-{i.NoRack} | VIN: {i.VIN}"
                })
                .ToList();

            ViewData["Items"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(itemList, "ItemId", "DisplayName", SelectedItemId);
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

                // Headers sesuai gambar User
                var headers = new[] { 
                    "MANIFESTING", "DOCK", "KODE CUSTOMER", "NAMA CUSTOMER", 
                    "ROUTE", "CYCLE", "PART NO / VIN", "QTY/LOT", "QTY (PCS)", "KANBAN" 
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

                worksheet.Cell(2, 1).Value = "MAN-001"; // Manifesting Example
                worksheet.Cell(2, 2).Value = sampleCust?.Docking ?? "08:00"; // Dock
                worksheet.Cell(2, 3).Value = sampleCust?.CustomerCode ?? "CUST001"; // Kode Customer
                worksheet.Cell(2, 4).Value = sampleCust?.CustomerName ?? "PT. CONTOH"; // Nama Customer
                worksheet.Cell(2, 5).Value = sampleCust?.Route ?? "R1"; // Route
                worksheet.Cell(2, 6).Value = sampleCust?.Cycle ?? "C1"; // Cycle
                worksheet.Cell(2, 7).Value = sampleItem?.CustomerPartNumber ?? "PART-001"; // Item / Part No
                worksheet.Cell(2, 8).Value = sampleItem?.QtyLot?.ToString() ?? "10"; // Qty/Lot
                worksheet.Cell(2, 9).Value = 500; // Qty (Pcs)
                worksheet.Cell(2, 10).Value = sampleItem?.KanbanType ?? "E-KANBAN"; // Kanban

                // Border
                headerRange.RangeUsed().Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Columns().AdjustToContents();

                // Instructions
                worksheet.Cell(4, 2).Value = "CATATAN PENGISIAN:";
                worksheet.Cell(5, 2).Value = "• Gunakan format ini untuk Upload Jadwal.";
                worksheet.Cell(6, 2).Value = "• Kolom KODE CUSTOMER dan PART NO / VIN Wajib diisi.";
                worksheet.Cell(7, 2).Value = "• Data ETD, PICKUP, SKID, AREA akan diambil otomatis dari Master Customer.";

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Jadwal_Preparation.xlsx");
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
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
                // 1. Load Customers for Lookup
                var customers = await _context.Customers.ToDictionaryAsync(c => c.CustomerCode.Trim().ToUpper(), c => c);

                // 2. Load Items for "Translation" (Manifest -> VIN)
                var allItems = await _context.Items.Where(i => i.IsActive).ToListAsync();
                var itemMap = new Dictionary<string, Item>();

                foreach (var item in allItems)
                {
                    // Prioritize Manifest/CustomerPartNumber for mapping
                    if (!string.IsNullOrWhiteSpace(item.CustomerPartNumber))
                    {
                        var key = item.CustomerPartNumber.Trim().ToUpper();
                        if (!itemMap.ContainsKey(key)) itemMap[key] = item;
                    }
                    // Fallback to VIN
                    if (!string.IsNullOrWhiteSpace(item.VIN))
                    {
                        var keyVin = item.VIN.Trim().ToUpper();
                        if (!itemMap.ContainsKey(keyVin)) itemMap[keyVin] = item;
                    }
                }

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);

                        // --- Robust Header Detection ---
                        var headerRow = worksheet.Row(1);
                        // Updated keywords for new template
                         var targetKeywords = new[] { 
                            "MANIFESTING", "DOCK", "KODE", "CUSTOMER", "ROUTE", 
                            "CYCLE", "ITEM", "PART", "QTY", "KANBAN" 
                        };

                        for (int r = 1; r <= 30; r++) // Scan first 30 rows
                        {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 30; c++)
                            {
                                var val = NormalizeHeader(GetSafeString(testRow.Cell(c)));
                                foreach (var k in targetKeywords) if (val.Contains(k)) currentScore++;
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

                        // Column Mapping (Updated to match Image)
                        var colMap = new
                        {
                            Manifesting = FindCol("MANIFESTING", "MANIFEST"), // New Header
                            Dock = FindCol("DOCK", "DOCKING", "ENTER DOCK"),
                            Cust = FindCol("KODE CUSTOMER", "KODE", "CUST"),
                            CustName = FindCol("NAMA CUSTOMER", "NAMA"),
                            Route = FindCol("ROUTE", "RUTE"),
                            Cycle = FindCol("CYCLE", "SIKLUS"),
                            ItemPartNo = FindCol("ITEM / PART NO", "ITEM", "PART NO", "PART"),
                            QtyLot = FindCol("QTY/LOT", "LOT"),
                            QtyPcs = FindCol("QTY (PCS)", "QTY PCS", "QTY"),
                            Kanban = FindCol("KANBAN"),
                            
                            // Legacy/Hidden Columns (Fallback)
                            Pickup = FindCol("PICKUP", "PENJEMPUTAN"),
                            Etd = FindCol("ETD"),
                            Skid = FindCol("SKID", "PALLET"),
                            Area = FindCol("AREA"),
                            Target = FindCol("QTYTARGET", "TARGET", "QTY_TOTAL") // Target total schedule
                        };

                        if (colMap.Cust == -1)
                        {
                            TempData["ErrorMessage"] = "Header 'KODE CUSTOMER' tidak ditemukan. Pastikan file Excel sesuai template.";
                            return RedirectToAction(nameof(Index));
                        }

                        var schedules = new List<DeliverySchedule>();
                        // Default Date: Today (or logic to pick from excel if exists, but strictly Today for now as per requirement)
                        var scheduledDate = DateTime.Today; 
                        int sequenceNumber = await GetNextSequenceInternal(scheduledDate);

                        // Process Rows
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber());
                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;

                            try
                            {
                                string custCode = GetSafeString(row.Cell(colMap.Cust)).Trim().ToUpper();
                                if (string.IsNullOrEmpty(custCode)) continue;

                                if (!customers.TryGetValue(custCode, out var customer))
                                {
                                    throw new Exception($"Customer Code '{custCode}' tidak ditemukan di database.");
                                }

                                // Parsing Data
                                string? route = colMap.Route != -1 ? GetSafeString(row.Cell(colMap.Route)) : customer.Route;
                                string? cycle = colMap.Cycle != -1 ? GetSafeString(row.Cell(colMap.Cycle)) : customer.Cycle;
                                string? area = colMap.Area != -1 ? GetSafeString(row.Cell(colMap.Area)) : customer.Area;
                                string? skid = colMap.Skid != -1 ? GetSafeString(row.Cell(colMap.Skid)) : customer.SKID; // Use SKID prop

                                // Times (Fallback to Master if not in Excel)
                                var enterDockTime = colMap.Dock != -1 ? GetSafeTime(row.Cell(colMap.Dock), scheduledDate) : ParseTimeToDateTime(customer.Docking, scheduledDate);
                                var pickupTime = colMap.Pickup != -1 ? GetSafeTime(row.Cell(colMap.Pickup), scheduledDate) : ParseTimeToDateTime(customer.Pickup, scheduledDate);
                                var etdTime = colMap.Etd != -1 ? GetSafeTime(row.Cell(colMap.Etd), scheduledDate) : ParseTimeToDateTime(customer.ETD, scheduledDate);

                                // Mapping Item Decision: MANIFESTING is Key!
                                DeliveryItem? dItem = null;
                                int qtyPcs = colMap.QtyPcs != -1 ? GetSafeInt(row.Cell(colMap.QtyPcs)) : 0;
                                string itemSearchKey = "";
                                string keySource = "";

                                // Priority 1: Check MANIFESTING column
                                if (colMap.Manifesting != -1)
                                {
                                    string val = GetSafeString(row.Cell(colMap.Manifesting)).Trim().ToUpper();
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        itemSearchKey = val;
                                        keySource = "Manifesting";
                                    }
                                }

                                // Priority 2: Check ITEM / PART NO (Fallback)
                                if (string.IsNullOrEmpty(itemSearchKey) && colMap.ItemPartNo != -1)
                                {
                                    string val = GetSafeString(row.Cell(colMap.ItemPartNo)).Trim().ToUpper();
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        itemSearchKey = val;
                                        keySource = "Item/PartNo";
                                    }
                                }

                                if (!string.IsNullOrEmpty(itemSearchKey))
                                {
                                    if (itemMap.TryGetValue(itemSearchKey, out var matchedItem))
                                    {
                                        dItem = new DeliveryItem
                                        {
                                            ItemId = matchedItem.ItemId,
                                            Quantity = qtyPcs,
                                            ActualQuantity = 0,
                                            CreatedDate = DateTime.Now
                                        };
                                    }
                                    else
                                    {
                                         // Log warning if item not found
                                         // If key came from Manifesting, it's critical
                                         // We can add a note to the schedule
                                    }
                                }

                                int targetQty = colMap.Target != -1 ? GetSafeInt(row.Cell(colMap.Target)) : qtyPcs; // Fallback to item qty if total not specified

                                // Adjust Days for Overnight logic
                                if (enterDockTime.HasValue && pickupTime.HasValue && enterDockTime.Value.TimeOfDay > pickupTime.Value.TimeOfDay)
                                    enterDockTime = enterDockTime.Value.AddDays(-1);

                                if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value.TimeOfDay < pickupTime.Value.TimeOfDay)
                                    etdTime = etdTime.Value.AddDays(1);

                                 // Manifest logic: Use provided Manifest from Excel if available
                                 string? manifestCode = colMap.Manifesting != -1 ? GetSafeString(row.Cell(colMap.Manifesting)).Trim().ToUpper() : null;
                                 string scheduleNumber = "";

                                 if (!string.IsNullOrEmpty(manifestCode))
                                 {
                                     scheduleNumber = manifestCode;
                                 }
                                 else
                                 {
                                     // Generate fallback Schedule Number
                                     var prefix = $"SCH-{scheduledDate:yyyyMMdd}";
                                     scheduleNumber = $"{prefix}{sequenceNumber:D3}";
                                     sequenceNumber++;
                                 }

                                var schedule = new DeliverySchedule
                                {
                                    ScheduleNumber = scheduleNumber,
                                    CustomerId = customer.CustomerId,
                                    ScheduledDate = scheduledDate,
                                    Route = route,
                                    Cycle = cycle,
                                    EnterDockTime = enterDockTime,
                                    PickupTime = pickupTime,
                                    ETD = etdTime,
                                    Range = (etdTime.HasValue && pickupTime.HasValue) ? (etdTime.Value - pickupTime.Value).ToString(@"hh\:mm") : null,
                                    SKID = skid,
                                    Area = area,
                                    TotalTargetQuantity = targetQty,
                                    Status = "Scheduled",
                                    CreatedDate = DateTime.Now,
                                    CreatedBy = User.Identity?.Name ?? "ImportExcel"
                                };
                                
                                if (dItem != null)
                                {
                                    schedule.DeliveryItems.Add(dItem);
                                }
                                else if (colMap.ItemPartNo != -1 && qtyPcs > 0)
                                {
                                     // Case: Item existed in Excel but not found in DB
                                     string partVal = GetSafeString(row.Cell(colMap.ItemPartNo));
                                     schedule.Notes = $"[Warning] Part '{partVal}' tidak dikenal sistem.";
                                }

                                schedules.Add(schedule);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: {ex.Message}");
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
}
