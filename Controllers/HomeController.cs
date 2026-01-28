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

        // Tentukan tanggal yang akan di-auto-generate berdasarkan jam cutoff yang bisa diatur
        // - Sebelum jam cutoff  -> generate untuk "hari ini"
        // - Setelah jam cutoff  -> generate untuk "besok" (H+1)
        var autoScheduleDate = GetAutoScheduleDateLocal(today, now);

        // Jalankan auto-generate jadwal untuk tanggal tersebut (sekali per hari, hanya hari kerja)
        await AutoGenerateSchedulesForTodayIfNeeded(autoScheduleDate);
        
        _logger.LogWarning("=== DASHBOARD INDEX START ===");
        _logger.LogWarning($"Today: {today:yyyy-MM-dd}");
        _logger.LogWarning($"Now: {now:yyyy-MM-dd HH:mm:ss}");
        _logger.LogWarning($"Auto Schedule Date: {autoScheduleDate:yyyy-MM-dd}");
        
        // Ambil kandidat schedule dari kemarin, hari ini, DAN BESOK
        // Karena ada schedule yang dibuat kemarin (EnterDock H-1) tapi Pickup-nya hari ini
        var allSchedulesRaw = await _context.DeliverySchedules
            .Include(d => d.Customer)
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
        
        // Count Completed - berdasarkan actual times (prioritas lebih tinggi dari database status)
        var completedCount = activeSchedules.Count(s => GetDisplayStatus(s) == "Completed");
        
        // Count In Progress - berdasarkan actual times (prioritas lebih tinggi dari database status)
        var inProgressCount = activeSchedules.Count(s => GetDisplayStatus(s) == "In Progress");
        
        // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
        // Menggunakan semua schedule pickup hari ini (termasuk yang Cancelled) agar sesuai total per jam
        var shouldBeDeliveredCount = todayPickupSchedules.Count(s => s.ETD.HasValue && s.ETD.Value <= now);
        
        // Count Delay Pickup - KRITERIA BARU:
        // 1. Belum ada ActualStartTime (belum tiba)
        // 2. Ada PickupTime
        // 3. Waktu sekarang sudah lewat dari PickupTime
        var delayPickupSchedules = activeSchedules
            .Where(s => s.ActualStartTime == null && 
                       s.PickupTime != null && 
                       now > s.PickupTime.Value)
            .ToList();
        
        var delayPickupCount = delayPickupSchedules.Count;
        
        // Count Delay Prepare (belum masuk dock sampai lewat dari jadwal Enter Dock)
        // KRITERIA:
        // 1. Belum ada ActualEnterDockTime (belum masuk dock)
        // 2. Ada EnterDockTime (ada jadwal masuk dock)
        // 3. Waktu sekarang sudah lewat dari EnterDockTime
        // 4. Belum completed (masih aktif) - gunakan GetDisplayStatus untuk konsistensi
        var delayPrepareSchedules = activeSchedules
            .Where(s => s.ActualEnterDockTime == null && 
                       s.EnterDockTime != null && 
                       now > s.EnterDockTime.Value &&
                       GetDisplayStatus(s) != "Completed")
            .ToList();
        
        var delayPrepareCount = delayPrepareSchedules.Count;
        
        // Log untuk debugging
        _logger.LogWarning("=== DASHBOARD STATISTICS ===");
        _logger.LogWarning($"Current Time: {now:yyyy-MM-dd HH:mm:ss}");
        _logger.LogWarning($"Total Active Schedules: {activeSchedules.Count}");
        _logger.LogWarning($"Completed: {completedCount}");
        _logger.LogWarning($"In Progress: {inProgressCount}");
        _logger.LogWarning($"DELAY PICKUP: {delayPickupCount}");
        _logger.LogWarning($"DELAY PREPARE: {delayPrepareCount}");
        
        // Log detail delay prepare schedules
        if (delayPrepareSchedules.Any())
        {
            _logger.LogWarning("=== DELAY PREPARE DETAILS ===");
            foreach (var s in delayPrepareSchedules)
            {
                if (s.EnterDockTime.HasValue)
                {
                    _logger.LogWarning($"  - {s.ScheduleNumber}: EnterDock={s.EnterDockTime.Value:HH:mm}, Delay={(now - s.EnterDockTime.Value).TotalMinutes:F0} minutes, Status={s.DriverStatus ?? s.Status}");
                }
            }
        }
        
        // Log detail delay pickup schedules
        if (delayPickupSchedules.Any())
        {
            _logger.LogWarning("=== DELAY PICKUP DETAILS ===");
            foreach (var s in delayPickupSchedules)
            {
                if (s.PickupTime.HasValue)
                {
                    _logger.LogWarning($"  - {s.ScheduleNumber}: Pickup={s.PickupTime.Value:HH:mm}, Delay={(now - s.PickupTime.Value).TotalMinutes:F0} minutes");
                }
            }
        }
        
        // Set ViewBag
        ViewBag.TodaySchedules = activeSchedules.Count;
        ViewBag.CompletedCount = completedCount;
        ViewBag.InProgressCount = inProgressCount;
        ViewBag.DelayPickupCount = delayPickupCount;
        ViewBag.NotArrivedCount = delayPrepareCount; // Delay Prepare count
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
        else if (schedule.ActualStartTime.HasValue)
        {
            // Sedang berjalan (ada ActualStartTime tapi belum ada ActualEndTime)
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
        
        // Count Completed - berdasarkan actual times (prioritas lebih tinggi dari database status)
        var completedCount = activeSchedules.Count(s => GetDisplayStatus(s) == "Completed");
        
        // Count In Progress - berdasarkan actual times (prioritas lebih tinggi dari database status)
        var inProgressCount = activeSchedules.Count(s => GetDisplayStatus(s) == "In Progress");
        
        // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
        // Menggunakan semua schedule pickup hari ini (termasuk yang Cancelled) agar konsisten dengan tampilan total per jam
        var shouldBeDeliveredCount = todayPickupSchedules.Count(s => s.ETD.HasValue && s.ETD.Value <= now);
        
        var delayPickupCount = activeSchedules
            .Count(s => s.ActualStartTime == null && 
                       s.PickupTime != null && 
                       now > s.PickupTime.Value);
        
        // Count Delay Prepare (belum masuk dock sampai lewat dari jadwal Enter Dock)
        // KRITERIA:
        // 1. Belum ada ActualEnterDockTime (belum masuk dock)
        // 2. Ada EnterDockTime (ada jadwal masuk dock)
        // 3. Waktu sekarang sudah lewat dari EnterDockTime
        // 4. Belum completed (masih aktif) - gunakan GetDisplayStatus untuk konsistensi
        var delayPrepareCount = activeSchedules
            .Count(s => s.ActualEnterDockTime == null && 
                       s.EnterDockTime != null && 
                       now > s.EnterDockTime.Value &&
                       GetDisplayStatus(s) != "Completed");
        
        return Json(new
        {
            todaySchedules = activeSchedules.Count,
            completedCount,
            shouldBeDeliveredCount,
            inProgressCount,
            delayPickupCount,
            notArrivedCount = delayPrepareCount // Delay Prepare count
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

    // ============================================
    // AUTO GENERATE JADWAL BERDASARKAN TANGGAL HARI INI
    // (dipanggil setiap kali user membuka Dashboard)
    // ============================================

    private async Task AutoGenerateSchedulesForTodayIfNeeded(DateTime targetDate)
    {
        // Skip Sabtu & Minggu
        if (targetDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return;

        // Jika sudah pernah auto-generate untuk hari ini, jangan ulangi (cek via SystemSettings agar permanen meskipun data dihapus)
        var settingKey = $"AutoScheduleDone_{targetDate:yyyyMMdd}";
        var alreadyAutoGenerated = await _context.SystemSettings
            .AsNoTracking()
            .AnyAsync(s => s.Key == settingKey && s.Value == "True");

        if (alreadyAutoGenerated)
            return;

        // Ambil semua customer aktif
        var allCustomers = await _context.Customers
            .Where(c => c.IsActive)
            .ToListAsync();

        var customersToSchedule = allCustomers
            .Where(c => ShouldScheduleCustomerOnDateLocal(c, targetDate))
            .ToList();

        if (!customersToSchedule.Any())
            return;

        // Hindari duplikasi jika sudah ada schedule manual di hari ini
        var existingSchedules = await _context.DeliverySchedules
            .Where(s => s.ScheduledDate.Date == targetDate.Date)
            .ToListAsync();

        var existingCustomerIds = existingSchedules
            .Select(s => s.CustomerId)
            .ToHashSet();

        var schedules = new List<DeliverySchedule>();

        // Prefix dan sequence number sama dengan yang lain
        var prefix = $"SCH-{targetDate:yyyyMMdd}";
        var lastSchedule = existingSchedules
            .Where(s => s.ScheduleNumber.StartsWith(prefix))
            .OrderByDescending(s => s.ScheduleNumber)
            .FirstOrDefault();

        int sequenceNumber = 1;
        if (lastSchedule != null)
        {
            var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
            if (int.TryParse(lastSequence, out int lastNum))
            {
                sequenceNumber = lastNum + 1;
            }
        }

        foreach (var customer in customersToSchedule)
        {
            if (existingCustomerIds.Contains(customer.CustomerId))
                continue;

            var scheduleNumber = $"{prefix}{sequenceNumber:D3}";
            sequenceNumber++;

            // Parse waktu (Pickup = hari H)
            var pickupTime = ParseTimeToDateTimeLocal(customer.Pickup, targetDate);
            var enterDockTime = ParseTimeToDateTimeLocal(customer.Docking, targetDate);
            var etdTime = ParseTimeToDateTimeLocal(customer.ETD, targetDate);

            if (enterDockTime.HasValue && pickupTime.HasValue && enterDockTime.Value.TimeOfDay > pickupTime.Value.TimeOfDay)
            {
                enterDockTime = enterDockTime.Value.AddDays(-1);
            }

            if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value.TimeOfDay < pickupTime.Value.TimeOfDay)
            {
                etdTime = etdTime.Value.AddDays(1);
            }

            var schedule = new DeliverySchedule
            {
                ScheduleNumber = scheduleNumber,
                CustomerId = customer.CustomerId,
                ScheduledDate = targetDate,
                Route = customer.Route,
                Cycle = customer.Cycle,
                EnterDockTime = enterDockTime,
                PickupTime = pickupTime,
                ETD = etdTime,
                Range = customer.Range,
                SKID = ParseSKIDLocal(customer.SKID),
                Area = customer.Area,
                Status = "Scheduled",
                CreatedDate = DateTime.Now,
                CreatedBy = "AutoScheduler"
            };

            schedules.Add(schedule);
        }

        if (!schedules.Any())
            return;

        _context.DeliverySchedules.AddRange(schedules);
        
        // Simpan status auto-generate agar tidak diulang jika user menghapus data
        settingKey = $"AutoScheduleDone_{targetDate:yyyyMMdd}";
        if (!await _context.SystemSettings.AnyAsync(s => s.Key == settingKey))
        {
            _context.SystemSettings.Add(new SystemSetting 
            { 
                Key = settingKey, 
                Value = "True", 
                Description = $"Auto-generation status for {targetDate:yyyy-MM-dd}" 
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("AutoScheduler: berhasil membuat {Count} schedule untuk {Date}",
            schedules.Count, targetDate.ToString("yyyy-MM-dd"));

        // Tampilkan notifikasi di UI (menggunakan alert global di _Layout)
        var msg = $"✅ AutoScheduler membuat {schedules.Count} schedule untuk tanggal {targetDate:dd/MM/yyyy}.";
        if (TempData["SuccessMessage"] is string existing && !string.IsNullOrWhiteSpace(existing))
        {
            TempData["SuccessMessage"] = existing + " " + msg;
        }
        else
        {
            TempData["SuccessMessage"] = msg;
        }
    }

    private bool ShouldScheduleCustomerOnDateLocal(Customer customer, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(customer.Cycle))
            return true;

        var cycle = customer.Cycle.Trim();

        var hariId = date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "Minggu",
            DayOfWeek.Monday => "Senin",
            DayOfWeek.Tuesday => "Selasa",
            DayOfWeek.Wednesday => "Rabu",
            DayOfWeek.Thursday => "Kamis",
            DayOfWeek.Friday => "Jumat",
            DayOfWeek.Saturday => "Sabtu",
            _ => ""
        };

        var hariEn = date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "SUN",
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            _ => ""
        };

        if (cycle.Contains(hariId, StringComparison.OrdinalIgnoreCase) ||
            cycle.Contains(hariEn, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Jika cycle tidak valid sebagai info hari, anggap berlaku setiap hari kerja
        if (!IsValidDayCycleLocal(cycle))
            return true;

        return false;
    }

    private bool IsValidDayCycleLocal(string cycle)
    {
        var daysId = new[] { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };
        var daysEn = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        return daysId.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase)) ||
               daysEn.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase));
    }

    private DateTime? ParseTimeToDateTimeLocal(string? timeString, DateTime baseDate)
    {
        if (string.IsNullOrWhiteSpace(timeString))
            return null;

        if (TimeSpan.TryParse(timeString, out var time))
        {
            return baseDate.Date.Add(time);
        }

        return null;
    }

    private string? ParseSKIDLocal(string? skidString)
    {
        if (string.IsNullOrWhiteSpace(skidString))
            return null;

        return skidString.Trim();
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

    /// <summary>
    /// Menentukan tanggal yang akan di-auto-generate berdasarkan jam cutoff yang bisa diubah lewat konfigurasi / UI.
    /// Urutan prioritas cutoff:
    /// 1) SystemSetting dengan Key = "AutoSchedulerCutoffTime" (database, diatur via menu Settings)
    /// 2) appsettings.json key AutoScheduler:CutoffTime
    /// 3) Default: 18:00
    /// </summary>
    private DateTime GetAutoScheduleDateLocal(DateTime today, DateTime now)
    {
        // Default cutoff jam 18:00
        var defaultCutoff = new TimeSpan(18, 0, 0);
        TimeSpan cutoffTime = defaultCutoff;

        try
        {
            // 1) Cek di database (SystemSettings)
            var dbCutoff = _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefault(s => s.Key == "AutoSchedulerCutoffTime");

            if (dbCutoff != null && TimeSpan.TryParse(dbCutoff.Value, out var dbParsed))
            {
                cutoffTime = dbParsed;
            }
            else
            {
                // 2) Cek di konfigurasi appsettings (jika ada)
                var configured = _configuration["AutoScheduler:CutoffTime"];
                if (!string.IsNullOrWhiteSpace(configured) && TimeSpan.TryParse(configured, out var parsed))
                {
                    cutoffTime = parsed;
                }
            }
        }
        catch
        {
            // Jika ada error baca setting, fallback ke default
            cutoffTime = defaultCutoff;
        }

        return now.TimeOfDay >= cutoffTime
            ? today.AddDays(1)
            : today;
    }
}
