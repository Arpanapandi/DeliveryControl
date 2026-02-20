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

            // Filter berdasarkan PICKUP DATE
            var schedulesForSelectedDate = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Where(s => s.Status != "Cancelled")
                // Only show if Prepared in preparation workflow or already being handled by driver
                .Where(s => s.PreparationStatus == "Prepared" || s.Status == "Completed" || s.Status == "In Progress")
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

            // Grouping Logic: Schedule Date + Route + Cycle + Area + PickupTime (IGNORE CustomerId)
            var groupedSchedules = schedulesForSelectedDate
                .GroupBy(s => new {
                    Date = s.PickupTime?.Date ?? s.ScheduledDate.Date,
                    Manifest = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(), // Group by Base Manifest (before /)
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Area = (s.Area ?? "").Trim().ToUpper()
                })
                .Select(g => {
                    var first = g.First();
                    var sortedGroup = g.OrderBy(x => x.ScheduleNumber).ToList();
                    
                    // Combine Customer Names
                    var uniqueCustomers = g.Select(x => x.Customer?.CustomerName ?? "-").Distinct().ToList();
                    var customerDisplay = uniqueCustomers.Count > 1 
                        ? string.Join(", ", uniqueCustomers) 
                        : (uniqueCustomers.FirstOrDefault() ?? "-");

                    return new DeliveryControl.Models.ViewModels.DriverTripViewModel
                    {
                        RepresentativeScheduleId = first.ScheduleId,
                        CustomerName = customerDisplay,
                        Cycle = first.Cycle ?? "",
                        Route = first.Route ?? "",
                        Area = first.Area ?? "",
                        PickupTime = first.PickupTime,
                        ETD = first.ETD,
                        ActualStartTime = first.ActualStartTime, // Assumes synchronized updates
                        ActualEndTime = first.ActualEndTime,     // Assumes synchronized updates
                        DriverStatus = (first.DriverStatus ?? first.Status) ?? "Scheduled",
                        OverallStatus = first.Status ?? "Scheduled",
                        Schedules = sortedGroup
                    };
                })
                .OrderBy(vm => vm.PickupTime ?? vm.ETD ?? DateTime.MaxValue)
                .ToList();

            // Split into "Need Action" and "Completed"
            var needAction = groupedSchedules
                .Where(vm => !vm.HasDeparted)
                .OrderByDescending(vm => vm.Schedules.Any(s => s.ActualEnterDockTime.HasValue)) // Priority 1: Enter Dock
                .ThenBy(vm => {
                    if (!vm.HasArrived) return 0; // Scheduled
                    return 1; // In Progress (Arrived but not Departed)
                })
                .ThenBy(vm => vm.PickupTime ?? DateTime.MaxValue)
                .ToList();

            var completed = groupedSchedules
                .Where(vm => vm.HasDeparted)
                .OrderByDescending(vm => vm.ActualEndTime)
                .ToList();

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;

            // Return the View with the ViewModel list (Grouped) instead of raw schedules
            return View(groupedSchedules);
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

        // Helper to find group
        private async Task<List<DeliverySchedule>> GetSchedulesInGroup(DeliverySchedule baseSchedule)
        {
            var date = baseSchedule.PickupTime?.Date ?? baseSchedule.ScheduledDate.Date;
            
            // Normalize for comparison
            string route = (baseSchedule.Route ?? "").Trim().ToUpper();
            string cycle = (baseSchedule.Cycle ?? "").Trim().ToUpper();
            string area = (baseSchedule.Area ?? "").Trim().ToUpper();
            
            // Note: EF Core translation for case-insensitive/trim might differ. 
            // Since we are using SQLite/SQLServer, usually case-insensitive by default or simple comparison.
            // But to be safe and match the GroupBy logic in Index:
            
            // Taking all schedules for the same date (removed CustomerId filter)
            var candidates = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => (s.PickupTime != null ? s.PickupTime.Value.Date : s.ScheduledDate.Date) == date)
                .ToListAsync(); // Client-side evaluation for safely matching string properties
                
            return candidates
                .Where(s => 
                    (s.Route ?? "").Trim().ToUpper() == route &&
                    (s.Cycle ?? "").Trim().ToUpper() == cycle &&
                    (s.Area ?? "").Trim().ToUpper() == area
                )
                .ToList();
        }

        // POST: Driver/Arrival/5 - Konfirmasi kedatangan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Arrival(int id, DateTime arrivalTime, string? notes)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            try
            {
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);
                
                foreach (var schedule in groupSchedules)
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
                }

                await _context.SaveChangesAsync();

                // Log activity (Group log?)
                await _logService.LogConfirm(
                    "Driver",
                    $"Group-{baseSchedule.Cycle}", // Log Cycle/Group ID?
                    baseSchedule.ScheduleId,
                    $"Konfirmasi kedatangan GROUP ({groupSchedules.Count} Manifest) untuk {baseSchedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR (Send for one, or all? Front-end expects one update usually)
                // Sending for representative is enough if FE reloads, but for real-time card update we might need more.
                // However, Index reloads every 60s or on action. RedirectToAction Index will reload page.
                // Realtime update on Dashboard needs to know all changed.
                foreach(var s in groupSchedules) {
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = s.ScheduleNumber,
                        action = "arrival",
                        message = $"Driver tiba di {s.Customer?.CustomerName}",
                        timestamp = DateTime.Now
                    });
                }

                TempData["SuccessMessage"] = $"✅ Kedatangan berhasil dikonfirmasi untuk {groupSchedules.Count} Manifest pada {arrivalTime:HH:mm}!";
                return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
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
                .FirstOrDefaultAsync(s => s.ScheduleId == id);
        
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
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            // Validasi: Harus sudah ada arrival time (Cek baseSchedule saja cukup karena harusnya sinkron)
            if (!baseSchedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "⚠️ Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Arrival), new { id });
            }

            if (departureTime < baseSchedule.ActualStartTime.Value)
            {
                TempData["ErrorMessage"] = "⚠️ Waktu keberangkatan tidak boleh lebih awal dari waktu kedatangan!";
                return RedirectToAction(nameof(Departure), new { id });
            }

            try
            {
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualEndTime = departureTime;
                    schedule.DriverStatus = "Completed";
                    schedule.UpdatedDate = DateTime.Now;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                    
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                            ? $"[Departure] {notes}" 
                            : schedule.Notes + $"\n[Departure] {notes}";
                    }
                }

                await _context.SaveChangesAsync();

                var duration = departureTime - baseSchedule.ActualStartTime.Value;
                var durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";

                await _logService.LogConfirm(
                    "Driver",
                    $"Group-{baseSchedule.Cycle}",
                    baseSchedule.ScheduleId,
                    $"Konfirmasi keberangkatan GROUP ({groupSchedules.Count} Manifest) dari {baseSchedule.Customer?.CustomerName}. Durasi: {durationText}",
                    User.Identity?.Name ?? "Driver"
                );

                foreach(var s in groupSchedules) {
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = s.ScheduleNumber,
                        action = "departure",
                        message = $"Delivery ke {s.Customer?.CustomerName} selesai.",
                        timestamp = DateTime.Now
                    });
                }

                TempData["SuccessMessage"] = $"✅ Keberangkatan berhasil dikonfirmasi untuk {groupSchedules.Count} Manifest! Durasi: {durationText}";
                return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(Departure), new { id });
            }
        }

        // Quick Actions also need to be updated
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickArrival(int id)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            if (baseSchedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi kedatangan!";
                return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
            }

            try 
            {
                var arrivalTime = DateTime.Now;
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualStartTime = arrivalTime;
                    schedule.DriverStatus = "In Progress";
                    schedule.UpdatedDate = arrivalTime;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                }

                await _context.SaveChangesAsync();
                
                // SignalR & Log (Simplified for brevity, same logic as above)
                await _logService.LogConfirm("Driver", $"Group-{baseSchedule.Cycle}", baseSchedule.ScheduleId, $"Quick Arrival Group ({groupSchedules.Count})", User.Identity?.Name ?? "Driver");
                
                foreach(var s in groupSchedules) {
                     await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { scheduleNumber = s.ScheduleNumber, action = "arrival", message = "Driver Tiba", timestamp = arrivalTime });
                }

                TempData["SuccessMessage"] = $"✅ Kedatangan dikonfirmasi untuk {groupSchedules.Count} Manifest!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick arrival: " + ex.Message;
            }
            return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickDeparture(int id)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            if (!baseSchedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
            }

            if (baseSchedule.ActualEndTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi keberangkatan!";
                return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
            }

            try 
            {
                var departureTime = DateTime.Now;
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualEndTime = departureTime;
                    schedule.DriverStatus = "Completed";
                    schedule.UpdatedDate = departureTime;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                }

                await _context.SaveChangesAsync();

                 await _logService.LogConfirm("Driver", $"Group-{baseSchedule.Cycle}", baseSchedule.ScheduleId, $"Quick Departure Group ({groupSchedules.Count})", User.Identity?.Name ?? "Driver");
                 foreach(var s in groupSchedules) {
                      await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { scheduleNumber = s.ScheduleNumber, action = "departure", message = "Driver Berangkat", timestamp = departureTime });
                }

                TempData["SuccessMessage"] = $"✅ Keberangkatan dikonfirmasi untuk {groupSchedules.Count} Manifest!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick departure: " + ex.Message;
            }
            return RedirectToAction(nameof(Index), new { selectedDate = baseSchedule.ScheduledDate });
        }
    }
}

