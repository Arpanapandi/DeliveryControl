using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using DeliveryControl.Helpers;
using DeliveryControl.Filters;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.Authorize]
    [AuthorizeRoles("Admin", "Preparation", "User")]
    public class PreparationWorkflowController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<StockHub> _stockHubContext;
        private readonly IHubContext<DeliveryHub> _deliveryHubContext;
        private readonly DeliveryControl.Services.ActivityLogService _logService;
        private readonly PreparationSyncService _syncService;

        public PreparationWorkflowController(
            ApplicationDbContext context, 
            IHubContext<StockHub> stockHubContext,
            IHubContext<DeliveryHub> deliveryHubContext,
            DeliveryControl.Services.ActivityLogService logService,
            PreparationSyncService syncService)
        {
            _context = context;
            _stockHubContext = stockHubContext;
            _deliveryHubContext = deliveryHubContext;
            _logService = logService;
            _syncService = syncService;
        }

        private async Task<HashSet<string>> GetAllowedDockCodesAsync()
        {
            // PENTING: gunakan Session, bukan Claims — sistem ini session-based
            var sessionRole = HttpContext.Session.GetString("Role");
            var userIdStr = HttpContext.Session.GetString("UserId");
            
            if (sessionRole == "Admin") return new HashSet<string>();
            
            if (int.TryParse(userIdStr, out int userId))
            {
                var dockData = await _context.UserDockAccesses
                    .Where(uda => uda.UserId == userId)
                    .Select(uda => new { uda.Dock.DockCode, uda.Dock.DockName })
                    .ToListAsync();

                // Kembalikan set gabungan DockCode + DockName dalam uppercase
                return dockData
                    .SelectMany(d => new[] { d.DockCode ?? "", d.DockName ?? "" })
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .ToHashSet();
            }
            
            return new HashSet<string>();
        }

        public async Task<IActionResult> Index(DateTime? filterDate, string? vin)
        {
            var isAdmin = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin" || HttpContext.Session.GetString("Role") == "Admin";
            var sessionRole = HttpContext.Session.GetString("Role");
            var isAdminOrPrep = isAdmin || sessionRole == "Preparation";
            var isUser = sessionRole == "User";
            var allowedDockCodes = await GetAllowedDockCodesAsync();
            
            ViewData["TargetVin"] = vin; // Simpan untuk UI
            ViewData["IsAdmin"] = isAdmin; // Untuk toggle Mode Ketik Manual & Skip Stock
            ViewData["IsAdminOrPrep"] = isAdminOrPrep; // Untuk indicator Skip Stock (tampil semua role)
            ViewData["IsUser"] = isUser; // Role User: read-only
            // Status Skip Stock dari DB SystemSettings (global — berlaku untuk semua role/user)
            var skipSetting = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "SkipStockValidation");
            ViewData["SkipStockActive"] = skipSetting?.Value == "1";

            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Terapkan filter dock HANYA jika user punya dock assignment
            if (!isAdmin && allowedDockCodes.Any())
            {
                var allSched = await query.ToListAsync();
                var matchIds = allSched
                    .Where(s =>
                        allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                        (s.Customer != null && (
                            allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                    .Select(s => s.ScheduleId).ToHashSet();
                query = query.Where(s => matchIds.Contains(s.ScheduleId));
            }

            if (!string.IsNullOrEmpty(vin))
            {
                query = query.Where(s => s.ScheduleNumber.Contains(vin) || s.DeliveryItems.Any(di => di.Item.ItemCode.Contains(vin)));
            }

            if (filterDate.HasValue)
            {
                query = query.Where(s => s.ScheduledDate.Date == filterDate.Value.Date);
                ViewData["CurrentFilterDate"] = filterDate.Value.ToString("yyyy-MM-dd");
                ViewData["FilterTitle"] = "Jadwal Tanggal " + filterDate.Value.ToString("dd MMM yyyy");
            }
            else
            {
                // Default: Today and Tomorrow
                var tomorrow = today.AddDays(1);
                query = query.Where(s => s.ScheduledDate.Date >= today && s.ScheduledDate.Date <= tomorrow);
                ViewData["CurrentFilterDate"] = "";
                ViewData["FilterTitle"] = "Jadwal Hari Ini & Besok";
            }

            var rawSchedules = await query.ToListAsync();

            // Self-Correct Status for Old Data (In-Memory Fix)
            foreach (var s in rawSchedules)
            {
                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.Status != "Completed" || s.PreparationStatus != "Prepared")
                    {
                        s.Status = "Completed";
                        s.PreparationStatus = "Prepared";
                    }
                }
            }

            var schedules = rawSchedules
                .OrderBy(s => ((s.Status == "Preparing" || s.Status == "In Progress" || s.PreparationStatus == "Preparing" || s.PreparationStatus == "In Progress") && s.PreparationStatus != "Prepared" && s.Status != "Completed") ? 0 : 
                              (s.Status == "Completed" || s.PreparationStatus == "Prepared") ? 2 : 1) // 0=Top, 1=Scheduled, 2=Bottom
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToList();
            
            return View(schedules);
        }

        /// <summary>
        /// Admin-only: Aktifkan/nonaktifkan Skip Stock Validation secara global (via DB SystemSettings).
        /// Berlaku untuk SEMUA user/role yang login karena disimpan di database.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SetSkipStockMode([FromBody] SetSkipStockModeRequest req)
        {
            var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
            if (!isAdmin)
                return Json(new { success = false, message = "Hanya Admin yang bisa mengaktifkan mode ini." });

            const string key = "SkipStockValidation";
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSetting { Key = key, Description = "Skip validasi stock habis saat preparation (diaktifkan Admin)" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.Active ? "1" : "0";
            await _context.SaveChangesAsync();

            return Json(new { success = true, active = req.Active });
        }

        [HttpGet]
        public async Task<IActionResult> LookupSchedule(string tag, string? label, string? kanban, bool skipStockValidation = false)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Json(new { success = false });

            // Override skipStockValidation dengan nilai dari DB SystemSettings (di-set oleh Admin)
            // Agar role Preparation pun bisa bypass karena flag disimpan di DB (bukan per-session)
            if (!skipStockValidation)
            {
                var skipSetting = await _context.SystemSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "SkipStockValidation");
                if (skipSetting?.Value == "1") skipStockValidation = true;
            }

            try
            {
                tag = tag.Trim();
                label = (label ?? "").Trim();
                kanban = (kanban ?? "").Trim();

                // 1. Find ALL Items by Tag (Internal) - Supporting Flexible LB Prefix/Suffix
                var normalizedTag = VinHelper.Normalize(tag);
                var items = await _context.Items
                    .Where(i => i.VIN.Contains(normalizedTag) || i.ItemCode.Contains(normalizedTag))
                    .ToListAsync();
                
                // Filter in-memory for exact flexible match
                items = items.Where(i => VinHelper.IsMatch(i.VIN, tag) || VinHelper.IsMatch(i.ItemCode, tag)).ToList();
                
                if (!items.Any())
                {
                    // Fallback to pulling records
                    var pulling = await _context.PullingRecords
                        .OrderByDescending(p => p.CreatedDate)
                        .FirstOrDefaultAsync(p => p.Tag == normalizedTag);
                    if (pulling != null)
                    {
                        var pItem = await _context.Items.FindAsync(pulling.ItemId);
                        if (pItem != null) items.Add(pItem);
                    }
                }

                if (!items.Any()) return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });

                // 2. Filter Items by active Schedules (with Dock Access Control)
                var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
                var allowedDockCodes = await GetAllowedDockCodesAsync();

                var scheduleQuery = _context.DeliverySchedules
                    .Where(s => s.Status == "Scheduled" || s.Status == "In Progress");

                if (!isAdmin && allowedDockCodes.Any())
                {
                    var allSchedList = await scheduleQuery.Include(s => s.Customer).ToListAsync();
                    var matchSchedIds = allSchedList
                        .Where(s =>
                            allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                            (s.Customer != null && (
                                allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                                allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                                allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                        .Select(s => s.ScheduleId).ToHashSet();
                    scheduleQuery = scheduleQuery.Where(s => matchSchedIds.Contains(s.ScheduleId));
                }

                var activeScheduleItemIds = await scheduleQuery
                    .SelectMany(s => s.DeliveryItems)
                    .Select(di => di.ItemId)
                    .Distinct()
                    .ToListAsync();

                var itemsInSchedule = items.Where(i => activeScheduleItemIds.Contains(i.ItemId)).ToList();
                
                // If NO items in schedule, pick the first one just to show info/metadata
                var targetItem = itemsInSchedule.FirstOrDefault() ?? items.First();

                // Calculate current stock to show to user
                var totalPulled = await _context.PullingRecords.CountAsync(r => r.ItemId == targetItem.ItemId);
                var totalPrepared = await _context.PreparationRecords.CountAsync(r => r.Tag == targetItem.VIN || r.Tag == targetItem.ItemCode);
                var currentStock = Math.Max(0, totalPulled - totalPrepared);

                // --- EARLY VALIDATION: Check for empty stock right away ---
                if (!skipStockValidation && currentStock <= 0)
                {
                    return Json(new { 
                        success = false, 
                        message = $"STOCK HABIS! ({targetItem.ItemName}) Tidak bisa lanjut scan." 
                    });
                }

                // --- Ambil external PartNo dari ItemMappings (sekali, dipakai label & kanban validation) ---
                // Gunakan VinHelper untuk flexible match karena VIN di ItemMappings mungkin disimpan dengan format berbeda
                string normalizedTargetVin = VinHelper.Normalize(targetItem.VIN);
                var allItemMappings = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                // Filter di memory dengan flexible VIN matching
                var matchedMappings = allItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.VIN) && 
                                (VinHelper.Normalize(m.VIN) == normalizedTargetVin || 
                                 VinHelper.IsMatch(m.VIN, targetItem.VIN)))
                    .ToList();
                var externalPartNos = matchedMappings
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosUpper = externalPartNos.Select(p => p.ToUpper()).ToList();

        DeliverySchedule? schedule = null;
        var today = DateTime.Today;

        if (!string.IsNullOrEmpty(kanban))
        {
            string kanbanUpper = kanban.ToUpper();

            // Jika ada external PartNo dari ItemMappings → validasi UTAMA kanban
            // Jika tidak ada → fallback ke PartNo / VIN / ItemCode dari master Item
            bool kanbanValid;
            List<string> expectedKanbanCodes;

            if (externalPartNosUpper.Any())
            {
                kanbanValid = externalPartNosUpper.Any(ep => kanbanUpper == ep || kanbanUpper.Contains(ep));
                expectedKanbanCodes = externalPartNos;
            }
            else
            {
                string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();
                string itemVin = (targetItem.VIN ?? "").ToUpper();
                bool matchPartNo = !string.IsNullOrEmpty(partNo)  && (kanbanUpper == partNo || kanbanUpper.Contains(partNo));
                bool matchVin    = !string.IsNullOrEmpty(itemVin) && (kanbanUpper == itemVin || kanbanUpper.Contains(itemVin));
                bool matchCode   = !string.IsNullOrEmpty(targetItem.ItemCode) && kanbanUpper == targetItem.ItemCode.ToUpper();
                kanbanValid = matchPartNo || matchVin || matchCode;
                expectedKanbanCodes = new List<string>(
                    new[] { targetItem.CustomerPartNumber, targetItem.VIN, targetItem.ItemCode }
                    .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Take(1));
            }

            if (kanbanValid)
            {
                schedule = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") &&
                                 s.ScheduledDate.Date >= today &&
                                 s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId && (di.ActualQuantity ?? 0) < di.Quantity))
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .FirstOrDefaultAsync();
            }
            else
            {
                var validCodesStr = string.Join(" / ", expectedKanbanCodes.Distinct());
                return Json(new {
                    success = true,
                    found   = false,
                    step    = "kanban",
                    item = new {
                        itemName           = targetItem.ItemName,
                        vin                = targetItem.VIN,
                        customerPartNumber = targetItem.CustomerPartNumber,
                        qtyLot             = targetItem.QtyLot ?? 0,
                        currentStock       = currentStock,
                        externalPartNos    = externalPartNos
                    },
                    message = $"KANBAN tidak sesuai! Barcode harus mengandung: {(string.IsNullOrEmpty(validCodesStr) ? "Part No terdaftar" : validCodesStr)}"
                });
            }
        }

        if (!string.IsNullOrEmpty(label))
        {
            string labelUpper = label.ToUpper();
            string vin = (targetItem.VIN ?? "").ToUpper();
            string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();

            // Label valid jika mengandung VIN (dengan berbagai format), CustomerPartNumber,
            // ATAU salah satu external PartNo dari ItemMappings
            bool labelValid = VinHelper.IsLabelContainsVin(label, targetItem.VIN)
                || (!string.IsNullOrEmpty(partNo) && labelUpper.Contains(partNo))
                || externalPartNosUpper.Any(ep => labelUpper.Contains(ep));

            if (!labelValid)
            {
                var normalizedVinDisplay = VinHelper.Normalize(targetItem.VIN);
                return Json(new { 
                    success = true, 
                    found = false, 
                    step = "label",
                    item = new { 
                        itemName = targetItem.ItemName, 
                        vin = targetItem.VIN, 
                        customerPartNumber = targetItem.CustomerPartNumber,
                        qtyLot = targetItem.QtyLot ?? 0,
                        currentStock = currentStock,
                        externalPartNos = externalPartNos
                    },
                    message = $"❌ LABEL TIDAK VALID! Label harus mengandung kode VIN: \"{normalizedVinDisplay}\". Scan ulang label yang benar." 
                });
            }
        }

        // If not all 3 are provided, we don't look for schedule yet
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(kanban))
        {
            return Json(new { 
                success = true, 
                found = false, 
                step = "partial",
                item = new { 
                    itemName = targetItem.ItemName, 
                    vin = targetItem.VIN, 
                    customerPartNumber = targetItem.CustomerPartNumber,
                    currentStock = currentStock,
                    externalPartNos = externalPartNos  // Dikirim ke client setelah label di-scan
                },
                message = "Dilanjutkan ke scan berikutnya..." 
            });
        }

        // 4. Find FIFO Schedule if not already found via Kanban
        if (schedule == null)
        {
            schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") &&
                             s.ScheduledDate.Date >= today &&
                             s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId && (di.ActualQuantity ?? 0) < di.Quantity))
                .OrderBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();
        }

        if (schedule == null)
        {
            return Json(new { 
                success = true, 
                found = false, 
                step = "final",
                item = new { 
                    itemName = targetItem.ItemName, 
                    vin = targetItem.VIN, 
                    customerPartNumber = targetItem.CustomerPartNumber,
                    currentStock = currentStock,
                    externalPartNos = externalPartNos
                },
                message = "Produk OK, tapi JADWAL tidak ditemukan" 
            });
        }

                var deliveryItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == targetItem.ItemId);

                return Json(new { 
                    success = true, 
                    found = true,
                    step = "complete",
                    item = new { targetItem.ItemName, targetItem.VIN, targetItem.CustomerPartNumber, targetItem.QtyLot, currentStock, externalPartNos, TargetPartNo = !string.IsNullOrEmpty(targetItem.CustomerPartNumber) ? targetItem.CustomerPartNumber : targetItem.VIN },
                    schedule = new { 
                        schedule.ScheduleId, 
                        schedule.ScheduleNumber, 
                        CustomerName = schedule.Customer?.CustomerCode,
                        Dock = schedule.Customer?.CustomerName,
                        Date = schedule.ScheduledDate.ToString("dd MMM yyyy"),
                        TargetQty = deliveryItem?.Quantity ?? 0,
                        ActualQty = deliveryItem?.ActualQuantity ?? 0
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [AuthorizeRoles("Admin", "Preparation")]
        public async Task<IActionResult> Save([FromBody] PreparationRecord record)
        {
            if (record == null) return Json(new { success = false, message = "Data kosong." });

            if (string.IsNullOrWhiteSpace(record.Tag) || string.IsNullOrWhiteSpace(record.Label) || string.IsNullOrWhiteSpace(record.Kanban))
            {
                return Json(new { success = false, message = "Tag, Label, dan Kanban wajib diisi!" });
            }

            // Retry loop: tangani SQLite database locked (error code 5 = SQLITE_BUSY)
            // yang bisa terjadi saat beberapa scan dalam antrian dikirim hampir bersamaan.
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await ExecuteSave(record);
                }
                catch (Microsoft.Data.Sqlite.SqliteException sqlEx) when (sqlEx.SqliteErrorCode == 5 && attempt < maxRetries)
                {
                    // SQLITE_BUSY — tunggu sebentar lalu coba lagi
                    await Task.Delay(120 * attempt);
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
                {
                    await Task.Delay(80 * attempt);
                }
            }

            // Final attempt — biarkan exception naik sebagai retryable response
            try { return await ExecuteSave(record); }
            catch (Exception ex)
            {
                return Json(new { success = false, retryable = true, message = "Sistem sedang sibuk, scan akan dicoba ulang otomatis. (" + ex.Message + ")" });
            }
        }

        private async Task<IActionResult> ExecuteSave(PreparationRecord record)
        {
            // Override SkipStockValidation dengan nilai dari DB SystemSettings (di-set oleh Admin)
            // Berlaku untuk semua role termasuk Preparation karena disimpan di DB bukan per-session
            if (!record.SkipStockValidation)
            {
                var skipSetting = await _context.SystemSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "SkipStockValidation");
                if (skipSetting?.Value == "1") record.SkipStockValidation = true;
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try 
            {
                record.Tag = VinHelper.Normalize(record.Tag);
                record.Label = record.Label.Trim();
                record.Kanban = record.Kanban.Trim();

                // NOTE: Duplicate prevention DIHAPUS karena:
                // - 1 item bisa butuh 10+ kanban = scan 3-point check yang SAMA 10x
                // - Scan cepat dengan data identik adalah VALID dan harus semua tersimpan
                // - Client-side queue sudah handle throttling untuk mencegah double-submit accidental
                // - Validasi kanban terhadap database tetap dilakukan di bawah

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification & Item Lookup
                var normalizedSaveTag = VinHelper.Normalize(record.Tag);
                var itemsMatch = await _context.Items
                    .Where(i => i.VIN.Contains(normalizedSaveTag) || i.ItemCode.Contains(normalizedSaveTag))
                    .ToListAsync();
                
                var item = itemsMatch.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, record.Tag) || VinHelper.IsMatch(i.ItemCode, record.Tag));
                
                if (item == null)
                {
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == record.Tag);
                    if (pulling != null)
                    {
                        item = await _context.Items.FindAsync(pulling.ItemId);
                    }
                }

                if (item == null) return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });

                // 1b. VALIDASI LABEL: Label harus mengandung kode VIN item
                // Ini adalah validasi server-side (double-check dari client-side)
                string labelSaveUpper = record.Label.ToUpper();
                string itemPartNoSave2 = (item.CustomerPartNumber ?? "").ToUpper();
                // Ambil external PartNos dari ItemMappings untuk validasi label
                string normalizedItemVinForLabel = VinHelper.Normalize(item.VIN);
                var allMappingsLabel = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                var matchedMappingsLabel = allMappingsLabel
                    .Where(m => !string.IsNullOrEmpty(m.VIN) &&
                                (VinHelper.Normalize(m.VIN) == normalizedItemVinForLabel ||
                                 VinHelper.IsMatch(m.VIN, item.VIN)))
                    .ToList();
                var externalPartNosLabel = matchedMappingsLabel
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosLabelUpper = externalPartNosLabel.Select(p => p.ToUpper()).ToList();

                bool labelSaveValid = VinHelper.IsLabelContainsVin(record.Label, item.VIN)
                    || (!string.IsNullOrEmpty(itemPartNoSave2) && labelSaveUpper.Contains(itemPartNoSave2))
                    || externalPartNosLabelUpper.Any(ep => labelSaveUpper.Contains(ep));

                bool isMismatch = false;

                if (!labelSaveValid)
                {
                    isMismatch = true;
                }

                // 2. Final Schedule Matching (Strict Part Number Validation + FIFO)
                string kanbanSaveUpper = record.Kanban.ToUpper();

                // Ambil external PartNo dari ItemMappings — gunakan sebagai prioritas utama validasi kanban
                // Gunakan VinHelper untuk flexible match karena VIN di ItemMappings mungkin disimpan dengan format berbeda
                string normalizedItemVin = VinHelper.Normalize(item.VIN);
                var allMappingsSave = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                var matchedMappingsSave = allMappingsSave
                    .Where(m => !string.IsNullOrEmpty(m.VIN) && 
                                (VinHelper.Normalize(m.VIN) == normalizedItemVin || 
                                 VinHelper.IsMatch(m.VIN, item.VIN)))
                    .ToList();
                var externalPartNosSave = matchedMappingsSave
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosSaveUpper = externalPartNosSave.Select(p => p.ToUpper()).ToList();

                DeliverySchedule? schedule = null;
                var today = DateTime.Today;

                bool kanbanSaveValid;
                List<string> validSaveCodes;

                if (externalPartNosSaveUpper.Any())
                {
                    // Prioritas: validasi hanya via ItemMappings
                    kanbanSaveValid = externalPartNosSaveUpper.Any(ep => kanbanSaveUpper == ep || kanbanSaveUpper.Contains(ep));
                    validSaveCodes = externalPartNosSave;
                }
                else
                {
                    // Fallback: validasi via PartNo / VIN / ItemCode dari master Item
                    string partNoSave = (item.CustomerPartNumber ?? "").ToUpper();
                    string vinSave    = (item.VIN ?? "").ToUpper();
                    bool matchPartNo  = !string.IsNullOrEmpty(partNoSave) && (kanbanSaveUpper == partNoSave || kanbanSaveUpper.Contains(partNoSave));
                    bool matchVin     = !string.IsNullOrEmpty(vinSave)    && (kanbanSaveUpper == vinSave    || kanbanSaveUpper.Contains(vinSave));
                    bool matchCode    = !string.IsNullOrEmpty(item.ItemCode) && kanbanSaveUpper == item.ItemCode.ToUpper();
                    kanbanSaveValid   = matchPartNo || matchVin || matchCode;
                    validSaveCodes    = new List<string>(
                        new[] { item.CustomerPartNumber, item.VIN, item.ItemCode }
                        .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Take(1));
                }

                if (!kanbanSaveValid)
                {
                    isMismatch = true;
                }

                // If Mismatch: save the record but don't affect stock/schedule
                if (isMismatch)
                {
                    record.Plant = item.Plant ?? "-";
                    record.ScheduleId = null;
                    record.Remark = "Mismatch";

                    // Tentukan alasan mismatch
                    string mismatchReason = !labelSaveValid
                        ? $"Label tidak sesuai VIN: {item.VIN}"
                        : $"Kanban tidak sesuai (Expected: {string.Join("/", validSaveCodes.Take(3))})";

                    // Catat ke ScanNGLogs (server-side safety net — selain dari client-side logNGScan)
                    var ngLog = new ScanNGLog
                    {
                        Module = "Preparation",
                        Tag = record.Tag ?? "",
                        Label = record.Label ?? "",
                        Kanban = record.Kanban ?? "",
                        Reason = mismatchReason,
                        CreatedBy = record.CreatedBy ?? "Operator",
                        CreatedDate = DateTime.Now
                    };
                    _context.ScanNGLogs.Add(ngLog);

                    _context.PreparationRecords.Add(record);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Log activity
                    await _logService.LogActivity(
                        module: "Preparation",
                        action: "Create",
                        entityName: item.ItemName + " (" + item.VIN + ")",
                        entityId: record.PreparationId,
                        description: $"Plant: {record.Plant}, Label: {record.Label}, Kanban: {record.Kanban} [MISMATCH - No stock impact]",
                        performedBy: record.CreatedBy
                    );

                    await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "mismatch" });

                    return Json(new { 
                        success = true, 
                        isMismatch = true,
                        remark = "Mismatch",
                        message = "⚠️ MISMATCH — Transaksi tercatat tetapi TIDAK mempengaruhi stock. (Label/Kanban tidak sesuai)" 
                    });
                }

                record.Remark = "Match";

                // Find FIFO Schedule
                schedule = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") &&
                                 s.ScheduledDate.Date >= today &&
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity))
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .FirstOrDefaultAsync();

                if (schedule == null) 
                {
                    // --- PENDING SAVE: Jadwal belum tersedia, simpan dulu tanpa ScheduleId ---
                    // SyncService akan otomatis mengisi ScheduleId ketika admin buat/upload jadwal baru.
                    record.Plant = item.Plant ?? "-";
                    record.ScheduleId = null; // eksplisit null = pending, belum terikat jadwal

                    _context.PreparationRecords.Add(record);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Broadcast ke preparation hub saja (bukan stockHub — agar tidak trigger notif di dashboard FG)
                    await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "pending" });

                    return Json(new { 
                        success = true, 
                        isPending = true,
                        message = "Part tersimpan sebagai PENDING — akan tersinkronisasi ke jadwal secara otomatis saat jadwal tersedia." 
                    });
                }

                // DOCK ACCESS CONTROL
                var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
                if (!isAdmin)
                {
                    var allowedDockCodes = await GetAllowedDockCodesAsync();
                    bool isAuthorized = !allowedDockCodes.Any()
                        || allowedDockCodes.Contains((schedule.Area ?? "").Trim().ToUpper())
                        || (schedule.Customer != null && (
                            allowedDockCodes.Contains((schedule.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((schedule.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((schedule.Customer.Area ?? "").Trim().ToUpper())));
                    if (!isAuthorized)
                    {
                        return Json(new { success = false, message = "Anda tidak memiliki akses ke Dock ini" });
                    }
                }

                // --- VALIDASI 1: Cek apakah kebutuhan QTY sudah terpenuhi ---
                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                if (dItem != null && (dItem.ActualQuantity ?? 0) >= dItem.Quantity)
                {
                    return Json(new { success = false, message = $"QTY sudah CUKUP untuk jadwal ini" });
                }

                // --- VALIDASI 2: Cek Saldo Stok Riil (Per Label & FIFO) ---
                // Only count Match records for stock validation (Mismatch records don't affect stock)
                var countPulled = await _context.PullingRecords.CountAsync(p => p.Tag == record.Tag && p.Label == record.Label && p.Remark == "Match");
                var countPrepared = await _context.PreparationRecords.CountAsync(p => p.Tag == record.Tag && p.Label == record.Label && p.Remark == "Match");

                var allPullings = await _context.PullingRecords.Where(p => p.Tag == record.Tag && p.Remark == "Match").OrderBy(p => p.CreatedDate).ToListAsync();
                var allPreps = await _context.PreparationRecords.Where(p => p.Tag == record.Tag && p.Remark == "Match").ToListAsync();

                var consumedPullingIds = new HashSet<int>();
                foreach (var p in allPreps)
                {
                    var pLabel = (p.Label ?? "").Trim().ToUpper();
                    // Prioritas 1: match Tag + Label persis
                    var matchPrep = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId) && (pl.Label ?? "").Trim().ToUpper() == pLabel);
                    // Prioritas 2: match Tag saja (untuk Manual Adjust di mana Label bisa berbeda)
                    if (matchPrep == null)
                        matchPrep = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId));
                    if (matchPrep != null) consumedPullingIds.Add(matchPrep.PullingId);
                }

                // Sisa stock yang belum dikonsumsi oleh preparation lain
                var availablePullings = allPullings.Where(pl => !consumedPullingIds.Contains(pl.PullingId)).ToList();

                PullingRecord? targetPulling = null;
                if (!availablePullings.Any())
                {
                    // Jika mode Skip Stock Validation aktif, biarkan lanjut meski tidak ada pulling di rak
                    if (!record.SkipStockValidation)
                        return Json(new { success = false, message = "STOCK tidak tersedia di Rak" });
                    // Mode skip: targetPulling = null, proses tetap lanjut tanpa linking ke pulling
                }
                else
                {
                    // Cari Pulling yang label-nya cocok dengan yang di-scan user
                    targetPulling = availablePullings.FirstOrDefault(pl => 
                        (pl.Label ?? "").Trim().ToUpper() == record.Label.Trim().ToUpper());

                    // Jika tidak ada exact match label, ambil yang paling lama (FIFO dari sisa available)
                    if (targetPulling == null)
                    {
                        targetPulling = availablePullings.First(); // Sudah di-order by CreatedDate
                    }
                }

                record.Plant = item.Plant ?? "-";
                record.ScheduleId = schedule.ScheduleId;

                // 3. Update Stats (Atomic Increment)
                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;
                
                schedule.TotalActualQuantity += qpc;
                schedule.Status = "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                if (dItem != null)
                {
                    dItem.ActualQuantity = (dItem.ActualQuantity ?? 0) + qpc;
                    if (dItem.ActualQuantity >= dItem.Quantity) dItem.IsCompleted = true;

                    // Update main schedule status if all items are done
                    if (schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                    {
                        if (schedule.PreparationStatus != "Prepared")
                        {
                            schedule.PreparationStatus = "Prepared";
                            // NOTE: ReadyToDockTime is NOT set here.
                            // It is set at the GROUP level below, only when ALL schedules in the manifest are done.
                        }
                    }
                }

                // --- AUTO PREPARED LOGIC: Automate "Ready to Pickup" when all scans for the group are done ---
                var groupDate = schedule.ScheduledDate.Date;
                var groupSchedules = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => s.ScheduledDate.Date == groupDate &&
                                s.Cycle == schedule.Cycle &&
                                s.Route == schedule.Route &&
                                s.Area == schedule.Area &&
                                s.Status != "Cancelled")
                    .ToListAsync();

                bool isGroupReady = false;
                string groupManifest = (schedule.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper();

                if (groupSchedules.All(gs => gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                {
                    isGroupReady = true;
                    var scanTime = DateTime.Now;
                    foreach (var gs in groupSchedules)
                    {
                        // Mark all as Prepared and In Progress
                        gs.PreparationStatus = "Prepared";
                        gs.Status = "In Progress"; // MUST be In Progress for Driver Portal visibility
                        gs.UpdatedDate = scanTime;

                        // Set ReadyToDockTime for ALL in group (not just unset ones)
                        if (!gs.ReadyToDockTime.HasValue)
                        {
                            gs.ReadyToDockTime = scanTime;
                        }
                    }
                }
                else
                {
                    // Even if not fully prepared, ensure all members of the group are "In Progress" 
                    // so the card appears in the Driver Portal as soon as preparation starts.
                    foreach (var gs in groupSchedules)
                    {
                        if (gs.Status == "Scheduled")
                        {
                            gs.Status = "In Progress";
                            gs.UpdatedDate = DateTime.Now;
                        }
                    }
                }

                _context.PreparationRecords.Add(record);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // --- BROADCAST: Send all SignalR notifications AFTER data is fully committed ---
                var totalTarget = schedule.DeliveryItems.Sum(di => di.Quantity);
                var totalActual = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                var totalPercent = totalTarget > 0 ? ((double)totalActual / (double)totalTarget * 100) : 0;

                var broadcastData = new {
                    action = isGroupReady ? "readyToDock" : "preparation",
                    scheduleNumber = schedule.ScheduleNumber,
                    manifest = groupManifest,
                    status = schedule.Status,
                    preparationStatus = schedule.PreparationStatus,
                    totalPercent = totalPercent,
                    customerName = schedule.Customer?.CustomerName,
                    items = schedule.DeliveryItems.Select(di => new {
                        itemId = di.ItemId,
                        vin = di.Item?.VIN,
                        actual = di.ActualQuantity ?? 0,
                        target = di.Quantity,
                        percent = (di.Quantity > 0) ? ((double)(di.ActualQuantity ?? 0) / (double)di.Quantity * 100) : 0
                    }).ToList()
                };

                await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", broadcastData);
                // Tidak broadcast updateStock ke stockHub — agar dashboard FG hanya notif saat pulling

                return Json(new { 
                    success = true, 
                    message = $"Berhasil! Persiapan tersimpan.",
                    scheduleDetails = new
                    {
                        schedule.ScheduleId,
                        schedule.ScheduleNumber,
                        CustomerName = schedule.Customer?.CustomerName,
                        TotalPercent = totalPercent,
                        Items = schedule.DeliveryItems.Select(di => {
                            var iQpc = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                            return new {
                                itemName = di.Item?.ItemName ?? "Unknown",
                                vin = di.Item?.VIN ?? "-",
                                target = di.Quantity,
                                actual = di.ActualQuantity ?? 0,
                                targetKanban = Math.Ceiling((double)di.Quantity / iQpc),
                                actualKanban = ((double)(di.ActualQuantity ?? 0) / iQpc)
                            };
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        } // end ExecuteSave

        [HttpPost]
        [AuthorizeRoles("Admin", "Preparation")]
        public async Task<IActionResult> ConfirmSelesai(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null) return Json(new { success = false, message = "Jadwal tidak ditemukan." });

            schedule.PreparationStatus = "Prepared";
            schedule.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "preparation", scheduleNumber = schedule.ScheduleNumber });

            return Json(new { success = true });
        }

        /// <summary>
        /// Endpoint JSON untuk menampilkan daftar PreparationRecord yang masih pending (ScheduleId == null).
        /// Dipanggil dari Jadwal Index view via JS saat collapse dibuka.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPendingPreparations()
        {
            var records = await _context.PreparationRecords
                .Where(p => p.ScheduleId == null)
                .OrderByDescending(p => p.CreatedDate)
                .Take(200) // batas tampil
                .Select(p => new {
                    createdDate = p.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                    tag         = p.Tag,
                    label       = p.Label,
                    kanban      = p.Kanban,
                    createdBy   = p.CreatedBy ?? "-"
                })
                .ToListAsync();

            return Json(records);
        }

        [HttpGet]
        public async Task<IActionResult> GetSchedulesJson(DateTime? filterDate, string? vin)
        {
            var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
            var allowedDockCodes = await GetAllowedDockCodesAsync();

            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Terapkan filter dock HANYA jika user punya dock assignment
            if (!isAdmin && allowedDockCodes.Any())
            {
                var allSchedJson = await query.ToListAsync();
                var matchJsonIds = allSchedJson
                    .Where(s =>
                        allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                        (s.Customer != null && (
                            allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                    .Select(s => s.ScheduleId).ToHashSet();
                query = query.Where(s => matchJsonIds.Contains(s.ScheduleId));
            }

            // 1. Date Filter (Default: Today)
            if (filterDate.HasValue)
            {
                query = query.Where(s => s.ScheduledDate.Date == filterDate.Value.Date);
            }
            else
            {
                query = query.Where(s => s.ScheduledDate.Date == today);
            }

            // 2. VIN Filter (Partial Match)
            if (!string.IsNullOrEmpty(vin))
            {
                var vinLower = vin.ToLower();
                query = query.Where(s => s.DeliveryItems.Any(di => 
                    (di.Item != null && di.Item.VIN != null && di.Item.VIN.ToLower().Contains(vinLower)) ||
                    (di.Item != null && di.Item.CustomerPartNumber != null && di.Item.CustomerPartNumber.ToLower().Contains(vinLower))
                ));
            }

            var rawSchedules = await query.ToListAsync();

            // 3. Priority Sorting & Projection — PER ITEM (bukan per schedule)
            // Setiap DeliveryItem ditampilkan sebagai 1 baris terpisah
            var result = rawSchedules
                .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.Select(di => new { Schedule = s, Item = di })
                    : new[] { new { Schedule = s, Item = (DeliveryItem)null! } })
                .Select(row => {
                    var s = row.Schedule;
                    var di = row.Item;
                    
                    // Status per item
                    string itemStatus;
                    int priority;
                    if (di == null)
                    {
                        itemStatus = "Waiting";
                        priority = 1;
                    }
                    else
                    {
                        var actual = di.ActualQuantity ?? 0;
                        var target = di.Quantity;
                        if (actual > 0 && actual >= target) { itemStatus = "Completed"; priority = 2; }
                        else if (actual > 0) { itemStatus = "Preparing"; priority = 0; }
                        else { itemStatus = "Waiting"; priority = 1; }
                    }

                    // Calculate Kanban Counts for this item
                    double kanbanTarget = 0;
                    double kanbanActual = 0;
                    string vinDisplay = "-";
                    int qtyLot = 1;

                    if (di?.Item != null)
                    {
                        qtyLot = (di.Item.QtyLot != null && di.Item.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                        kanbanTarget = Math.Ceiling((double)di.Quantity / qtyLot);
                        kanbanActual = (double)(di.ActualQuantity ?? 0) / qtyLot;
                        vinDisplay = !string.IsNullOrEmpty(di.Item.VIN) ? di.Item.VIN : (di.Item.CustomerPartNumber ?? "-");
                    }

                    return new {
                        s.ScheduleId,
                        s.ScheduleNumber,
                        CustomerName = s.Customer?.CustomerCode ?? "-",
                        Dock = s.Customer?.CustomerName ?? "-",
                        VINs = vinDisplay,
                        PartNo = di?.Item?.CustomerPartNumber ?? "-",
                        QtyLot = qtyLot,
                        QtyPcs = di?.Quantity ?? 0,
                        Status = itemStatus,
                        Priority = priority,
                        TotalKanbanActual = kanbanActual, 
                        TotalKanbanTarget = kanbanTarget,
                        ScheduledDate = s.ScheduledDate
                    };
                })
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.TotalKanbanActual) // Yang baru di-scan naik ke atas
                .ThenBy(x => x.ScheduledDate)
                .ThenBy(x => x.ScheduleNumber);

            return Json(result);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LogNG([FromBody] PreparationNGLogRequest req)
        {
            try
            {
                var createdBy = HttpContext.Session.GetString("FullName") 
                             ?? HttpContext.Session.GetString("Username") 
                             ?? "Operator";
                var rec = new ScanNGLog
                {
                    Module = "Preparation",
                    Tag = req?.Tag?.Trim() ?? "",
                    Label = req?.Label?.Trim() ?? "",
                    Kanban = req?.Kanban?.Trim() ?? "",
                    Reason = req?.Reason?.Trim() ?? "Unknown Error",
                    CreatedBy = createdBy,
                    CreatedDate = DateTime.Now
                };
                _context.ScanNGLogs.Add(rec);
                await _context.SaveChangesAsync();
                
                // Broadcast ke dashboard Log Scan NG
                await _stockHubContext.Clients.All.SendAsync("updateStock");

                return Json(new { ok = true });
            }
            catch { return Json(new { ok = false }); }
        }
    }

    /// <summary>Request body untuk SetSkipStockMode endpoint.</summary>
    public class SetSkipStockModeRequest
    {
        public bool Active { get; set; }
    }

    public class PreparationNGLogRequest
    {
        public string Module { get; set; } = "Preparation";
        public string? Tag { get; set; }
        public string? Label { get; set; }
        public string? Kanban { get; set; }
        public string? Reason { get; set; }
    }
}
