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
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled" && s.Status != "Completed");

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

            var schedules = await query
                .OrderBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();
            
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

                if (!items.Any()) return Json(new { success = false, message = "Tag internal tidak dikenali." });

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

        DeliverySchedule? schedule = null;

        // 3. Validation and Disambiguation
        if (!string.IsNullOrEmpty(kanban))
        {
            string kanbanUpper = kanban.ToUpper();
            schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") && 
                             s.ScheduleNumber.ToUpper() == kanbanUpper &&
                             s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId))
                .FirstOrDefaultAsync();

            if (schedule == null) 
            {
                return Json(new { 
                    success = true, 
                    found = false, 
                    step = "kanban",
                    item = new { 
                        itemName = targetItem.ItemName, 
                        vin = targetItem.VIN, 
                        customerPartNumber = targetItem.CustomerPartNumber 
                    },
                    message = $"Kanban (Manifest) '{kanban}' tidak cocok dengan jadwal aktif untuk item ini!" 
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
                            customerPartNumber = targetItem.CustomerPartNumber 
                        },
                        message = "Label tidak sesuai dengan Tag produk dan tidak ditemukan di stok!" 
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
                    customerPartNumber = targetItem.CustomerPartNumber 
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
                    customerPartNumber = targetItem.CustomerPartNumber 
                },
                message = $"Item valid untuk {targetItem.Customer}, tapi tidak ada jadwal aktif saat ini." 
            });
        }

                var deliveryItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == targetItem.ItemId);

                return Json(new { 
                    success = true, 
                    found = true,
                    step = "complete",
                    item = new { targetItem.ItemName, targetItem.VIN, targetItem.CustomerPartNumber, TargetPartNo = !string.IsNullOrEmpty(targetItem.CustomerPartNumber) ? targetItem.CustomerPartNumber : targetItem.VIN },
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

            try 
            {
                record.Tag = record.Tag.Trim();
                record.Label = record.Label.Trim();
                record.Kanban = record.Kanban.Trim();
                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification & Disambiguation
                var items = await _context.Items
                    .Where(i => i.VIN == record.Tag || i.ItemCode == record.Tag)
                    .ToListAsync();
                
                if (!items.Any())
                {
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == record.Tag);
                    if (pulling != null)
                    {
                        var pItem = await _context.Items.FindAsync(pulling.ItemId);
                        if (pItem != null) items.Add(pItem);
                    }
                }

        if (!items.Any()) return Json(new { success = false, message = "Tag tidak dikenali." });

        // For now, assume the first item match is the target (usually VIN is unique)
        var item = items.First();

        // 2. Final Schedule Matching using Kanban (Manifest)
        string kanbanSaveUpper = record.Kanban.ToUpper();
        var schedule = await _context.DeliverySchedules
            .Include(s => s.Customer)
            .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
            .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") && 
                         s.ScheduleNumber.ToUpper() == kanbanSaveUpper &&
                         s.DeliveryItems.Any(di => di.ItemId == item.ItemId))
            .OrderBy(s => s.ScheduledDate)
            .ThenBy(s => s.ScheduleNumber)
            .FirstOrDefaultAsync();

        if (schedule == null) 
        {
            return Json(new { success = false, message = $"Validasi Gagal! Kanban (Manifest) '{record.Kanban}' tidak ditemukan untuk item ini." });
        }

        // --- VALIDASI 1: Cek apakah kebutuhan QTY sudah terpenuhi ---
        var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
        if (dItem != null && (dItem.ActualQuantity ?? 0) >= dItem.Quantity)
        {
            return Json(new { success = false, message = $"Kebutuhan item '{item.ItemName}' ({dItem.Quantity} pcs) sudah terpenuhi untuk jadwal ini!" });
        }

        // --- VALIDASI 2: Cek Saldo Stok Riil (FIFO / Stock On Hand) ---
        // Hitung saldo: Total Pulling (All Time) - Total Preparation (All Time) untuk Tag/Label ini
        var totalPulled = await _context.PullingRecords
            .CountAsync(p => p.Tag == record.Tag && p.Label == record.Label);
        
        var totalPrepared = await _context.PreparationRecords
            .CountAsync(p => p.Tag == record.Tag && p.Label == record.Label);
        
        // Saldo riil di rak untuk fisik barang dengan label spesifik ini
        int stockBalance = totalPulled - totalPrepared;

        if (stockBalance <= 0)
        {
            return Json(new { success = false, message = $"STOK HABIS! Barang dengan Label ini sudah pernah dipakai atau belum di-Pulling ke rak." });
        }

        // Strict label string check removed.
        // We rely on stockBalance check above to ensure the Tag+Label pair exists in inventory.
        // bool labelOk = labelUpper.Contains(vin) || (!string.IsNullOrEmpty(partNo) && labelUpper.Contains(partNo));
        
        // if (!labelOk)
        // {
        //    return Json(new { success = false, message = "Label tidak sesuai dengan produk internal!" });
        // }

        record.Plant = item.Plant ?? "-";
        record.ScheduleId = schedule.ScheduleId;

                // 3. Update Stats
                // LOGIKA BARU: 1x Input = 1 Kanban (Box)
                // Jadi Actual Qty bertambah sebesar QPC (Qty per Lot), bukan bertambah 1
                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;
                
                schedule.TotalActualQuantity += qpc;
                schedule.Status = "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                if (dItem != null)
                {
                    dItem.ActualQuantity = (dItem.ActualQuantity ?? 0) + qpc;
                    if (dItem.ActualQuantity >= dItem.Quantity) dItem.IsCompleted = true;
                }

                // Check if ALL items in this schedule are completed
                bool allDone = schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);

                if (allDone)
                {
                    schedule.PreparationStatus = "Prepared";
                    schedule.Status = "In Progress"; 

                    // --- LOGIKA AUTO-CONFIRM ENTER DOCK ---
                    // Jika preparation selesai, otomatis set status truk sudah masuk dock
                    if (!schedule.ActualEnterDockTime.HasValue)
                    {
                        var now = DateTime.Now;
                        schedule.ActualEnterDockTime = now;
                        
                        // Jika driver belum confirm arrival, otomatis set juga
                        if (!schedule.ActualStartTime.HasValue)
                        {
                            schedule.ActualStartTime = now;
                            schedule.DriverStatus = "In Progress";
                        }
                        
                        schedule.UpdatedBy = "Auto-System";

                        // Log Activity
                        await _logService.LogConfirm(
                            "System",
                            schedule.ScheduleNumber,
                            schedule.ScheduleId,
                            $"Auto-Confirm: Preparation Selesai -> Otomatis Set Masuk Dock pada {now:HH:mm}",
                            "System"
                        );

                        // Broadcast EXTRA notification for Enter Dock
                        await _deliveryHubContext.Clients.All.SendAsync("DeliveryUpdated", new { 
                            Action = "enterDock", 
                            ScheduleNumber = schedule.ScheduleNumber,
                            Message = $"[AUTO] Persiapan Selesai! Truk masuk dock untuk {schedule.Customer?.CustomerName}",
                            Timestamp = now
                        });
                    }
                    // ---------------------------------------
                }

                _context.PreparationRecords.Add(record);
                await _context.SaveChangesAsync();
                
                await _deliveryHubContext.Clients.All.SendAsync("DeliveryUpdated", new { Action = "preparation", ScheduleNumber = schedule.ScheduleNumber });
                await _stockHubContext.Clients.All.SendAsync("UpdateStock");

                // Return detailed info for the UI progress area
                var totalTarget = schedule.DeliveryItems.Sum(di => di.Quantity);
                var totalActual = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                var totalPercent = totalTarget > 0 ? ((double)totalActual / (double)totalTarget * 100) : 0;

                var details = new
                {
                    schedule.ScheduleId,
                    schedule.ScheduleNumber,
                    CustomerName = schedule.Customer?.CustomerName,
                    TotalPercent = totalPercent,
                    Items = schedule.DeliveryItems.Select(di => {
                        var qpc = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                        return new {
                            ItemName = di.Item?.ItemName ?? "Unknown",
                            VIN = di.Item?.VIN ?? "-",
                            Target = di.Quantity,
                            Actual = di.ActualQuantity ?? 0,
                            TargetKanban = Math.Ceiling((double)di.Quantity / qpc),
                            ActualKanban = ((double)(di.ActualQuantity ?? 0) / qpc)
                        };
                    }).ToList()
                };

                return Json(new { 
                    success = true, 
                    message = $"Berhasil! Persiapan tersimpan.",
                    scheduleDetails = details
                });
            }
            catch (Exception ex)
            {
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
            await _deliveryHubContext.Clients.All.SendAsync("DeliveryUpdated", new { Action = "preparation", ScheduleNumber = schedule.ScheduleNumber });

            return Json(new { success = true });
        }
    }
}
