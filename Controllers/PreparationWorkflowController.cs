using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Controllers
{
    public class PreparationWorkflowController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryControl.Hubs.StockHub> _stockHubContext;
        private readonly IHubContext<DeliveryControl.Hubs.DeliveryHub> _deliveryHubContext;

        public PreparationWorkflowController(
            ApplicationDbContext context, 
            IHubContext<DeliveryControl.Hubs.StockHub> stockHubContext,
            IHubContext<DeliveryControl.Hubs.DeliveryHub> deliveryHubContext)
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

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PreparationRecord record)
        {
            if (ModelState.IsValid)
            {
                // Trim strings for data consistency
                record.Tag = (record.Tag ?? "").Trim();
                record.Label = (record.Label ?? "").Trim();
                record.Kanban = (record.Kanban ?? "").Trim();

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";
                
                _context.PreparationRecords.Add(record);

                // Update Delivery Schedule if linked
                if (record.ScheduleId.HasValue)
                {
                    var schedule = await _context.DeliverySchedules.FindAsync(record.ScheduleId.Value);
                    if (schedule != null)
                    {
                        // Increment Actual Quantity
                        schedule.TotalActualQuantity += 1;
                        
                        // Update Status
                        if (schedule.Status == "Scheduled")
                        {
                            schedule.Status = "In Progress";
                        }
                        
                        if (schedule.PreparationStatus == "Scheduled" || string.IsNullOrEmpty(schedule.PreparationStatus))
                        {
                            schedule.PreparationStatus = "In Progress";
                        }

                        // Broadcast update via DeliveryHub to Dashboard
                        await _deliveryHubContext.Clients.All.SendAsync("DeliveryUpdated", new
                        {
                            ScheduleNumber = schedule.ScheduleNumber,
                            Action = "preparation",
                            Message = $"Preparation scan untuk {schedule.ScheduleNumber}. Total: {schedule.TotalActualQuantity.ToString("N0")}/{schedule.TotalTargetQuantity.ToString("N0")}",
                            Timestamp = DateTime.Now
                        });
                    }
                }

                await _context.SaveChangesAsync();

                // Notify Stock Dashboard
                await _stockHubContext.Clients.All.SendAsync("UpdateStock");
                
                return Json(new { success = true, message = "Data preparation berhasil disimpan!" });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }
    }
}
