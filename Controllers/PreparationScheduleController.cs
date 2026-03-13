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
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin")]
    public class PreparationScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly PreparationSyncService _syncService;
        private readonly SmartImportService _smartImportService;
        private readonly ILogger<PreparationScheduleController> _logger;

        public PreparationScheduleController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, PreparationSyncService syncService, SmartImportService smartImportService, ILogger<PreparationScheduleController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _syncService = syncService;
            _smartImportService = smartImportService;
            _logger = logger;
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

            // Tampilkan jadwal tanggal yang dipilih + carry-over (jadwal lama yang belum selesai)
            // Carry-over hanya berlaku untuk hari ini (bukan historical view) agar tidak membingungkan
            var isToday = dateToFilter.Date == DateTime.Today;
            var filteredQuery = isToday
                ? query.Where(s => s.ScheduledDate.Date == dateToFilter.Date ||
                                   // Carry-over: jadwal dari hari sebelumnya yang belum Completed/Cancelled
                                   (s.ScheduledDate.Date < dateToFilter.Date && s.Status != "Completed" && !s.ActualEndTime.HasValue))
                : query.Where(s => s.ScheduledDate.Date == dateToFilter.Date);
            
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
            // Urutan PER-ITEM: Preparing (ada scan, belum 100%) → Waiting (belum mulai) → Completed (sudah 100%)
            // Sort berdasarkan status ITEM bukan schedule, agar yang baru di-scan selalu naik ke atas
            var flatRows = rawSchedules
                .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.Select(di => (Schedule: s, Item: di))
                    : new[] { (Schedule: s, Item: (DeliveryItem)null!) })
                .OrderBy(row =>
                {
                    if (row.Item == null) return 1; // Ghost schedule = Waiting
                    var actual = row.Item.ActualQuantity ?? 0;
                    var target = row.Item.Quantity;
                    if (actual > 0 && actual < target) return 0; // Preparing (sedang dikerjakan)
                    if (actual == 0) return 1;                    // Waiting (belum mulai)
                    if (actual >= target) return 2;               // Completed (sudah selesai)
                    return 1;
                })
                .ThenByDescending(row => row.Item?.ActualQuantity ?? 0) // Yang baru di-scan (qty terbanyak) naik ke atas
                .ThenBy(row => row.Schedule.ScheduledDate)
                .ThenBy(row => row.Schedule.ScheduleNumber)
                .ThenBy(row => row.Schedule.ScheduleId)
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

            // Hitung pending preparations (belum terikat jadwal)
            ViewBag.PendingPrepCount = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null);

            return View(schedules);
        }

        /// <summary>
        /// Manual trigger untuk sinkronisasi pending preparations ke jadwal yang sudah ada.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncPendingNow()
        {
            int synced = await _syncService.SyncAllPendingAsync();
            TempData["SuccessMessage"] = synced > 0
                ? $"✅ {synced} record preparation PENDING berhasil disinkronisasi ke jadwal."
                : "ℹ️ Tidak ada pending preparation yang bisa disinkronisasi saat ini (belum ada jadwal yang sesuai, atau memang sudah kosong).";
            return RedirectToAction(nameof(Index));
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

                    // Auto-sync pending preparations ke jadwal yang baru dibuat
                    int createSynced = await _syncService.SyncAllPendingAsync();
                    if (createSynced > 0)
                    {
                        TempData["InfoMessage"] = $"{createSynced} record preparation PENDING telah tersinkronisasi ke jadwal baru.";
                    }

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

                        // Auto-sync pending preparations ke jadwal yang baru dibuat
                        int synced = await _syncService.SyncAllPendingAsync();
                        if (synced > 0)
                        {
                            TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal baru.";
                        }

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

        // Download Excel Template (Multi-Sheet per Customer)
        public async Task<IActionResult> DownloadTemplate()
        {
            // Custom sort order: ADM, TMMIN, HINO, AHM, HYUNDAI first, then rest alphabetically
            var priorityOrder = new[] { "ADM", "TMMIN", "HINO", "AHM", "HYUNDAI" };
            var allCustomers = await _context.Customers
                .Where(c => c.IsActive)
                .ToListAsync();
            
            var customers = allCustomers
                .OrderBy(c => {
                    var code = (c.CustomerCode ?? "").ToUpper();
                    for (int i = 0; i < priorityOrder.Length; i++)
                        if (code.Contains(priorityOrder[i])) return i;
                    return priorityOrder.Length; // others come last
                })
                .ThenBy(c => c.CustomerCode)
                .ThenBy(c => c.CustomerName)
                .ToList();

            using (var workbook = new XLWorkbook())
            {
                // ─── PANDUAN sheet (first) ───────────────────────────────────────
                var guideSheet = workbook.Worksheets.Add("PANDUAN");
                guideSheet.Cell(1, 1).Value = "PANDUAN TEMPLATE JADWAL PREPARATION";
                guideSheet.Cell(1, 1).Style.Font.Bold = true;
                guideSheet.Cell(1, 1).Style.Font.FontSize = 14;
                guideSheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#f59e0b");
                guideSheet.Cell(3, 1).Value = "1. Template ini berisi satu sheet untuk setiap Customer yang aktif.";
                guideSheet.Cell(4, 1).Value = "2. Isi data pada sheet Customer yang ingin di-import (boleh lebih dari satu sheet).";
                guideSheet.Cell(5, 1).Value = "3. Kolom: MANIFESTING | PART NO / VIN | QTY (PCS) — jangan ubah nama kolom header.";
                guideSheet.Cell(6, 1).Value = "4. Baris 1 (abu-abu) adalah info sistem — JANGAN dihapus atau diubah.";
                guideSheet.Cell(7, 1).Value = "5. Saat upload, pilihan Customer di dropdown bersifat opsional (override semua sheet).";
                guideSheet.Column(1).Width = 75;

                // ─── One sheet per customer ──────────────────────────────────────
                var headers = new[] { "MANIFESTING", "PART NO / VIN", "QTY (PCS)" };
                var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var c in customers)
                {
                    // Sheet name = Dock (CustomerName) + Cycle, max 31 chars (Excel limit)
                    string rawName = $"{c.CustomerName}-{c.Cycle}";
                    // Replace invalid chars: \ / ? * [ ] :
                    rawName = System.Text.RegularExpressions.Regex.Replace(rawName, @"[\\/?*\[\]:]", "-").Trim();
                    if (rawName.Length > 28) rawName = rawName.Substring(0, 28); // reserve 3 chars for dedup suffix

                    // Deduplicate: append (2), (3), ... if name already used
                    string sheetName = rawName;
                    int dupCounter = 2;
                    while (usedSheetNames.Contains(sheetName))
                    {
                        string suffix = $"({dupCounter++})";
                        sheetName = rawName.Substring(0, Math.Min(rawName.Length, 31 - suffix.Length)) + suffix;
                    }
                    usedSheetNames.Add(sheetName);

                    var ws = workbook.Worksheets.Add(sheetName);

                    // Row 1: system info (CUSTOMER_ID) — gray, small font
                    ws.Cell(1, 1).Value = "CUSTOMER_ID";
                    ws.Cell(1, 2).Value = c.CustomerId.ToString();
                    ws.Cell(1, 3).Value = c.CustomerCode ?? "";
                    var infoRange = ws.Range(1, 1, 1, 3);
                    infoRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#94a3b8");
                    infoRange.Style.Font.FontColor = XLColor.FromHtml("#1e293b");
                    infoRange.Style.Font.FontSize = 8;
                    infoRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    // Row 2: headers
                    for (int i = 0; i < headers.Length; i++)
                        ws.Cell(2, i + 1).Value = headers[i];
                    var headerRange = ws.Range(2, 1, 2, headers.Length);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // Row 3-4: brief instructions (no sample data - sheet stays empty if user doesn't fill)
                    ws.Cell(3, 1).Value = "Keterangan:";
                    ws.Cell(3, 1).Style.Font.Bold = true;
                    ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
                    ws.Cell(4, 1).Value = $"Customer: {c.CustomerCode} | Dock: {c.CustomerName} | Route: {c.Route} | Cycle: {c.Cycle}";
                    ws.Cell(4, 1).Style.Font.FontColor = XLColor.Gray;
                    ws.Cell(5, 1).Value = "Hapus baris Keterangan ini, lalu isi data mulai baris 3.";
                    ws.Cell(5, 1).Style.Font.FontColor = XLColor.Gray;

                    ws.Columns().AdjustToContents();
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Jadwal_Preparation_AllCustomer.xlsx");
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
                        // ─── PHASE 1: Collect All Valid Data Rows (multi-sheet loop) ───
                        var validRowsData = new List<ExcelRowData>();

                        foreach (var worksheet in workbook.Worksheets)
                        {
                        // Skip guide/info sheets
                        string wsName = worksheet.Name.Trim().ToUpper();
                        if (wsName == "PANDUAN" || wsName == "INFO" || wsName == "GUIDE") continue;

                        // --- Detect CUSTOMER_ID info row (multi-sheet template) ---
                        Customer? sheetCustomer = null;
                        int headerStartRow = 1;
                        if (worksheet.Cell(1, 1).GetString().Trim().ToUpper() == "CUSTOMER_ID")
                        {
                            if (int.TryParse(worksheet.Cell(1, 2).GetString().Trim(), out int sheetCustId))
                                sheetCustomer = allCustomers.FirstOrDefault(c => c.CustomerId == sheetCustId);
                            headerStartRow = 2;
                        }
                        
                        // --- Try to extract customer from sheet name (e.g., "ADM EXP-TUE" -> ADM) ---
                        if (sheetCustomer == null)
                        {
                            // Try matching sheet name prefix with customer code, name, or dock
                            string sheetNameClean = NormalizeHeader(worksheet.Name);
                            var sheetNameParts = worksheet.Name.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            foreach (var part in sheetNameParts)
                            {
                                if (string.IsNullOrWhiteSpace(part) || part.Length < 2) continue;
                                var partNorm = NormalizeHeader(part);
                                
                                // Try match by customer code (exact or prefix)
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.CustomerCode ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase) ||
                                    NormalizeHeader(c.CustomerCode ?? "").StartsWith(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by customer name (prefix)
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.CustomerName ?? "").StartsWith(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by dock/docking
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.Docking ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by area
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.Area ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                            }
                        }
                        
                        // Form dropdown override takes priority over sheet CUSTOMER_ID
                        Customer? sheetEffectiveCustomer = chosenCustomer ?? sheetCustomer;

                        // --- Robust Header Detection ---
                        var headerRow = worksheet.Row(headerStartRow);
                        var targetKeywords = new[] { 
                            "MANIFESTING", "DOCK", "KODE", "CUSTOMER", "ROUTE", 
                            "CYCLE", "ITEM", "PART", "QTY", "KANBAN" 
                        };

                        for (int r = headerStartRow; r <= headerStartRow + 9; r++) // Scan up to 10 rows from header start
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

                        if (colMap.CustName == -1 && sheetEffectiveCustomer == null) {
                            errorCount++;
                            // Extract first word from sheet name for error message
                            var firstWord = worksheet.Name.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? worksheet.Name;
                            if (errorSamples.Count < 5) errorSamples.Add($"Sheet '{worksheet.Name}': Customer '{firstWord}' tidak ditemukan di master. Tambahkan customer atau pilih Customer di dropdown sebelum import.");
                            continue; // skip this sheet, continue to next
                        }

                        // --- Collect rows for this sheet (fill-down vars reset per sheet) ---
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()).ToList();
                        
                        // Skip sheet if no data rows
                        if (!rows.Any()) continue;
                        
                        // Skip sheet if no real data (check Part No/VIN column for non-empty values)
                        int realDataCol = colMap.ItemPartNo != -1 ? colMap.ItemPartNo : (colMap.PartNo != -1 ? colMap.PartNo : colMap.Vin);
                        bool hasRealData = rows.Any(r => {
                            if (r.IsEmpty()) return false;
                            if (realDataCol == -1) return false;
                            string val = GetSafeString(r.Cell(realDataCol)).Trim();
                            // Skip rows that look like instructions (start with "Keterangan", "Hapus", "Customer:", etc.)
                            if (string.IsNullOrEmpty(val)) return false;
                            if (val.StartsWith("Keterangan", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Hapus", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Customer:", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Isi data", StringComparison.OrdinalIgnoreCase)) return false;
                            return true;
                        });
                        if (!hasRealData) continue; // skip this sheet - no user data
                        
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
                                Customer? customer = sheetEffectiveCustomer; // PRIORITAS 1: form override or sheet CUSTOMER_ID
                                
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
                                    Row = row,
                                    PickupColIndex = colMap.Pickup,
                                    EtdColIndex = colMap.Etd
                                });
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) errorSamples.Add($"Sheet '{worksheet.Name}' Baris {row.RowNumber()}: {ex.Message}");
                            }
                        } // end foreach row

                        } // end foreach worksheet

                        // --- PHASE 2: Group by ManifestNum → 1 DeliverySchedule per manifest ---
                        var scheduledDate = DateTime.Today;
                        var importSession = DateTime.Now.ToString("HHmm");

                        // Group rows by ManifestNum (empty manifest treated as separate schedules)
                        var groupedByManifest = validRowsData
                            .GroupBy(r => string.IsNullOrEmpty(r.ManifestNum) ? Guid.NewGuid().ToString() : r.ManifestNum.Trim().ToUpper())
                            .ToList();

                        // Pre-load existing manifests to check for duplicates
                        var manifestNumbers = groupedByManifest
                            .Where(g => !string.IsNullOrEmpty(g.First().ManifestNum))
                            .Select(g => g.First().ManifestNum.Trim().ToUpper())
                            .ToList();
                        
                        var existingManifests = await _context.DeliverySchedules
                            .Where(s => s.ScheduledDate.Date == scheduledDate && 
                                        manifestNumbers.Contains(s.ScheduleNumber.ToUpper()))
                            .Select(s => s.ScheduleNumber.ToUpper())
                            .Distinct()
                            .ToListAsync();
                        
                        int skippedCount = 0;
                        var skippedManifests = new List<string>();

                        int sequentialCounter = 1;
                        foreach (var manifestGroup in groupedByManifest)
                        {
                            var firstRow = manifestGroup.First();
                            var customer = firstRow.Customer;
                            var manifestNum = firstRow.ManifestNum;

                            // Gunakan manifest number langsung tanpa suffix
                            var scheduleNumber = string.IsNullOrEmpty(manifestNum)
                                ? $"SCH-{scheduledDate:yyyyMMdd}-{sequentialCounter++}"
                                : manifestNum.Trim();

                            // Skip jika manifest sudah ada di database
                            if (!string.IsNullOrEmpty(manifestNum) && existingManifests.Contains(manifestNum.Trim().ToUpper()))
                            {
                                skippedCount++;
                                if (skippedManifests.Count < 5)
                                    skippedManifests.Add(manifestNum);
                                continue;
                            }

                            var schedule = new DeliverySchedule
                            {
                                ScheduleNumber = scheduleNumber,
                                CustomerId = customer.CustomerId,
                                ScheduledDate = scheduledDate,
                                Status = "Scheduled",
                                CreatedDate = DateTime.Now,
                                CreatedBy = User.Identity?.Name ?? "ImportExcel",
                                Route = !string.IsNullOrEmpty(customer.Route) ? customer.Route : firstRow.Route,
                                Cycle = !string.IsNullOrEmpty(customer.Cycle) ? customer.Cycle : firstRow.Cycle,
                                Area = !string.IsNullOrEmpty(customer.Area) ? customer.Area : firstRow.DockLocation,
                                StartPrepareTime = customer.StartPrepareTime,
                                StdPrepareTime = customer.StdPrepareTime,
                                Range = customer.Range,
                                SKID = customer.SKID
                            };

                            var enterDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                            var pickupTime = (firstRow.PickupColIndex != -1 ? GetSafeTime(firstRow.Row.Cell(firstRow.PickupColIndex), scheduledDate) : null)
                                            ?? ParseTimeToDateTime(customer.Pickup, scheduledDate);
                            var etdTime = (firstRow.EtdColIndex != -1 ? GetSafeTime(firstRow.Row.Cell(firstRow.EtdColIndex), scheduledDate) : null)
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

                            // Tambahkan semua item dari manifest yang sama
                            decimal totalQty = 0;
                            foreach (var rowData in manifestGroup)
                            {
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
                                totalQty += rowData.Quantity;
                                itemCount++;
                            }

                            schedule.TotalTargetQuantity = totalQty;
                            _context.DeliverySchedules.Add(schedule);
                            successCount++;
                        }

                        // SAVE EVERYTHING
                        await _context.SaveChangesAsync();

                        // Hitung manifest dengan multiple items (grouped)
                        int groupedManifests = groupedByManifest.Count(g => g.Count() > 1);
                        int groupedItems = groupedByManifest.Where(g => g.Count() > 1).Sum(g => g.Count());
                        
                        // Build success message dengan info grouping
                        var successMsg = $"Berhasil import {successCount} schedule ({itemCount} item baris) dari Excel!";
                        if (groupedManifests > 0)
                        {
                            successMsg += $" ({groupedManifests} manifest ter-group dengan {groupedItems} item)";
                        }
                        TempData["SuccessMessage"] = successMsg;

                        // Auto-sync pending preparations ke jadwal yang baru di-import
                        int synced = await _syncService.SyncAllPendingAsync();
                        if (synced > 0)
                        {
                            TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal yang baru di-import.";
                        }

                        // Notifikasi untuk manifest yang di-skip karena duplikat
                        if (skippedCount > 0)
                        {
                            var skippedList = string.Join(", ", skippedManifests.Take(5));
                            if (skippedCount > 5) skippedList += $" (+{skippedCount - 5} lagi)";
                            TempData["WarningMessage"] = $"{skippedCount} manifest di-skip karena sudah ada di database: {skippedList}";
                        }

                        // Notify Dashboard via SignalR
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                        {
                            action = "import",
                            message = $"Berhasil import {successCount} schedule preparation dari Excel.",
                            timestamp = DateTime.Now
                        });
                    }
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

        #region === Smart Import Endpoints ===

        /// <summary>
        /// Step 1: Preview — upload file, auto-detect customer, extract items
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SmartImportPreview(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, errorMessage = "File tidak valid." });

            if (file.Length > 10 * 1024 * 1024) // 10MB limit
                return Json(new { success = false, errorMessage = "Ukuran file melebihi 10MB." });

            var result = await _smartImportService.ProcessFileAsync(file);

            // Get all customers for manual selection dropdown (grouped by dock)
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode).ThenBy(c => c.CustomerName)
                .Select(c => new { c.CustomerId, c.CustomerCode, c.CustomerName, c.Route, c.Cycle, c.Docking, c.Area })
                .ToListAsync();

            // VIN lookup: translate each extracted Part No → VIN via ItemMappings
            // Priority: ItemMappings.CustomerPartNumber → Items.CustomerPartNumber → Items.VIN/ItemCode
            var allMappingsPreview = await _context.ItemMappings.AsNoTracking().ToListAsync();
            var allItemsPreview    = await _context.Items.Where(i => i.IsActive).AsNoTracking()
                .Select(i => new { i.VIN, i.CustomerPartNumber, i.ItemCode }).ToListAsync();

            // Normalize Part No for flexible matching: remove dashes/spaces, uppercase
            // e.g. "16261-0Y030-00" → "162610Y03000", "16261 0Y030 00" → "162610Y03000"
            static string NormalizePartNo(string? s) =>
                string.IsNullOrWhiteSpace(s) ? "" :
                System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");

            // Normalize VIN-like strings for comparison using VinHelper + remove dashes/spaces
            static string NormalizeVinForCompare(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                var cleaned = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");
                return DeliveryControl.Helpers.VinHelper.Normalize(cleaned);
            }

            string? ResolveVin(string partNo)
            {
                if (string.IsNullOrWhiteSpace(partNo)) return null;

                string pNorm = NormalizePartNo(partNo);   // e.g. "162610Y03000"
                string pUpper = partNo.Trim().ToUpper();  // e.g. "16261-0Y030-00"
                string pVinNorm = NormalizeVinForCompare(partNo);

                // P1: ItemMappings → CustomerPartNumber  (exact, then normalized)
                var map = allMappingsPreview.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.CustomerPartNumber) && (
                        m.CustomerPartNumber.Trim().ToUpper() == pUpper ||
                        NormalizePartNo(m.CustomerPartNumber) == pNorm));
                if (map != null && !string.IsNullOrWhiteSpace(map.VIN))
                {
                    // Prefer master Item VIN if exists (map.VIN might include LB suffix while Items store without)
                    var mappedItem = allItemsPreview.FirstOrDefault(i =>
                        !string.IsNullOrWhiteSpace(i.VIN) && NormalizeVinForCompare(i.VIN) == NormalizeVinForCompare(map.VIN));
                    return mappedItem != null && !string.IsNullOrWhiteSpace(mappedItem.VIN) ? mappedItem.VIN : map.VIN;
                }

                // P2: Items → CustomerPartNumber  (exact, then normalized)
                var item = allItemsPreview.FirstOrDefault(i =>
                    !string.IsNullOrWhiteSpace(i.CustomerPartNumber) && (
                        i.CustomerPartNumber.Trim().ToUpper() == pUpper ||
                        NormalizePartNo(i.CustomerPartNumber) == pNorm));
                if (item != null && !string.IsNullOrWhiteSpace(item.VIN))
                    return item.VIN;

                // P3: Items → VIN / ItemCode  (exact, then normalized or VIN-normalized)
                var item2 = allItemsPreview.FirstOrDefault(i =>
                    (!string.IsNullOrWhiteSpace(i.VIN) && (
                        i.VIN.Trim().ToUpper() == pUpper || NormalizePartNo(i.VIN) == pNorm || NormalizeVinForCompare(i.VIN) == pVinNorm)) ||
                    (!string.IsNullOrWhiteSpace(i.ItemCode) && (
                        i.ItemCode.Trim().ToUpper() == pUpper || NormalizePartNo(i.ItemCode) == pNorm)));
                if (item2 != null && !string.IsNullOrWhiteSpace(item2.VIN))
                    return item2.VIN;

                return null;
            }

            return Json(new
            {
                success = result.Success,
                errorMessage = result.ErrorMessage,
                detectedCustomer = result.DetectedCustomerName,
                detectedCustomerCode = result.DetectedCustomerCode,
                customerId = result.CustomerId,
                detectedDock = result.DetectedDock,
                detectedManifest = result.DetectedManifest,
                detectedRoute = result.DetectedRoute,
                detectedCycle = result.DetectedCycle,
                fileFormat = result.FileFormat,
                parserUsed = result.ParserUsed,
                items = result.Items.Select(i => new
                {
                    manifesting = i.Manifesting,
                    partNo = i.PartNo,
                    partName = i.PartName,
                    qty = i.Qty,
                    vin = ResolveVin(i.PartNo)   // VIN translated from ItemMappings / Items
                }),
                warnings = result.Warnings,
                // All matching docks for detected customer
                matchedDocks = result.MatchedDocks.Select(d => new
                {
                    customerId = d.CustomerId,
                    customerCode = d.CustomerCode,
                    dockName = d.DockName,
                    route = d.Route,
                    cycle = d.Cycle,
                    docking = d.Docking,
                    area = d.Area
                }),
                // All customers for full manual selection
                customerList = customers
            });
        }

        /// <summary>
        /// Step 2: Confirm — save extracted items as DeliverySchedule + DeliveryItems
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SmartImportConfirm([FromBody] SmartImportConfirmRequest request)
        {
            if (request == null || request.Items == null || !request.Items.Any())
                return Json(new { success = false, message = "Tidak ada item untuk di-import." });

            try
            {
                _logger.LogInformation("SmartImportConfirm: CustomerId={CustId}, Items={Count}",
                    request.CustomerId, request.Items.Count);

                // 1. Load Customer by the SELECTED dock (customerId from grid)
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId);
                if (customer == null)
                    return Json(new { success = false, message = "Customer tidak ditemukan di database." });

                _logger.LogInformation("SmartImportConfirm: Using Customer '{Code}' Dock='{Name}' (ID={Id})",
                    customer.CustomerCode, customer.CustomerName, customer.CustomerId);

                // 2. Load Items & Mappings for matching
                // allDbItems = master items from DB only (never mix with auto-created)
                var allDbItems = await _context.Items.Where(i => i.IsActive).ToListAsync();
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                // newItems = auto-created items this session (so we can reuse them across manifests)
                var newItems = new List<Item>();

                var scheduledDate = DateTime.Today;
                int successCount = 0;
                int itemCount = 0;
                int skippedCount = 0;

                // 3. Group items by manifest number
                var groupedByManifest = request.Items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.Manifesting) ? Guid.NewGuid().ToString() : i.Manifesting.Trim().ToUpper())
                    .ToList();

                // Check for existing manifests
                var manifestNumbers = groupedByManifest
                    .Where(g => !string.IsNullOrEmpty(g.First().Manifesting))
                    .Select(g => g.First().Manifesting!.Trim().ToUpper())
                    .ToList();

                var existingManifests = await _context.DeliverySchedules
                    .Where(s => s.ScheduledDate.Date == scheduledDate &&
                                manifestNumbers.Contains(s.ScheduleNumber.ToUpper()))
                    .Select(s => s.ScheduleNumber.ToUpper())
                    .Distinct()
                    .ToListAsync();

                int seqCounter = 1;
                foreach (var manifestGroup in groupedByManifest)
                {
                    var first = manifestGroup.First();
                    var manifestNum = first.Manifesting ?? "";

                    // Skip duplicate manifests
                    if (!string.IsNullOrEmpty(manifestNum) && existingManifests.Contains(manifestNum.Trim().ToUpper()))
                    {
                        skippedCount++;
                        continue;
                    }

                    var scheduleNumber = string.IsNullOrEmpty(manifestNum)
                        ? $"SCH-{scheduledDate:yyyyMMdd}-{seqCounter++}"
                        : manifestNum.Trim();

                    var schedule = new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = customer.CustomerId,
                        ScheduledDate = scheduledDate,
                        Status = "Scheduled",
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "SmartImport",
                        Route = customer.Route,
                        Cycle = customer.Cycle,
                        Area = customer.Area,
                        StartPrepareTime = customer.StartPrepareTime,
                        StdPrepareTime = customer.StdPrepareTime,
                        Range = customer.Range,
                        SKID = customer.SKID
                    };

                    // Set times from customer master
                    var enterDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                    var pickupTime = ParseTimeToDateTime(customer.Pickup, scheduledDate);
                    var etdTime = ParseTimeToDateTime(customer.ETD, scheduledDate);

                    var startPrepTime = scheduledDate.Date.AddMinutes(customer.StartPrepareTime);
                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime) enterDockTime = enterDockTime.Value.AddDays(1);
                    if (pickupTime.HasValue && pickupTime.Value < startPrepTime) pickupTime = pickupTime.Value.AddDays(1);
                    if (etdTime.HasValue && etdTime.Value < startPrepTime) etdTime = etdTime.Value.AddDays(1);
                    if (pickupTime.HasValue && enterDockTime.HasValue && pickupTime.Value < enterDockTime.Value) pickupTime = pickupTime.Value.AddDays(1);
                    if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value < pickupTime.Value) etdTime = etdTime.Value.AddDays(1);

                    schedule.EnterDockTime = enterDockTime;
                    schedule.PickupTime = pickupTime;
                    schedule.ETD = etdTime;

                    // Add items
                    decimal totalQty = 0;
                    foreach (var itemData in manifestGroup)
                    {
                        string itemCode = itemData.PartNo.Trim().ToUpper();
                        // Strip dashes/spaces for normalized comparison: "16261-0Y030-00" → "162610Y03000"
                        string itemCodeNorm = System.Text.RegularExpressions.Regex.Replace(itemCode, @"[-\s]", "");

                        // -----------------------------------------------------------------------
                        // FlexMatch: normalize both sides
                        //   1. Strip dashes/spaces  (Part No format: "16261-0Y030-00" → "162610Y03000")
                        //   2. VinHelper.Normalize  (strip suffix LBX/LB/X, prefix LB: "TA1680LB" → "TA1680")
                        // -----------------------------------------------------------------------
                        string NormFull(string? s)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return "";
                            var stripped = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");
                            return VinHelper.Normalize(stripped);
                        }
                        bool FlexMatch(string? a, string? b)
                        {
                            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
                            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                            var aN = NormFull(a); var bN = NormFull(b);
                            return !string.IsNullOrEmpty(aN) && !string.IsNullOrEmpty(bN) && aN == bN;
                        }

                        Item? matchedItem = null;
                        string? finalVin = null;

                        // Helper to search for item in DB list by multiple fields
                        Item? FindInDb(string code) =>
                            allDbItems.FirstOrDefault(i => FlexMatch(i.CustomerPartNumber, code)) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, code)) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, code));

                        // P0: Gunakan VIN yang sudah di-resolve saat preview (shortcut — paling akurat)
                        // VIN ini berasal dari ResolveVin() di SmartImportPreview, tidak perlu matching ulang
                        if (!string.IsNullOrWhiteSpace(itemData.Vin))
                        {
                            matchedItem = FindInDb(itemData.Vin);
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // P1: Direct match in master Items (CustomerPartNumber, VIN, ItemCode)
                        if (matchedItem == null)
                        {
                            matchedItem = FindInDb(itemCode);
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // P2: Via ItemMappings.CustomerPartNumber → resolve VIN → find Item
                        if (matchedItem == null)
                        {
                            var map = allMappings.FirstOrDefault(m => FlexMatch(m.CustomerPartNumber, itemCode));
                            if (map != null && !string.IsNullOrWhiteSpace(map.VIN))
                            {
                                // map.VIN might be "TA1680LB" while master item has "TA1680" — FlexMatch handles this
                                matchedItem = allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, map.VIN))
                                           ?? allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, map.VIN));
                                finalVin = matchedItem?.VIN ?? map.VIN;
                            }
                        }

                        // P3: Via ItemMappings.VIN (part no IS the VIN in mapping table)
                        if (matchedItem == null)
                        {
                            var mapByVin = allMappings.FirstOrDefault(m => FlexMatch(m.VIN, itemCode));
                            if (mapByVin != null && !string.IsNullOrWhiteSpace(mapByVin.VIN))
                            {
                                matchedItem = allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, mapByVin.VIN))
                                           ?? allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, mapByVin.VIN));
                                finalVin = matchedItem?.VIN ?? mapByVin.VIN;
                            }
                        }

                        // P4: Reuse auto-created item from this session (same part no appeared in earlier manifest)
                        if (matchedItem == null)
                        {
                            matchedItem = newItems.FirstOrDefault(i => FlexMatch(i.CustomerPartNumber, itemCode)
                                                                    || FlexMatch(i.VIN, itemCode));
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // Target VIN: dari preview resolve, matched item, atau normalized part no sebagai fallback terakhir
                        string targetVin = finalVin
                            ?? (!string.IsNullOrWhiteSpace(itemData.Vin) ? itemData.Vin : null)
                            ?? itemCodeNorm;

                        _logger.LogInformation(
                            "SmartImportConfirm: PartNo='{Part}' VinFromPreview='{PreviewVin}' → {Status}, VIN='{Vin}', ItemId={Id}",
                            itemCode,
                            itemData.Vin ?? "(none)",
                            matchedItem != null ? "MATCHED" : "AUTO-CREATE",
                            finalVin ?? "(none)",
                            matchedItem?.ItemId ?? 0);

                        // Auto-create only if not found anywhere
                        if (matchedItem == null)
                        {
                            matchedItem = new Item
                            {
                                ItemCode = targetVin,
                                VIN = targetVin,
                                CustomerPartNumber = itemCode,  // keep original with dashes
                                Customer = customer.CustomerName,
                                ItemName = "SmartImport (" + itemCode + ")",
                                Description = "Auto-created from Smart Import",
                                IsActive = true,
                                CreatedDate = DateTime.Now,
                                QtyLot = 1
                            };
                            _context.Items.Add(matchedItem);
                            newItems.Add(matchedItem); // track separately from DB items
                        }
                        else if (matchedItem.ItemId > 0
                            && string.IsNullOrWhiteSpace(matchedItem.CustomerPartNumber)
                            && itemCode.Contains('-'))
                        {
                            // Enrich existing item with customer part number if missing
                            matchedItem.CustomerPartNumber = itemCode;
                        }

                        var deliveryItem = new DeliveryItem
                        {
                            Quantity = itemData.Qty,
                            ActualQuantity = 0,
                            CreatedDate = DateTime.Now
                        };

                        if (matchedItem.ItemId == 0)
                            deliveryItem.Item = matchedItem;
                        else
                            deliveryItem.ItemId = matchedItem.ItemId;

                        schedule.DeliveryItems.Add(deliveryItem);
                        totalQty += itemData.Qty;
                        itemCount++;
                    }

                    schedule.TotalTargetQuantity = totalQty;
                    _context.DeliverySchedules.Add(schedule);
                    successCount++;
                }

                await _context.SaveChangesAsync();

                // Auto-sync pending preparations
                int synced = await _syncService.SyncAllPendingAsync();

                // SignalR notification
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "smartImport",
                    message = $"Smart Import: {successCount} schedule ({itemCount} items) berhasil di-import.",
                    timestamp = DateTime.Now
                });

                return Json(new
                {
                    success = true,
                    message = $"Berhasil import {successCount} schedule ({itemCount} items)." +
                              (skippedCount > 0 ? $" {skippedCount} manifest di-skip (sudah ada)." : "") +
                              (synced > 0 ? $" {synced} preparation otomatis tersinkronisasi." : "")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        public class SmartImportConfirmRequest
        {
            public int CustomerId { get; set; }
            public List<SmartImportItemRequest> Items { get; set; } = new();
        }

        public class SmartImportItemRequest
        {
            public string? Manifesting { get; set; }
            public string PartNo { get; set; } = "";
            /// <summary>
            /// VIN yang sudah di-resolve saat preview (dari ItemMappings/Items).
            /// Jika tersedia, digunakan sebagai shortcut lookup item — tidak perlu matching ulang.
            /// </summary>
            public string? Vin { get; set; }
            public int Qty { get; set; }
        }

        #endregion

        public class ClearPreparationRequest
        {
            public string Mode { get; set; } = "all"; // "date" atau "all"
            public string? Date { get; set; }         // format "yyyy-MM-dd", hanya dipakai jika Mode = "date"
        }

        [HttpPost]
        public async Task<IActionResult> ClearPreparationData([FromBody] ClearPreparationRequest? request)
        {
            request ??= new ClearPreparationRequest { Mode = "all" };

            int scheduleCount;
            int prepCount;

            if (request.Mode == "date" && !string.IsNullOrWhiteSpace(request.Date) && DateTime.TryParse(request.Date, out DateTime targetDate))
            {
                var targetDay = targetDate.Date;

                // Hapus jadwal pada tanggal tersebut beserta DeliveryItems-nya (cascade)
                var schedules = await _context.DeliverySchedules
                    .Where(s => s.ScheduledDate.Date == targetDay)
                    .ToListAsync();
                scheduleCount = schedules.Count;
                _context.DeliverySchedules.RemoveRange(schedules);

                // Hapus PreparationRecords yang tanggal scan-nya sama
                var preps = await _context.PreparationRecords
                    .Where(p => p.CreatedDate.Date == targetDay)
                    .ToListAsync();
                prepCount = preps.Count;
                _context.PreparationRecords.RemoveRange(preps);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Berhasil menghapus {scheduleCount} jadwal dan {prepCount} data Preparation tanggal {targetDay:dd/MM/yyyy}." });
            }
            else
            {
                // Hapus semua
                scheduleCount = await _context.DeliverySchedules.CountAsync();
                _context.DeliverySchedules.RemoveRange(_context.DeliverySchedules);

                prepCount = await _context.PreparationRecords.CountAsync();
                _context.PreparationRecords.RemoveRange(_context.PreparationRecords);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Berhasil menghapus semua data: {scheduleCount} jadwal dan {prepCount} data Preparation." });
            }
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
        public int PickupColIndex { get; set; } = -1;
        public int EtdColIndex { get; set; } = -1;
    }
}
