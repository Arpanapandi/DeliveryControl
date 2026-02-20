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

                // 1. Find ALL Items by Tag (Internal)
                var items = await _context.Items
                    .Where(i => i.VIN == tag || i.ItemCode == tag)
                    .ToListAsync();
                
                if (!items.Any())
                {
                    // Fallback to pulling records
                    var pulling = await _context.PullingRecords
                        .OrderByDescending(p => p.CreatedDate)
                        .FirstOrDefaultAsync(p => p.Tag == tag);
                    if (pulling != null)
                    {
                        var pItem = await _context.Items.FindAsync(pulling.ItemId);
                        if (pItem != null) items.Add(pItem);
                    }
                }

                if (!items.Any()) return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });

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
                                 s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId))
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
            string vin = (targetItem.VIN ?? "").ToUpper();
            string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();
            string labelUpper = label.ToUpper();
            
            if (!labelUpper.Contains(vin) && (string.IsNullOrEmpty(partNo) || !labelUpper.Contains(partNo)))
            {
                // Fallback: Cek apakah kombinasi Tag & Label ini pernah di-scan di Pulling (Valid secara data historis)
                bool existsInPulling = await _context.PullingRecords
                    .AnyAsync(p => p.Tag == tag && p.Label == label);

                if (!existsInPulling)
                {
                    return Json(new { 
                        success = true, 
                        found = false, 
                        step = "label",
                        item = new { 
                            itemName = targetItem.ItemName, 
                            vin = targetItem.VIN, 
                            customerPartNumber = targetItem.CustomerPartNumber,
                            qtyLot = targetItem.QtyLot ?? 0,
                            currentStock = currentStock
                        },
                        message = "LABEL tidak sesuai dengan produk!" 
                    });
                }
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
                             s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId))
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

                // Double-Submission Prevention: Check if this exact scan was recorded in the last 10 seconds
                var recentDuplicate = await _context.PreparationRecords
                    .AnyAsync(p => p.Tag == record.Tag && p.Label == record.Label && p.CreatedDate > DateTime.Now.AddSeconds(-10));
                
                if (recentDuplicate)
                {
                    return Json(new { success = false, message = "Data sedang diproses atau sudah tersimpan (Duplicate Prevention)." });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification & Item Lookup
                var item = await _context.Items
                    .FirstOrDefaultAsync(i => i.VIN == record.Tag || i.ItemCode == record.Tag);
                
                if (item == null)
                {
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == record.Tag);
                    if (pulling != null)
                    {
                        item = await _context.Items.FindAsync(pulling.ItemId);
                    }
                }

                if (item == null) return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });

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

                var schedule = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") && 
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId))
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

                // --- VALIDASI 2: Cek Saldo Stok Riil (Per Label & FIFO) ---
                var countPulled = await _context.PullingRecords.CountAsync(p => p.Tag == record.Tag && p.Label == record.Label);
                var countPrepared = await _context.PreparationRecords.CountAsync(p => p.Tag == record.Tag && p.Label == record.Label);

                var allPullings = await _context.PullingRecords.Where(p => p.Tag == record.Tag).OrderBy(p => p.CreatedDate).ToListAsync();
                var allPreps = await _context.PreparationRecords.Where(p => p.Tag == record.Tag).ToListAsync();

                var consumedPullingIds = new HashSet<int>();
                foreach (var p in allPreps)
                {
                    var pLabel = (p.Label ?? "").Trim().ToUpper();
                    var match = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId) && (pl.Label ?? "").Trim().ToUpper() == pLabel);
                    if (match != null) consumedPullingIds.Add(match.PullingId);
                }

                if (countPulled <= countPrepared)
                {
                    var replacement = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId));
                    if (replacement != null)
                    {
                        record.Label = replacement.Label; 
                    }
                    else
                    {
                        return Json(new { success = false, message = "STOCK tidak tersedia di Rak" });
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
                            schedule.ReadyToDockTime = DateTime.Now;
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

                if (groupSchedules.All(gs => gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                {
                    var scanTime = DateTime.Now;
                    foreach (var gs in groupSchedules)
                    {
                        if (gs.PreparationStatus != "Prepared")
                        {
                            gs.PreparationStatus = "Prepared";
                            if (!gs.ReadyToDockTime.HasValue)
                            {
                                gs.ReadyToDockTime = scanTime;
                            }
                            gs.UpdatedDate = scanTime;
                        }
                    }
                }

                _context.PreparationRecords.Add(record);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                var totalTarget = schedule.DeliveryItems.Sum(di => di.Quantity);
                var totalActual = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                var totalPercent = totalTarget > 0 ? ((double)totalActual / (double)totalTarget * 100) : 0;

                var broadcastData = new {
                    action = "preparation",
                    scheduleNumber = schedule.ScheduleNumber,
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
    }
}
