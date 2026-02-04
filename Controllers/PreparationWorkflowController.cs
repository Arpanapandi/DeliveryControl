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

        public PreparationWorkflowController(
            ApplicationDbContext context, 
            IHubContext<StockHub> stockHubContext,
            IHubContext<DeliveryHub> deliveryHubContext)
        {
            _context = context;
            _stockHubContext = stockHubContext;
            _deliveryHubContext = deliveryHubContext;
        }

        public async Task<IActionResult> Index()
        {
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.Status != "Cancelled" && s.Status != "Completed")
                .OrderByDescending(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();
            
            return View(schedules);
        }

        [HttpGet]
        public async Task<IActionResult> LookupSchedule(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Json(new { success = false });

            try
            {
                tag = tag.Trim();
                // 1. Find Item
                var item = await _context.Items.FirstOrDefaultAsync(i => i.VIN == tag || i.ItemCode == tag);
                if (item == null)
                {
                    // Try pulling records
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == tag);
                    if (pulling != null) item = await _context.Items.FindAsync(pulling.ItemId);
                }

                if (item == null) return Json(new { success = false, message = "Tag tidak dikenali di Database (Item/Stock)." });

                // 2. Find FIFO Schedule
                var schedule = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") && 
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId))
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .FirstOrDefaultAsync();

                if (schedule == null)
                {
                    return Json(new { 
                        success = true, 
                        found = false, 
                        item = new { item.ItemName, item.VIN, item.CustomerPartNumber, item.Plant },
                        message = "Item dikenali, tapi tidak ada jadwal aktif yang membutuhkan ini." 
                    });
                }

                return Json(new { 
                    success = true, 
                    found = true,
                    item = new { item.ItemName, item.VIN, item.CustomerPartNumber, item.Plant, TargetPartNo = !string.IsNullOrEmpty(item.CustomerPartNumber) ? item.CustomerPartNumber : item.VIN },
                    schedule = new { 
                        schedule.ScheduleId, 
                        schedule.ScheduleNumber, 
                        CustomerName = schedule.Customer?.CustomerName,
                        Dock = schedule.Customer?.Docking,
                        Date = schedule.ScheduledDate.ToString("dd MMM yyyy")
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
            if (record == null)
            {
                return Json(new { success = false, message = "Data kosong." });
            }

            if (string.IsNullOrWhiteSpace(record.Tag) || string.IsNullOrWhiteSpace(record.Label))
            {
                return Json(new { success = false, message = "Tag dan Label wajib diisi." });
            }

            try 
            {
                record.Tag = record.Tag.Trim();
                record.Label = record.Label.Trim();
                record.Kanban = (record.Kanban ?? "").Trim();
                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification
                var item = await _context.Items.FirstOrDefaultAsync(i => i.VIN == record.Tag || i.ItemCode == record.Tag);
                if (item == null)
                {
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == record.Tag);
                    if (pulling != null) item = await _context.Items.FindAsync(pulling.ItemId);
                }

                if (item == null) return Json(new { success = false, message = "Tag tidak dikenali." });

                record.Plant = item.Plant ?? "-";

                // 2. Validation (Customer Part Number / VIN Translation)
                // Logic: Compare Customer Label with Part Number, OR translate to VIN if Part Number is empty.
                string partNo = item.CustomerPartNumber ?? "";
                string vin = item.VIN ?? "";
                
                bool isValid = false;
                string requiredCode = "";

                if (!string.IsNullOrEmpty(partNo))
                {
                    requiredCode = partNo;
                    if (record.Label.Contains(partNo) || record.Kanban.Contains(partNo))
                        isValid = true;
                }
                else if (!string.IsNullOrEmpty(vin))
                {
                    // TRANSLATION LOGIC: Fallback to VIN if Part Number is not available
                    requiredCode = vin;
                    if (record.Label.Contains(vin) || record.Kanban.Contains(vin))
                        isValid = true;
                }
                else
                {
                    // No part number and no VIN? Skip validation but log success for now
                    isValid = true; 
                }

                if (!isValid)
                {
                    return Json(new { success = false, message = $"VALIDASI GAGAL: Barcode Customer tidak mengandung '{requiredCode}'!" });
                }
                // 3. FIFO Schedule Matching
                var schedule = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") && 
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId))
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .FirstOrDefaultAsync();

                if (schedule == null)
                {
                    return Json(new { success = false, message = "Tidak ada jadwal aktif untuk item ini." });
                }

                record.ScheduleId = schedule.ScheduleId;

                // 4. Update Stats
                schedule.TotalActualQuantity += 1;
                schedule.Status = "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                // Update specific item actual quantity if exists
                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                if (dItem != null)
                {
                    dItem.ActualQuantity = (dItem.ActualQuantity ?? 0) + 1;
                    if (dItem.ActualQuantity >= dItem.Quantity) dItem.IsCompleted = true;
                }

                _context.PreparationRecords.Add(record);
                await _context.SaveChangesAsync();
                
                // Broadcast
                await _deliveryHubContext.Clients.All.SendAsync("DeliveryUpdated", new { Action = "preparation", ScheduleNumber = schedule.ScheduleNumber });
                await _stockHubContext.Clients.All.SendAsync("UpdateStock");

                return Json(new { success = true, message = $"Berhasil! Item '{record.Tag}' masuk ke {schedule.ScheduleNumber}." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
    }
}
