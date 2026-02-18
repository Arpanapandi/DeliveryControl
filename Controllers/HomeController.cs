using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DeliveryControl.Models;
using DeliveryControl.Data;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, IConfiguration configuration)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        var now = DateTime.Now;
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);

        // Ambil kandidat schedule dari kemarin, hari ini, DAN BESOK
        // Karena ada schedule yang dibuat kemarin (EnterDock H-1) tapi Pickup-nya hari ini
        var allSchedulesRaw = await _context.DeliverySchedules
            .Include(d => d.Customer)
            .Include(d => d.DeliveryItems)
                .ThenInclude(di => di.Item)
            .Where(s => s.ScheduledDate.Date == yesterday || 
                       s.ScheduledDate.Date == today || 
                       s.ScheduledDate.Date == tomorrow)
            .ToListAsync();
        
        _logger.LogWarning($"Total raw schedules (yesterday + today + tomorrow): {allSchedulesRaw.Count}");

        // LOGIKA BARU: Filter berdasarkan TANGGAL PICKUP, bukan tanggal operasional
        // - Schedule dengan PickupTime tanggal HARI INI = tampil di dashboard hari ini
        // - Schedule dengan PickupTime tanggal BESOK = tampil sebagai "schedule besok" (dari autoscheduler jam 18:00)
        
        var todayPickupSchedules = allSchedulesRaw
            .Where(s => (s.PickupTime.HasValue && s.PickupTime.Value.Date == today) || 
                        (!s.PickupTime.HasValue && s.ScheduledDate.Date == today))
            .ToList();
        
        var tomorrowPickupSchedules = allSchedulesRaw
            .Where(s => (s.PickupTime.HasValue && s.PickupTime.Value.Date == tomorrow) || 
                        (!s.PickupTime.HasValue && s.ScheduledDate.Date == tomorrow))
            .ToList();
        
        _logger.LogWarning($"Schedules with PICKUP date = TODAY ({today:yyyy-MM-dd}): {todayPickupSchedules.Count}");
        _logger.LogWarning($"Schedules with PICKUP date = TOMORROW ({tomorrow:yyyy-MM-dd}): {tomorrowPickupSchedules.Count}");
        
        // Hitung statistik HANYA dari schedule dengan PICKUP hari ini (tidak termasuk besok)
        var activeSchedules = todayPickupSchedules
            .Where(s => s.Status != "Cancelled")
            .ToList();
        
        // HITUNG STATISTIK BERDASARKAN GRUP (MANIFEST)
        var groupedSchedules = activeSchedules
            .GroupBy(s => new { s.CustomerId, s.Area, s.Cycle, Date = s.ScheduledDate.Date })
            .Select(g => new { 
                Schedules = g.ToList(),
                First = g.First(),
                DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                               (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled")
            })
            .ToList();

        // Count Completed - Satu grup dianggap Completed jika SEMUA schedule di dalamnya selesai
        var completedCount = groupedSchedules.Count(g => g.DisplayStatus == "Completed");
        
        // Count In Progress - Satu grup dianggap In Progress jika ada yang mulai tapi belum semua selesai
        var inProgressCount = groupedSchedules.Count(g => g.DisplayStatus == "In Progress");
        
        // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
        // Menggunakan grouping dari all today pickup schedules
        var shouldBeDeliveredCount = todayPickupSchedules
            .GroupBy(s => new { s.CustomerId, s.Area, s.Cycle, Date = s.ScheduledDate.Date })
            .Count(g => (g.First().ETD <= now) == true);
        
        // Count Delay Pickup - Berdasarkan jadwal perwakilan grup
        var delayPickupCount = groupedSchedules.Count(g => 
            g.First.ActualStartTime == null && 
            g.First.PickupTime != null && 
            now > g.First.PickupTime.Value &&
            g.DisplayStatus != "Completed");
        
        // Count Delay Prepare (Not Arrived) - Berdasarkan jadwal perwakilan grup
        var delayPrepareCount = groupedSchedules.Count(g => 
            g.First.ActualEnterDockTime == null && 
            g.First.EnterDockTime != null && 
            now > g.First.EnterDockTime.Value &&
            g.DisplayStatus != "Completed");
        
        // Count Prepared (Siap Kirim) - Satu grup dianggap Prepared jika SEMUA schedule di dalamnya "Prepared"
        var preparedCount = groupedSchedules.Count(g => 
            g.Schedules.All(s => s.PreparationStatus == "Prepared") && 
            g.DisplayStatus != "Completed");
        
        // Set ViewBag
        ViewBag.TodaySchedules = groupedSchedules.Count;
        ViewBag.CompletedCount = completedCount;
        ViewBag.InProgressCount = inProgressCount;
        ViewBag.DelayPickupCount = delayPickupCount;
        ViewBag.NotArrivedCount = delayPrepareCount;
        ViewBag.PreparedCount = preparedCount;
        ViewBag.ShouldBeDeliveredCount = shouldBeDeliveredCount;
        
        // Gabungkan schedule hari ini + besok, dengan prioritas:
        // GRUP 1: Schedule dengan PICKUP HARI INI
        // GRUP 2: Schedule dengan PICKUP BESOK (dari autoscheduler jam 18:00)
        var dashboardSchedules = new List<DeliverySchedule>();
        
        // Tambahkan schedule PICKUP HARI INI dulu (sorted)
        var todaySchedulesSorted = SortSchedulesByPriority(activeSchedules, now);
        dashboardSchedules.AddRange(todaySchedulesSorted);
        
        // Tambahkan schedule PICKUP BESOK di bawahnya (sorted)
        var tomorrowActiveSchedules = tomorrowPickupSchedules
            .Where(s => s.Status != "Cancelled")
            .ToList();
        var tomorrowSchedulesSorted = SortSchedulesByPriority(tomorrowActiveSchedules, now);
        dashboardSchedules.AddRange(tomorrowSchedulesSorted);
        
        _logger.LogWarning($"Total schedules to display: {dashboardSchedules.Count} (Pickup Today: {todaySchedulesSorted.Count}, Pickup Tomorrow: {tomorrowSchedulesSorted.Count})");
        
        // Pass informasi ke view untuk grouping
        ViewBag.TodayScheduleCount = todaySchedulesSorted.Count;
        ViewBag.TomorrowScheduleCount = tomorrowSchedulesSorted.Count;
        ViewBag.DisplayDate = today;

        return View(dashboardSchedules);
    }
    
    /// <summary>
    /// Helper method untuk menentukan display status berdasarkan actual times
    /// Prioritas: ActualEndTime > ActualStartTime > Database Status
    /// </summary>
    private string GetDisplayStatus(DeliverySchedule schedule)
    {
        if (schedule.ActualEndTime.HasValue)
        {
            // Sudah selesai (ada ActualEndTime)
            return "Completed";
        }
        else if (schedule.ActualStartTime.HasValue || schedule.ActualEnterDockTime.HasValue)
        {
            // Sedang berjalan (ada ActualStartTime ATAU ActualEnterDockTime, tapi belum selesai)
            // Enter Dock juga dianggap In Progress (barang sedang loading)
            return "In Progress";
        }
        else
        {
            // Gunakan status dari database (DriverStatus atau Status)
            return (schedule.DriverStatus ?? schedule.Status) ?? "Scheduled";
        }
    }

    /// <summary>
    /// Helper method untuk sorting schedule berdasarkan prioritas
    /// </summary>
    private List<DeliverySchedule> SortSchedulesByPriority(List<DeliverySchedule> schedules, DateTime now)
    {
        return schedules
            .Select(s => new
            {
                Schedule = s,
                DisplayStatus = GetDisplayStatus(s), // Gunakan helper method untuk konsistensi
                IsDelayPickup = !s.ActualStartTime.HasValue && 
                               s.PickupTime.HasValue && 
                               now > s.PickupTime.Value,
                // Lintas hari jika tanggal operasional > ScheduledDate (contoh: EnterDock 21:00, Pickup 03:04)
                IsCrossDay = GetOperationalDateLocal(s).Date > s.ScheduledDate.Date,
                // Prioritas: Completed harus di bawah
                IsCompleted = GetDisplayStatus(s) == "Completed",
                SortPriority = 
                    // Delay Pickup = 1 (highest priority)
                    (!s.ActualStartTime.HasValue && 
                     s.PickupTime.HasValue && 
                     now > s.PickupTime.Value) ? 1 :
                    // In Progress = 2
                    GetDisplayStatus(s) == "In Progress" ? 2 :
                    // Scheduled/Others = 3
                    3
            })
            // PERTAMA: Pisahkan Completed ke bawah
            .OrderBy(x => x.IsCompleted ? 1 : 0)
            // KEDUA: Cross-day schedules (lintas hari) muncul duluan (dalam grup yang sama)
            .ThenByDescending(x => x.IsCrossDay)
            // KETIGA: Lalu berdasarkan prioritas status (delay, in-progress, scheduled)
            .ThenBy(x => x.SortPriority)
            // KEEMPAT: Di dalam masing-masing grup, urutkan berdasarkan waktu PickupTime
            .ThenBy(x => x.Schedule.PickupTime 
                           ?? x.Schedule.EnterDockTime 
                           ?? x.Schedule.ETD 
                           ?? DateTime.MaxValue)
            .Select(x => x.Schedule)
            .ToList();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    /// <summary>
    /// API endpoint untuk mendapatkan statistik dashboard terbaru
    /// Dipanggil via AJAX dari client ketika ada update via SignalR
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetDashboardStatistics()
    {
        var today = DateTime.Today;
        var now = DateTime.Now;
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        
        // Always get fresh data from database (no caching)
        var allSchedulesRaw = await _context.DeliverySchedules
            .AsNoTracking()
            .Include(d => d.Customer)
            .Where(s => s.ScheduledDate.Date == yesterday || 
                       s.ScheduledDate.Date == today || 
                       s.ScheduledDate.Date == tomorrow)
            .ToListAsync();

        // Filter berdasarkan TANGGAL PICKUP = hari ini
        var todayPickupSchedules = allSchedulesRaw
            .Where(s => (s.PickupTime.HasValue && s.PickupTime.Value.Date == today) || 
                        (!s.PickupTime.HasValue && s.ScheduledDate.Date == today))
            .ToList();
        
        var activeSchedules = todayPickupSchedules
            .Where(s => s.Status != "Cancelled")
            .ToList();
        
        // HITUNG STATISTIK BERDASARKAN GRUP (MANIFEST)
        var groupedSchedules = activeSchedules
            .GroupBy(s => new { s.CustomerId, s.Area, s.Cycle, Date = s.ScheduledDate.Date })
            .Select(g => new { 
                Schedules = g.ToList(),
                First = g.First(),
                DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                               (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled")
            })
            .ToList();

        // Count Completed - Satu grup dianggap Completed jika SEMUA schedule di dalamnya selesai
        var completedCount = groupedSchedules.Count(g => g.DisplayStatus == "Completed");
        
        // Count In Progress - Satu grup dianggap In Progress jika ada yang mulai tapi belum semua selesai
        var inProgressCount = groupedSchedules.Count(g => g.DisplayStatus == "In Progress");
        
        // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
        var shouldBeDeliveredCount = todayPickupSchedules
            .GroupBy(s => new { s.CustomerId, s.Area, s.Cycle, Date = s.ScheduledDate.Date })
            .Count(g => (g.First().ETD <= now) == true);
        
        var delayPickupCount = groupedSchedules.Count(g => 
            g.First.ActualStartTime == null && 
            g.First.PickupTime != null && 
            now > g.First.PickupTime.Value &&
            g.DisplayStatus != "Completed");
        
        var delayPrepareCount = groupedSchedules.Count(g => 
            g.First.ActualEnterDockTime == null && 
            g.First.EnterDockTime != null && 
            now > g.First.EnterDockTime.Value &&
            g.DisplayStatus != "Completed");

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
            notArrivedCount = delayPrepareCount,
            preparedCount
        });
    }

    /// <summary>
    /// API endpoint untuk mendapatkan data tabel schedule terbaru
    /// Return partial HTML untuk update tabel
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetScheduleTableData()
    {
        var today = DateTime.Today;
        var now = DateTime.Now;
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        
        // Always get fresh data from database (no caching)
        var allSchedulesRaw = await _context.DeliverySchedules
            .AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.DeliveryItems)
                .ThenInclude(di => di.Item)
            .Where(s => s.ScheduledDate.Date == yesterday || 
                       s.ScheduledDate.Date == today || 
                       s.ScheduledDate.Date == tomorrow)
            .ToListAsync();
        
        // Filter berdasarkan TANGGAL PICKUP
        var todayPickupSchedules = allSchedulesRaw
            .Where(s => (s.PickupTime.HasValue && s.PickupTime.Value.Date == today) || 
                        (!s.PickupTime.HasValue && s.ScheduledDate.Date == today))
            .ToList();
        
        var tomorrowPickupSchedules = allSchedulesRaw
            .Where(s => (s.PickupTime.HasValue && s.PickupTime.Value.Date == tomorrow) || 
                        (!s.PickupTime.HasValue && s.ScheduledDate.Date == tomorrow))
            .ToList();
        
        var activeSchedules = todayPickupSchedules
            .Where(s => s.Status != "Cancelled")
            .ToList();
        
        // Gabungkan schedule pickup hari ini + besok
        var dashboardSchedules = new List<DeliverySchedule>();
        
        // Schedule PICKUP HARI INI
        var todaySchedulesSorted = SortSchedulesByPriority(activeSchedules, now);
        dashboardSchedules.AddRange(todaySchedulesSorted);
        
        // Schedule PICKUP BESOK
        var tomorrowActiveSchedules = tomorrowPickupSchedules
            .Where(s => s.Status != "Cancelled")
            .ToList();
        var tomorrowSchedulesSorted = SortSchedulesByPriority(tomorrowActiveSchedules, now);
        dashboardSchedules.AddRange(tomorrowSchedulesSorted);
        
        ViewBag.TodayScheduleCount = todaySchedulesSorted.Count;
        ViewBag.TomorrowScheduleCount = tomorrowSchedulesSorted.Count;
        ViewBag.DisplayDate = today;

        return PartialView("_ScheduleTablePartial", dashboardSchedules);
    }

    /// <summary>
    /// Menentukan "tanggal operasional" sebuah schedule.
    /// Jika jam Enter Dock lebih besar dari Pickup/ETD (lintas tengah malam),
    /// maka dianggap bergeser ke hari berikutnya.
    /// Contoh: EnterDock 21:00, Pickup 03:04, ETD 03:30 -> operational date = ScheduledDate + 1 hari.
    /// </summary>
    private DateTime GetOperationalDateLocal(DeliverySchedule schedule)
    {
        var baseDate = schedule.ScheduledDate.Date;

        if (schedule.EnterDockTime.HasValue)
        {
            var enter = schedule.EnterDockTime.Value.TimeOfDay;

            bool crossByPickup = schedule.PickupTime.HasValue &&
                                 schedule.PickupTime.Value.TimeOfDay < enter;

            bool crossByEtd = schedule.ETD.HasValue &&
                              schedule.ETD.Value.TimeOfDay < enter;

            if (crossByPickup || crossByEtd)
            {
                return baseDate.AddDays(1);
            }
        }

        return baseDate;
    }

    // ============================================
    // SEED TEST DATA (TEMPORARY FOR TESTING)
    // ============================================
    public async Task<IActionResult> SeedTestData()
    {
        var now = DateTime.Now;
        var today = DateTime.Today;

        // 1. Find or Create Customer TMMIN
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerName.Contains("TMMIN"));
        if (customer == null)
        {
            customer = new Customer
            {
                CustomerCode = "TMMIN",
                CustomerName = "TMMIN",
                CreatedDate = now,
                IsActive = true
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
        }

        // 2. Find or Create Item TA0840
        var item = await _context.Items.FirstOrDefaultAsync(i => i.VIN == "TA0840");
        if (item == null)
        {
            item = new Item
            {
                ItemCode = "TA0840",
                ItemName = "Part TA0840",
                Customer = customer.CustomerName, // Mapping to CUST string
                VIN = "TA0840",
                QtyLot = 25, // QPC
                CreatedDate = now
            };
            _context.Items.Add(item);
            await _context.SaveChangesAsync();
        }

        // 3. Create Schedule for Today
        var scheduleNumber = $"TEST-TMMIN-{now:yyyyMMdd}-{new Random().Next(100, 999)}";
        var enterDockTime = now.AddHours(1); // 1 hour from now
        var pickupTime = now.AddHours(3);

        var schedule = new DeliverySchedule
        {
            ScheduleNumber = scheduleNumber,
            CustomerId = customer.CustomerId,
            ScheduledDate = today,
            EnterDockTime = enterDockTime,
            PickupTime = pickupTime,
            Route = "ROUT-TMMIN",
            Status = "Scheduled",
            TotalTargetQuantity = 4, // 4 items
            TotalActualQuantity = 0,
            CreatedDate = now,
            CreatedBy = "SeedTestData"
        };
        _context.DeliverySchedules.Add(schedule);
        await _context.SaveChangesAsync();

        // 4. Add Items to Schedule
        var deliveryItem = new DeliveryItem
        {
            ScheduleId = schedule.ScheduleId,
            ItemId = item.ItemId,
            Quantity = 4,
            ActualQuantity = 0,
            IsCompleted = false,
            CreatedDate = now
        };
        _context.DeliveryItems.Add(deliveryItem);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"✅ Test Data Created: Schedule {scheduleNumber} for Customer {customer.CustomerName} with Item {item.VIN}.";
        return RedirectToAction("Index");
    }
}

