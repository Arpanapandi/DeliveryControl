using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;

namespace DeliveryControl.Controllers
{
    public class PreparationWorkflowController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<StockHub> _stockHubContext;
        private readonly IHubContext<DeliveryHub> _deliveryHubContext;
        private readonly DeliveryControl.Services.ActivityLogService _logService;

        public PreparationWorkflowController(
            ApplicationDbContext context, 
            IHubContext<StockHub> stockHubContext,
            IHubContext<DeliveryHub> deliveryHubContext,
             DeliveryControl.Services.ActivityLogService logService)
        {
            _context = context;
            _stockHubContext = stockHubContext;
            _deliveryHubContext = deliveryHubContext;
            _logService = logService;
        }

        public async Task<IActionResult> Index(DateTime? filterDate)
        {
            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

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

        [HttpGet]
        public async Task<IActionResult> LookupSchedule(string tag, string? label, string? kanban)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Json(new { success = false });

            try
            {
                tag = tag.Trim();
                label = (label ?? "").Trim();
                kanban = (kanban ?? "").Trim();

                // Strict 'X' Suffix Validation
                if (!tag.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "FORMAT RAK SALAH: Barcode Rak harus berakhiran 'X'!" });
                }

                // Stripped values for internal lookup
                string rawTag = tag.Substring(0, tag.Length - 1); // Remove 'X'
                string baseTag = GetBaseVin(rawTag); // Normalize to 6 characters (e.g., TA1234LB -> TA1234)

                // 1. Find ALL Items by Base Tag (Internal 6-Digit Match)
                var items = await _context.Items
                    .Where(i => i.VIN.Length >= 6 && i.VIN.Substring(0, 6) == baseTag || i.ItemCode.Length >= 6 && i.ItemCode.Substring(0, 6) == baseTag || i.Rack == rawTag)
                    .ToListAsync();
                
                // Extra check because EF Core Substring might behave differently across DBs, 
                // but for SQLite/SQLServer this is generally handled. Let's make it robust by pulling and filtering if needed, 
                // OR better yet, just normalize to 6 in memory if list is small.
                if (!items.Any())
                {
                    items = (await _context.Items.AsNoTracking().ToListAsync())
                        .Where(i => GetBaseVin(i.VIN) == baseTag || GetBaseVin(i.ItemCode) == baseTag || i.Rack == rawTag)
                        .ToList();
                }
                
                if (!items.Any())
                {
                    // Fallback to pulling records
                    var pulling = await _context.PullingRecords
                        .OrderByDescending(p => p.CreatedDate)
                        .FirstOrDefaultAsync(p => p.Tag.StartsWith(baseTag) || p.Tag == tag); // Match with or without X
                    if (pulling != null)
                    {
                        var pItem = await _context.Items.FindAsync(pulling.ItemId);
                        if (pItem != null) items.Add(pItem);
                    }
                }

                if (!items.Any()) return Json(new { success = false, message = "Kode Rak/VIN tidak terdaftar di Master Data" });

                // 2. Filter Items by active Schedules
                var activeScheduleItemIds = await _context.DeliverySchedules
                    .Where(s => s.Status == "Scheduled" || s.Status == "In Progress")
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
                if (currentStock <= 0)
                {
                    return Json(new { 
                        success = false, 
                        message = $"STOCK HABIS! ({targetItem.ItemName}) Tidak bisa lanjut scan." 
                    });
                }

        DeliverySchedule? schedule = null;
        var today = DateTime.Today;

