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
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "User")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
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

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Index(DateTime? date = null)
        {
            var now = DateTime.Now;
            var today = date?.Date ?? DateTime.Today;
            var isViewingPast = date.HasValue && date.Value.Date != DateTime.Today;
            // Untuk data historis, gunakan waktu akhir hari (23:59:59); untuk hari ini, gunakan waktu sekarang
            var effectiveNow = isViewingPast ? today.AddDays(1).AddSeconds(-1) : now;

            // Load all schedules for today and those that transitioned to tomorrow
            // (ScheduledDate is the anchor for the cycle)
            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();

            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today ||
                            // Carry-over: jadwal dari hari-hari sebelumnya yang belum Completed (delay)
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue))
                .OrderBy(s =>
                {
                    bool isCarryOver = s.ScheduledDate.Date < today;
                    bool isInProgress = s.PreparationStatus == "In Progress" || s.Status == "In Progress"
                                        || s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue;
                    bool isPrepared  = s.PreparationStatus == "Prepared";
                    bool isCompleted = s.Status == "Completed" || s.ActualEndTime.HasValue;

                    // Selesai delivery → paling bawah (semua kondisi)
                    if (isCompleted) return 5;

                    if (today == DateTime.Today)
                    {
                        // ── HARI INI: urutan normal ──
                        // Prepared (sudah scan, menunggu kirim) → urutan ke-3
                        if (isPrepared) return 3;
                        // In Progress (sedang dikerjakan) → paling atas
                        if (isInProgress) return 1;
                        // Scheduled / Waiting → urutan ke-2
                        return 2;
                    }
                    else
                    {
                        // ── HARI LAIN (filter past/future) ──
                        // Carry-over In Progress (masih dikerjakan, dari hari lalu) → paling atas
                        if (isCarryOver && isInProgress) return 0;
                        // Carry-over Scheduled (belum dikerjakan, dari hari lalu) → urutan ke-2
                        if (isCarryOver) return 1;
                        // Jadwal hari ini In Progress → urutan ke-3
                        if (isInProgress) return 2;
                        // Jadwal hari ini Scheduled → urutan ke-4
                        if (!isPrepared) return 3;
                        // Prepared → urutan ke-5
                        return 4;
                    }
                })
                .ThenBy(s => s.ScheduledDate)
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
                    IsCarryOver = g.All(s => s.ScheduledDate.Date < today),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                                   (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled"),
                    IsPrepared = g.All(s => s.PreparationStatus == "Prepared")
                })
                .OrderBy(g =>
                {
                    // Completed group → paling bawah (semua kondisi)
                    if (g.DisplayStatus == "Completed")
                        return 5;

                    if (today == DateTime.Today)
                    {
                        // ── HARI INI: urutan normal ──
                        // Prepared (scan selesai, belum kirim) → urutan ke-3
                        if (g.IsPrepared) return 3;
                        // In Progress (sedang dikerjakan) → paling atas
                        if (g.DisplayStatus == "In Progress") return 1;
                        // Scheduled / Waiting → urutan ke-2
                        return 2;
                    }
                    else
                    {
                        // ── HARI LAIN (filter tanggal past/future) ──
                        // Carry-over In Progress (masih dikerjakan dari hari lalu) → paling atas
                        if (g.IsCarryOver && g.DisplayStatus == "In Progress") return 0;
                        // Carry-over Scheduled (belum dikerjakan dari hari lalu) → urutan ke-2
                        if (g.IsCarryOver) return 1;
                        // Jadwal hari ini In Progress → urutan ke-3
                        if (g.DisplayStatus == "In Progress") return 2;
                        // Jadwal hari ini Scheduled → urutan ke-4
                        if (!g.IsPrepared) return 3;
                        // Prepared → urutan ke-5
                        return 4;
                    }
                })
                .ThenBy(g => g.First.ScheduledDate)
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
                .Count(g => (g.First().ETD <= effectiveNow) == true);
            
            // Count Delay Pickup - Berdasarkan jadwal perwakilan grup
            var delayPickupCount = groupedSchedules.Count(g => 
                g.First.ActualStartTime == null && 
                g.First.PickupTime != null && 
                effectiveNow > g.First.PickupTime.Value &&
                g.DisplayStatus != "Completed");
            
            // Count Delay Dock In (Not Arrived) - Berdasarkan jadwal perwakilan grup
            var delayDockInCount = groupedSchedules.Count(g => 
                g.First.ActualEnterDockTime == null && 
                g.First.EnterDockTime != null && 
                effectiveNow > g.First.EnterDockTime.Value &&
                g.DisplayStatus != "Completed");
            
            // Count Delay Prepare (Slow Work) - Berdasarkan Waktu Sekarang vs End Prep/Std Dock In
            var delayPrepareCount = groupedSchedules.Count(g => {
                if (g.DisplayStatus == "Completed") return false;
                
                var isGroupPrepared = g.Schedules.All(s => s.PreparationStatus == "Prepared");
                var item = g.First;
                var stdPMinutes   = item.StdPrepareTime > 0   ? item.StdPrepareTime   : (item.Customer?.StdPrepareTime ?? 0);
                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var planEndTime   = item.ScheduledDate.Date.AddMinutes(stdPMinutes);
                // Koreksi lintas tengah malam: jika End Prep < Start Prep, End Prep ada di hari berikutnya
                if (stdPMinutes > 0 && startPMinutes > stdPMinutes)
                    planEndTime = planEndTime.AddDays(1);
                var dockInTimeTarget = item.EnterDockTime;

                if (!isGroupPrepared)
                {
                    // Delay jika melewati target End Prep ATAU melewati jadwal masuk truk (Dock In)
                    return effectiveNow > planEndTime || (dockInTimeTarget.HasValue && effectiveNow > dockInTimeTarget.Value);
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
            ViewBag.SelectedDate = today.ToString("yyyy-MM-dd");

            return View(todaySchedules);
        }

        private string GetDisplayStatus(DeliverySchedule s)
        {
            if (s.ActualEndTime.HasValue) return "Completed";
            if (s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue || s.PreparationStatus == "In Progress") return "In Progress";
            return "Scheduled";
        }

        // Action for SignalR Dashboard Update (Polling replacement)
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
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
                .Where(s => s.ScheduledDate.Date == today ||
                            // Carry-over: jadwal dari hari-hari sebelumnya yang belum Completed
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue))
                .OrderBy(s =>
                {
                    bool isCarryOver = s.ScheduledDate.Date < today;
                    bool isInProgress = s.PreparationStatus == "In Progress" || s.Status == "In Progress"
                                        || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue)
                                        || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue);
                    bool isPrepared  = s.PreparationStatus == "Prepared";
                    bool isCompleted = s.Status == "Completed" || s.ActualEndTime.HasValue;

                    if (isCompleted) return 5;
                    // GetScheduleTableData selalu untuk hari ini (today == DateTime.Today)
                    // urutan normal: In Progress → Scheduled → Prepared
                    if (isCarryOver && isInProgress) return 0;
                    if (isCarryOver) return 1;
                    if (isPrepared) return 3;
                    if (isInProgress) return 2;
                    return 2;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.PickupTime)
                .ToList();

            return PartialView("_ScheduleTablePartial", todaySchedules);
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
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
                .Where(s => s.ScheduledDate.Date == today ||
                            // Carry-over: jadwal dari hari-hari sebelumnya yang belum Completed
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue))
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
                var stdPMinutes   = item.StdPrepareTime > 0   ? item.StdPrepareTime   : (item.Customer?.StdPrepareTime ?? 0);
                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var planEndTime   = item.ScheduledDate.Date.AddMinutes(stdPMinutes);
                // Koreksi lintas tengah malam
                if (stdPMinutes > 0 && startPMinutes > stdPMinutes)
                    planEndTime = planEndTime.AddDays(1);
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
            var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            if (feature?.Error != null)
            {
                try
                {
                    System.IO.File.AppendAllText(@"C:\DeliveryControl\DeliveryControl\error_log.txt", 
                        $"{DateTime.Now}: {feature.Error.ToString()}\n\n");
                }
                catch { }
            }
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
