using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using DeliveryControl.Data;

namespace DeliveryControl.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _logService;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, ActivityLogService logService, IHubContext<DeliveryHub> hubContext)
        {
            _logger = logger;
            _context = context;
            _logService = logService;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            // Load all schedules for today and those that transitioned to tomorrow
            // (ScheduledDate is the anchor for the cycle)
            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();

            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today)
                .OrderBy(s =>
                {
                    // Prepared scan selesai (walau Status masih In Progress) → tengah bawah
                    if (s.PreparationStatus == "Prepared")
                        return 2;
                    // Selesai delivery → paling bawah
                    if (s.Status == "Completed" || s.ActualEndTime.HasValue)
                        return 3;
                    // Preparing / In Progress → paling atas
                    if (s.PreparationStatus == "In Progress" || s.Status == "In Progress" || s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue)
                        return 0;
                    // Scheduled / Waiting
                    return 1;
                })
                .ThenBy(s => s.PickupTime)
                .ToList();

            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper()
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                                   (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled")
                })
                .OrderBy(g =>
                {
                    // Completed group → paling bawah
                    if (g.DisplayStatus == "Completed")
                        return 3;
                    // In Progress group → paling atas
                    if (g.DisplayStatus == "In Progress")
                        return 0;
                    // Scheduled / Waiting → tengah
                    return 1;
                })
                .ThenBy(g => g.First.PickupTime)
                .ToList();

            // Count Completed - Satu grup dianggap Completed jika SEMUA schedule di dalamnya selesai
            var completedCount = groupedSchedules.Count(g => g.DisplayStatus == "Completed");
            
            // Count In Progress - Satu grup dianggap In Progress jika ada yang mulai tapi belum semua selesai
            var inProgressCount = groupedSchedules.Count(g => g.DisplayStatus == "In Progress");
            
            // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
            var shouldBeDeliveredCount = todaySchedules
                .GroupBy(s => new { 
                    s.CustomerId, 
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(), 
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(), 
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Manifest = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(),
                    Date = s.ScheduledDate.Date 
                })
                .Count(g => (g.First().ETD <= now) == true);
            
            // Count Delay Pickup - Berdasarkan jadwal perwakilan grup
            var delayPickupCount = groupedSchedules.Count(g => 
                g.First.ActualStartTime == null && 
                g.First.PickupTime != null && 
                now > g.First.PickupTime.Value &&
                g.DisplayStatus != "Completed");
            
            // Count Delay Dock In (Not Arrived) - Berdasarkan jadwal perwakilan grup
            var delayDockInCount = groupedSchedules.Count(g => 
                g.First.ActualEnterDockTime == null && 
                g.First.EnterDockTime != null && 
                now > g.First.EnterDockTime.Value &&
                g.DisplayStatus != "Completed");
            
            // Count Delay Prepare (Slow Work) - Berdasarkan Waktu Sekarang vs End Prep/Std Dock In
            var delayPrepareCount = groupedSchedules.Count(g => {
                if (g.DisplayStatus == "Completed") return false;
                
                var isGroupPrepared = g.Schedules.All(s => s.PreparationStatus == "Prepared");
                var item = g.First;
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);
                var dockInTimeTarget = item.EnterDockTime;

                if (!isGroupPrepared)
                {
                    // Delay jika melewati target End Prep ATAU melewati jadwal masuk truk (Dock In)
                    return now > planEndTime || (dockInTimeTarget.HasValue && now > dockInTimeTarget.Value);
                }
                else 
                {
                    // Sudah siap semua, ambil waktu penyelesaian maksimal
                    var maxReadyTime = g.Schedules.Max(s => s.ReadyToDockTime);
                    if (maxReadyTime.HasValue)
                    {
                        return maxReadyTime.Value > planEndTime || (dockInTimeTarget.HasValue && maxReadyTime.Value > dockInTimeTarget.Value);
                    }
                }
                return false;
            });
            
            // Count Prepared (Siap Kirim) - Satu grup dianggap Prepared jika SEMUA schedule di dalamnya "Prepared"
            var preparedCount = groupedSchedules.Count(g => 
                g.Schedules.All(s => s.PreparationStatus == "Prepared") && 
                g.DisplayStatus != "Completed");
            
            // Set ViewBag
            ViewBag.TodaySchedules = groupedSchedules.Count;
            ViewBag.CompletedCount = completedCount;
            ViewBag.InProgressCount = inProgressCount;
            ViewBag.DelayPickupCount = delayPickupCount;
            ViewBag.NotArrivedCount = delayDockInCount;
            ViewBag.DelayPrepareCount = delayPrepareCount;
            ViewBag.PreparedCount = preparedCount;
            ViewBag.ShouldBeDeliveredCount = shouldBeDeliveredCount;

            return View(todaySchedules);
        }

        private string GetDisplayStatus(DeliverySchedule s)
        {
            if (s.ActualEndTime.HasValue) return "Completed";
            if (s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue || s.PreparationStatus == "In Progress") return "In Progress";
            return "Scheduled";
        }

        // Action for SignalR Dashboard Update (Polling replacement)
        [HttpGet]
        public async Task<IActionResult> GetScheduleTableData()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();

            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today)
                .OrderBy(s => (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue)) ? 0 : 
                             (s.Status == "Completed" || s.ActualEndTime.HasValue) ? 2 : 1)
                .ThenBy(s => s.PickupTime)
                .ToList();

            return PartialView("_ScheduleTablePartial", todaySchedules);
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStatistics()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();

            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today)
                .ToList();

            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper()
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                                   (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled")
                })
                .ToList();

            var completedCount = groupedSchedules.Count(g => g.DisplayStatus == "Completed");
            var inProgressCount = groupedSchedules.Count(g => g.DisplayStatus == "In Progress");
            
            var shouldBeDeliveredCount = todaySchedules
                .GroupBy(s => new { 
                    s.CustomerId, 
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(), 
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(), 
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Manifest = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(),
                    Date = s.ScheduledDate.Date 
                })
                .Count(g => (g.First().ETD <= now) == true);

            var delayPickupCount = groupedSchedules.Count(g => 
                g.First.ActualStartTime == null && 
                g.First.PickupTime != null && 
                now > g.First.PickupTime.Value &&
                g.DisplayStatus != "Completed");
            
            var delayDockInCount = groupedSchedules.Count(g => 
                g.First.ActualEnterDockTime == null && 
                g.First.EnterDockTime != null && 
                now > g.First.EnterDockTime.Value &&
                g.DisplayStatus != "Completed");
            
            var delayPrepareCount = groupedSchedules.Count(g => {
                if (g.DisplayStatus == "Completed") return false;
                
                var isGroupPrepared = g.Schedules.All(s => s.PreparationStatus == "Prepared");
                var item = g.First;
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);
                var dockInTimeTarget = item.EnterDockTime;

                if (!isGroupPrepared)
                {
                    // Delay jika melewati target End Prep ATAU melewati jadwal masuk truk (Dock In)
                    return now > planEndTime || (dockInTimeTarget.HasValue && now > dockInTimeTarget.Value);
                }
                else 
                {
                    // Sudah siap semua, ambil waktu penyelesaian maksimal
                    var maxReadyTime = g.Schedules.Max(s => s.ReadyToDockTime);
                    if (maxReadyTime.HasValue)
                    {
                        return maxReadyTime.Value > planEndTime || (dockInTimeTarget.HasValue && maxReadyTime.Value > dockInTimeTarget.Value);
                    }
                }
                return false;
            });

            var preparedCount = groupedSchedules.Count(g => 
                g.Schedules.All(s => s.PreparationStatus == "Prepared") && 
                g.DisplayStatus != "Completed");
            
            return Json(new
            {
                todaySchedules = groupedSchedules.Count,
                completedCount,
                shouldBeDeliveredCount,
                inProgressCount,
                delayPickupCount,
                notArrivedCount = delayDockInCount,
                delayPrepareCount = delayPrepareCount,
                preparedCount = preparedCount,
                timestamp = now.ToString("HH:mm:ss")
            });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