        if (!string.IsNullOrEmpty(kanban))
        {
            string kanbanUpper = kanban.ToUpper();
            string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();
            string itemVin = (targetItem.VIN ?? "").ToUpper();

            // NEW: Split by slash to handle "kode didepan garis miring"
            string cleanKanban = kanbanUpper.Split('/')[0].Trim();
            string cleanPartNo = partNo.Split('/')[0].Trim();
            string cleanVin = itemVin.Split('/')[0].Trim();

            // Validate Kanban input strictly against Part Number or VIN (Part BEFORE slash)
            if (cleanKanban == cleanPartNo || cleanKanban == cleanVin)
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
                return Json(new { 
                    success = true, 
                    found = false, 
                    step = "kanban",
                    item = new { 
                        itemName = targetItem.ItemName, 
                        vin = targetItem.VIN, 
                        customerPartNumber = targetItem.CustomerPartNumber,
                        qtyLot = targetItem.QtyLot ?? 0,
                        currentStock = currentStock
                    },
                    message = $"KANBAN tidak sesuai dengan produk!" 
                });
            }
        }

        if (!string.IsNullOrEmpty(label))
        {
            if (label.EndsWith("X", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { 
                    success = true, found = false, step = "label",
                    message = "MANIPULASI TERDETEKSI: Barcode Rak tidak boleh sebagai Label!" 
                });
            }

            string baseLabel = label; // Standard label (must NOT have X)

            // Pattern Check: Label must contain the base Rack/VIN code to prevent wrong rack scan
            if (!baseLabel.ToUpper().Contains(baseTag.ToUpper()))
            {
                return Json(new { 
                    success = true, found = false, step = "label",
                    message = "RAK MISMATCH: Label box tidak sesuai dengan Rak ini!" 
                });
            }

            // Reference Check: Verify if this specific Tag + Label was scanned in Pulling
            bool existsInPulling = await _context.PullingRecords
                .AnyAsync(p => p.Tag == tag && p.Label == label);

            if (!existsInPulling)
            {
                return Json(new { 
                    success = true, found = false, step = "label",
                    message = "LABEL BELUM SCAN PULLING: Belum masuk gudang!" 
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
                    currentStock = currentStock
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
                    currentStock = currentStock
                },
                message = "Produk OK, tapi JADWAL tidak ditemukan" 
            });
        }

                var deliveryItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == targetItem.ItemId);

                return Json(new { 
                    success = true, 
                    found = true,
                    step = "complete",
                    item = new { targetItem.ItemName, targetItem.VIN, targetItem.CustomerPartNumber, targetItem.QtyLot, currentStock, TargetPartNo = !string.IsNullOrEmpty(targetItem.CustomerPartNumber) ? targetItem.CustomerPartNumber : targetItem.VIN },
                    schedule = new { 
                        schedule.ScheduleId, 
                        schedule.ScheduleNumber, 
                        CustomerName = schedule.Customer?.CustomerName,
                        Dock = schedule.Customer?.Docking,
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
        public async Task<IActionResult> Save([FromBody] PreparationRecord record)
        {
            if (record == null) return Json(new { success = false, message = "Data kosong." });

            if (string.IsNullOrWhiteSpace(record.Tag) || string.IsNullOrWhiteSpace(record.Label) || string.IsNullOrWhiteSpace(record.Kanban))
            {
                return Json(new { success = false, message = "Tag, Label, dan Kanban wajib diisi!" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try 
            {
                record.Tag = record.Tag.Trim();
                record.Label = record.Label.Trim();
                record.Kanban = record.Kanban.Trim();

                // Strict Format Validation
                if (!record.Tag.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "FORMAT RAK SALAH: Barcode Rak wajib berakhiran 'X'!" });
                }

                if (record.Label.EndsWith("X", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "MANIPULASI TERDETEKSI: Barcode Rak tidak boleh digunakan sebagai Label!" });
                }

                string rawTagSave = record.Tag.Substring(0, record.Tag.Length - 1);
                string baseTagSave = GetBaseVin(rawTagSave);
                string baseLabelSave = record.Label; // Standard label (must NOT have X)

                // Double-Submission Prevention: Check if this exact scan was recorded in the last 10 seconds
                var recentDuplicate = await _context.PreparationRecords
                    .AnyAsync(p => p.Tag == record.Tag && p.Label == record.Label && p.CreatedDate > DateTime.Now.AddSeconds(-10));
                
                if (recentDuplicate)
                {
                    return Json(new { success = false, message = "Data sedang diproses atau sudah tersimpan (Duplicate Prevention)." });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification & Item Lookup - Using 6-Digit Base Tag
                var item = await _context.Items
                    .FirstOrDefaultAsync(i => 
                        (i.VIN.Length >= 6 && i.VIN.Substring(0, 6) == baseTagSave) || 
                        (i.ItemCode.Length >= 6 && i.ItemCode.Substring(0, 6) == baseTagSave) || 
                        i.Rack == rawTagSave || i.VIN == rawTagSave);
                
                if (item == null)
                {
                   // Fallback for tricky lookups
                   item = (await _context.Items.AsNoTracking().ToListAsync())
                        .FirstOrDefault(i => GetBaseVin(i.VIN) == baseTagSave || GetBaseVin(i.ItemCode) == baseTagSave || i.Rack == rawTagSave);
                }
                
                if (item == null) return Json(new { success = false, message = "Kode Rak/VIN tidak terdaftar di Master Data" });

                // 2. Final Schedule Matching (Strict Part Number Validation + FIFO)
                string kanbanSaveUpper = record.Kanban.ToUpper();
                string partNoSave = (item.CustomerPartNumber ?? "").ToUpper();
                string vinSave = (item.VIN ?? "").ToUpper();

                // Handle "kode didepan garis miring"
                string cleanKanbanSave = kanbanSaveUpper.Split('/')[0].Trim();
                string cleanPartNoSave = partNoSave.Split('/')[0].Trim();
                string cleanVinSave = vinSave.Split('/')[0].Trim();

                if (cleanKanbanSave != cleanPartNoSave && cleanKanbanSave != cleanVinSave)
                {
                    return Json(new { success = false, message = $"KANBAN tidak sesuai dengan produk!" });
                }

                var today = DateTime.Today;
                var schedule = await _context.DeliverySchedules
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
                    return Json(new { success = false, message = "Produk OK, tapi JADWAL tidak ditemukan" });
                }

                // --- VALIDASI 1: Cek apakah kebutuhan QTY sudah terpenuhi ---
                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                if (dItem != null && (dItem.ActualQuantity ?? 0) >= dItem.Quantity)
                {
                    return Json(new { success = false, message = $"QTY sudah CUKUP untuk jadwal ini" });
                }

                // --- VALIDASI 2: Cek Saldo Stok Riil (Per Tag & Label) ---
                var existsInPullingRecord = await _context.PullingRecords.AnyAsync(p => p.Tag == record.Tag && p.Label == record.Label);
                
                if (!existsInPullingRecord)
                {
                    return Json(new { success = false, message = "LABEL BELUM SCAN PULLING: Item ini belum masuk gudang!" });
                }

                // Check Pattern Match one last time for safety
                if (!baseLabelSave.ToUpper().Contains(baseTagSave.ToUpper()))
                {
                    return Json(new { success = false, message = "RAK MISMATCH: Label tidak sesuai dengan Rak ini!" });
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
                await _stockHubContext.Clients.All.SendAsync("updateStock");

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
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> GetSchedulesJson(DateTime? filterDate, string? vin)
        {
            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

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

            // 3. Priority Sorting & Projection
            var result = rawSchedules
                .Select(s => {
                    var status = s.PreparationStatus ?? s.Status;
                    int priority = 3; // Default (lowest)
                    
                    if (status == "In Progress" || status == "Preparing") priority = 0;
                    else if (status == "Scheduled" || status == "Waiting") priority = 1;
                    else if (status == "Prepared" || status == "Completed") priority = 2;

                    // Calculate Kanban Counts
                    double totalKanbanTarget = 0;
                    double totalKanbanActual = 0;

                    foreach(var item in s.DeliveryItems)
                    {
                        var qpc = (item.Item?.QtyLot != null && item.Item.QtyLot > 0) ? item.Item.QtyLot.Value : 1;
                        totalKanbanTarget += Math.Ceiling((double)item.Quantity / qpc);
                        totalKanbanActual += (double)(item.ActualQuantity ?? 0) / qpc;
                    }

                    // Get Unique VINs or Part Numbers
                    var vinList = s.DeliveryItems
                        .Where(di => di.Item != null)
                        .Select(di => !string.IsNullOrEmpty(di.Item.VIN) ? di.Item.VIN : di.Item.CustomerPartNumber)
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Distinct()
                        .ToList();

                    return new {
                        s.ScheduleId,
                        s.ScheduleNumber,
                        CustomerName = s.Customer?.CustomerName ?? "-",
                        Dock = string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "-") : s.Area,
                        VINs = string.Join(", ", vinList),
                        Status = status,
                        Priority = priority,
                        TotalKanbanActual = totalKanbanActual, 
                        TotalKanbanTarget = totalKanbanTarget,
                        ScheduledDate = s.ScheduledDate
                    };
                })
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.ScheduledDate)
                .ThenBy(x => x.ScheduleNumber);

            return Json(result);
        }
        private string GetBaseVin(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var normalized = input.Trim().ToUpper();
            // Take first 6 characters as the internal standard (e.g., TA1234LB -> TA1234)
            if (normalized.Length > 6) return normalized.Substring(0, 6);
            return normalized;
        }
    }
}
