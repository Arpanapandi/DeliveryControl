using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Hubs;
using DeliveryControl.Services;
using System.Globalization;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller khusus untuk Driver - konfirmasi kedatangan dan keberangkatan
    /// </summary>
    [Authorize]
    public class DriverController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly ActivityLogService _logService;

        public DriverController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, ActivityLogService logService)
        {
            _context = context;
            _hubContext = hubContext;
            _logService = logService;
        }

        // GET: Driver - Daftar schedule untuk driver (berbasis PICKUP DATE seperti Dashboard)
        public async Task<IActionResult> Index(DateTime? selectedDate, int? customerId, string? status, string? route, string? cycle)
        {
            var scheduleDate = selectedDate ?? DateTime.Today;
            var yesterday = scheduleDate.AddDays(-1);
            var tomorrow = scheduleDate.AddDays(1);
            var now = DateTime.Now;
            
            // Normalize filter - trim dan pastikan tidak null
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
            var normalizedRoute = string.IsNullOrWhiteSpace(route) ? string.Empty : route!.Trim();
            var normalizedCycle = string.IsNullOrWhiteSpace(cycle) ? string.Empty : cycle!.Trim();
            
            ViewData["SelectedDate"] = scheduleDate.ToString("yyyy-MM-dd");
            ViewData["SelectedCustomerId"] = customerId;
            ViewData["SelectedStatus"] = normalizedStatus;
            ViewData["SelectedRoute"] = normalizedRoute;
            ViewData["SelectedCycle"] = normalizedCycle;
            ViewData["DayName"] = scheduleDate.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));

            // Ambil kandidat schedule dari tiga hari (kemarin, hari ini, besok) untuk cover cross-day schedules
            var rawSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= yesterday.Date && s.ScheduledDate.Date <= tomorrow.Date)
                .ToListAsync();

            // Filter berdasarkan PICKUP DATE (bukan ScheduledDate atau OperationalDate)
            var schedulesForSelectedDate = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Where(s => s.Status != "Cancelled")
                .ToList();

            // Filter by customer jika ada
            if (customerId.HasValue && customerId.Value > 0)
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => s.CustomerId == customerId.Value)
                    .ToList();
            }

            // Filter by status jika ada
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => (s.DriverStatus ?? s.Status) == normalizedStatus)
                    .ToList();
            }

            // Filter by route jika ada
            if (!string.IsNullOrWhiteSpace(normalizedRoute))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => string.Equals(s.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Filter by cycle jika ada
            if (!string.IsNullOrWhiteSpace(normalizedCycle))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => string.Equals(s.Cycle, normalizedCycle, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Data customer untuk dropdown: SEMUA CUSTOMER AKTIF
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();
            
            ViewBag.Customers = customers;

            // Data route & cycle yang tersedia untuk tanggal terpilih (berdasarkan schedules yang sudah ter-filter by date)
            var availableRoutes = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Select(s => s.Route)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            var availableCycles = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Select(s => s.Cycle)
                .Where(cy => !string.IsNullOrWhiteSpace(cy))
                .Distinct()
                .OrderBy(cy => cy)
                .ToList();

            ViewBag.Routes = availableRoutes;
            ViewBag.Cycles = availableCycles;

            // Sorting utama (tetap satu list lengkap untuk kebutuhan lain)
            var schedules = schedulesForSelectedDate
                .OrderBy(s =>
                    s.PickupTime
                    ?? s.EnterDockTime
                    ?? s.ETD
                    ?? s.ScheduledDate)
                .ToList();

            // Bagi menjadi dua kelompok untuk kebutuhan UI:
            // 1. BUTUH AKSI DRIVER (belum arrival atau belum departure)
            // 2. SUDAH SELESAI / TIDAK PERLU AKSI (sudah memiliki ActualEndTime)
            var needAction = schedules
                .Where(s => !s.ActualEndTime.HasValue) // belum selesai
                // Urutkan berdasarkan status display: Scheduled dulu, lalu In Progress, lalu status lain
                .OrderBy(s =>
                {
                    var displayStatus = s.DriverStatus ?? s.Status;
                    return displayStatus switch
                    {
                        "Scheduled" => 0,
                        "In Progress" => 1,
                        _ => 2
                    };
                })
                // Di dalam masing-masing grup status, urutkan berdasarkan waktu
                .ThenBy(s =>
                    s.PickupTime
                    ?? s.EnterDockTime
                    ?? s.ETD
                    ?? s.ScheduledDate)
                .ToList();

            var completed = schedules
                .Where(s => s.ActualEndTime.HasValue)
                .OrderBy(s => s.ActualEndTime)
                .ToList();

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;

            // Model utama tetap dikirim sebagai list lengkap (jika suatu saat dibutuhkan)
            return View(schedules);
        }

        // GET: Driver/Arrival/5 - Halaman konfirmasi kedatangan
        public async Task<IActionResult> Arrival(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Cek apakah sudah ada arrival time
            if (schedule.ActualStartTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi kedatangan pada {schedule.ActualStartTime.Value:dd/MM/yyyy HH:mm}";
            }

            return View(schedule);
        }

        // POST: Driver/Arrival/5 - Konfirmasi kedatangan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Arrival(int id, DateTime arrivalTime, string? notes)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            try
            {
                schedule.ActualStartTime = arrivalTime;
                schedule.DriverStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;
                schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                
                // Tambahkan notes jika ada
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                        ? $"[Arrival] {notes}" 
                        : schedule.Notes + $"\n[Arrival] {notes}";
                }

                await _context.SaveChangesAsync();

                // Log activity
                await _logService.LogConfirm(
                    "Driver",
                    schedule.ScheduleNumber ?? "UNKNOWN",
                    schedule.ScheduleId,
                    $"Konfirmasi kedatangan untuk {schedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "arrival",
                    Message = $"Driver telah tiba di {schedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    Timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Kedatangan berhasil dikonfirmasi pada {arrivalTime:HH:mm}!";
                return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(Arrival), new { id });
            }
        }

        // GET: Driver/Departure/5 - Halaman konfirmasi keberangkatan
        public async Task<IActionResult> Departure(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Validasi: Harus sudah ada arrival time
            if (!schedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "⚠️ Konfirmasi kedatangan terlebih dahulu sebelum konfirmasi keberangkatan!";
                return RedirectToAction(nameof(Arrival), new { id });
            }

            // Cek apakah sudah ada departure time
            if (schedule.ActualEndTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi keberangkatan pada {schedule.ActualEndTime.Value:dd/MM/yyyy HH:mm}";
            }

            return View(schedule);
        }

        // POST: Driver/Departure/5 - Konfirmasi keberangkatan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Departure(int id, DateTime departureTime, string? notes)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Validasi: Harus sudah ada arrival time
            if (!schedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "⚠️ Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Arrival), new { id });
            }

            // Validasi: Departure time harus lebih besar dari arrival time
            if (departureTime < schedule.ActualStartTime.Value)
            {
                TempData["ErrorMessage"] = "⚠️ Waktu keberangkatan tidak boleh lebih awal dari waktu kedatangan!";
                return RedirectToAction(nameof(Departure), new { id });
            }

            try
            {
                schedule.ActualEndTime = departureTime;
                schedule.DriverStatus = "Completed";
                schedule.UpdatedDate = DateTime.Now;
                schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                
                // Tambahkan notes jika ada
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                        ? $"[Departure] {notes}" 
                        : schedule.Notes + $"\n[Departure] {notes}";
                }

                await _context.SaveChangesAsync();

                // Hitung durasi
                var duration = departureTime - schedule.ActualStartTime.Value;
                var durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";

                // Log activity
                await _logService.LogConfirm(
                    "Driver",
                    schedule.ScheduleNumber ?? "UNKNOWN",
                    schedule.ScheduleId,
                    $"Konfirmasi keberangkatan dari {schedule.Customer?.CustomerName} pada {departureTime:HH:mm}. Durasi: {durationText}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "departure",
                    Message = $"Delivery ke {schedule.Customer?.CustomerName} selesai pada {departureTime:HH:mm}. Durasi: {durationText}",
                    Timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Keberangkatan berhasil dikonfirmasi pada {departureTime:HH:mm}! Durasi: {durationText}";
                return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(Departure), new { id });
            }
        }

        // GET: Driver/Details/5 - Detail schedule untuk driver
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            return View(schedule);
        }

        // POST: Driver/QuickArrival/5 - Quick action untuk konfirmasi kedatangan (now)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickArrival(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            if (schedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi kedatangan!";
                return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
            }

            try 
            {
                var arrivalTime = DateTime.Now;
                schedule.ActualStartTime = arrivalTime;
                schedule.DriverStatus = "In Progress";
                schedule.UpdatedDate = arrivalTime;
                schedule.UpdatedBy = User.Identity?.Name ?? "Driver";

                await _context.SaveChangesAsync();

                // Load customer data untuk SignalR message
                await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();

                // Log activity
                await _logService.LogConfirm(
                    "Driver",
                    schedule.ScheduleNumber ?? "UNKNOWN",
                    schedule.ScheduleId,
                    $"Quick konfirmasi kedatangan untuk {schedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "arrival",
                    Message = $"Driver telah tiba di {schedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    Timestamp = arrivalTime
                });

                TempData["SuccessMessage"] = $"✅ Kedatangan dikonfirmasi pada {arrivalTime:HH:mm}!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick arrival: " + ex.Message;
            }
            return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
        }

        // POST: Driver/QuickDeparture/5 - Quick action untuk konfirmasi keberangkatan (now)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickDeparture(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            if (!schedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
            }

            if (schedule.ActualEndTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi keberangkatan!";
                return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
            }

            try 
            {
                var departureTime = DateTime.Now;
                schedule.ActualEndTime = departureTime;
                schedule.DriverStatus = "Completed";
                schedule.UpdatedDate = departureTime;
                schedule.UpdatedBy = User.Identity?.Name ?? "Driver";

                await _context.SaveChangesAsync();

                // Load customer data untuk SignalR message
                await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();

                // Hitung durasi
                var duration = departureTime - schedule.ActualStartTime!.Value;
                var durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";

                // Log activity
                await _logService.LogConfirm(
                    "Driver",
                    schedule.ScheduleNumber,
                    schedule.ScheduleId,
                    $"Quick konfirmasi keberangkatan dari {schedule.Customer?.CustomerName} pada {departureTime:HH:mm}. Durasi: {durationText}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "departure",
                    Message = $"Delivery ke {schedule.Customer?.CustomerName} selesai pada {departureTime:HH:mm}. Durasi: {durationText}",
                    Timestamp = departureTime
                });

                TempData["SuccessMessage"] = $"✅ Keberangkatan dikonfirmasi pada {departureTime:HH:mm}!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick departure: " + ex.Message;
            }
            return RedirectToAction(nameof(Index), new { selectedDate = schedule.ScheduledDate });
        }

    }
}

