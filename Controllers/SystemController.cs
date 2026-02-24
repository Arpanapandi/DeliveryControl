using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Controllers
{
    public class SystemController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SystemController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> ClearTransactions()
        {
            // Clear transaction tables using raw SQL for speed on SQLite
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM PreparationRecords");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM DeliveryItems");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM DeliverySchedules");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM PullingRecords");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM ActivityLogs");

            // Vacuum database to recover space and reset IDs if possible (optional)
            await _context.Database.ExecuteSqlRawAsync("VACUUM");

            TempData["SuccessMessage"] = "Semua data transaksi (Schedule, Pulling, Prep, Log) telah dikosongkan.";
            
            return RedirectToAction("Index", "Home");
        }
        public async Task<IActionResult> ConsolidateSchedules()
        {
            var activeSchedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Include(s => s.PreparationRecords)
                .Where(s => s.Status != "Completed" && s.Status != "Cancelled")
                .ToListAsync();

            // Grouping logic: Manifest (extracted from number before /), Customer, Route, Cycle, Area
            var grouped = activeSchedules
                .GroupBy(s => new {
                    Manifest = (s.ScheduleNumber ?? "").Split('/')[0].Split('-')[0] == "SCH" ? "" : (s.ScheduleNumber ?? "").Split('/')[0],
                    s.CustomerId,
                    Route = s.Route ?? "",
                    Cycle = s.Cycle ?? "",
                    Area = s.Area ?? ""
                })
                .Where(g => g.Count() > 1);

            int mergedCount = 0;

            foreach (var group in grouped)
            {
                var target = group.OrderBy(s => s.CreatedDate).First();
                var others = group.Where(s => s.ScheduleId != target.ScheduleId).ToList();

                foreach (var source in others)
                {
                    // Move DeliveryItems
                    foreach (var item in source.DeliveryItems.ToList())
                    {
                        var existing = target.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                        if (existing != null)
                        {
                            existing.Quantity += item.Quantity;
                            existing.ActualQuantity += item.ActualQuantity;
                            _context.DeliveryItems.Remove(item);
                        }
                        else
                        {
                            item.ScheduleId = target.ScheduleId;
                            target.DeliveryItems.Add(item);
                        }
                    }

                    // Move PreparationRecords
                    foreach (var rec in source.PreparationRecords.ToList())
                    {
                        rec.ScheduleId = target.ScheduleId;
                    }

                    _context.DeliverySchedules.Remove(source);
                    mergedCount++;
                }

                target.TotalTargetQuantity = (decimal)target.DeliveryItems.Sum(di => di.Quantity);
                target.TotalActualQuantity = (decimal)target.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                target.UpdatedDate = DateTime.Now;
                target.UpdatedBy = "SystemConsolidation";
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Berhasil menggabungkan {mergedCount} kartu duplikat.";
            return RedirectToAction("Index", "Home");
        }
    }
}
