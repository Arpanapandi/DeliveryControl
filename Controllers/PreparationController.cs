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
    /// Controller khusus untuk Portal Preparation - konfirmasi masuk dock
    /// </summary>
    [Authorize]
    public class PreparationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly ActivityLogService _logService;

        public PreparationController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, ActivityLogService logService)
        {
            _context = context;
            _hubContext = hubContext;
            _logService = logService;
        }

        // GET: Preparation - Daftar schedule hari ini untuk preparation
        public async Task<IActionResult> Index(DateTime? selectedDate, int? customerId, string? status, string? cycle)
        {
            var enterDockDate = selectedDate ?? DateTime.Today;
            var tomorrow = enterDockDate.AddDays(1);
            
            // Normalize status & cycle - trim dan pastikan tidak null
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
            var normalizedCycle = string.IsNullOrWhiteSpace(cycle) ? string.Empty : cycle!.Trim();
            
            ViewData["SelectedDate"] = enterDockDate.ToString("yyyy-MM-dd");
            ViewData["SelectedCustomerId"] = customerId;
            ViewData["SelectedStatus"] = normalizedStatus;
            ViewData["DayName"] = enterDockDate.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));
            ViewData["SelectedCycle"] = normalizedCycle;

            // LOGIKA: Ambil SEMUA schedule aktif untuk hari ini dan besok
            // Agar admin yang baru buat jadwal langsung muncul di Portal Preparation
            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();
            
            // Filter berdasarkan tanggal Enter Dock (jika ada) ATAU ScheduledDate
            var schedulesForToday = allSchedules
                .Where(s => (s.EnterDockTime.HasValue && s.EnterDockTime.Value.Date == enterDockDate.Date) || 
                            (!s.EnterDockTime.HasValue && s.ScheduledDate.Date == enterDockDate.Date))
                .ToList();
            
            // Self-Correct Status for Old Data (In-Memory Fix)
            foreach (var s in schedulesForToday)
            {
                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.Status != "Completed" || s.PreparationStatus != "Prepared")
                    {
                        s.Status = "Completed";
                        s.PreparationStatus = "Prepared";
                    }
                }
            }
            
            // Filter by customer jika ada
            if (customerId.HasValue && customerId.Value > 0)
            {
                schedulesForToday = schedulesForToday
                    .Where(s => s.CustomerId == customerId.Value)
                    .ToList();
            }

            // Filter by status jika ada
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                schedulesForToday = schedulesForToday
                    .Where(s => (s.PreparationStatus ?? s.Status) == normalizedStatus)
                    .ToList();
            }

            // Filter by cycle jika ada
            if (!string.IsNullOrWhiteSpace(normalizedCycle))
            {
                schedulesForToday = schedulesForToday
                    .Where(s => string.Equals(s.Cycle, normalizedCycle, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Data customer untuk dropdown: SEMUA CUSTOMER AKTIF
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();
            
            ViewBag.Customers = customers;

            // Data cycle untuk dropdown filter cycle (berdasarkan tanggal enter dock terpilih)
            var availableCycles = allSchedules
                .Where(s => s.EnterDockTime!.Value.Date == enterDockDate.Date)
                .Select(s => s.Cycle)
                .Where(cy => !string.IsNullOrWhiteSpace(cy))
                .Distinct()
                .OrderBy(cy => cy)
                .ToList();

            ViewBag.Cycles = availableCycles;

            // Kelompokkan menjadi:
            // 1. BUTUH AKSI PREPARATION (BELUM MASUK DOCK)
            // 2. SUDAH MASUK DOCK / TIDAK PERLU AKSI
            var needAction = schedulesForToday
                .Where(s => !s.ActualEnterDockTime.HasValue)
                .OrderBy(s => s.EnterDockTime ?? DateTime.MaxValue)
                .ToList();

            var completed = schedulesForToday
                .Where(s => s.ActualEnterDockTime.HasValue)
                .OrderBy(s => s.ActualEnterDockTime)
                .ToList();

            var schedules = needAction
                .Concat(completed)
                .ToList();
            
            // Statistics untuk tampilan
            ViewBag.TotalSchedules = schedules.Count;
            ViewBag.NotEnteredYet = needAction.Count;
            ViewBag.AlreadyEntered = completed.Count;

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;
            
            return View(schedules);
        }

        // GET: Preparation/EnterDock/5 - Halaman konfirmasi masuk dock
        public async Task<IActionResult> EnterDock(int? id)
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

            // Cek apakah sudah ada enter dock time
            if (schedule.ActualEnterDockTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi masuk dock pada {schedule.ActualEnterDockTime.Value:dd/MM/yyyy HH:mm}";
            }

            return View(schedule);
        }

        // POST: Preparation/EnterDock/5 - Konfirmasi masuk dock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnterDock(int id, DateTime enterDockTime, string? notes)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            try
            {
                schedule.ActualEnterDockTime = enterDockTime;
                
                // Jika Driver belum confirm arrival, otomatis set Arrival = Enter Dock
                if (!schedule.ActualStartTime.HasValue)
                {
                    schedule.ActualStartTime = enterDockTime;
                    schedule.DriverStatus = "In Progress"; 
                }

                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;
                schedule.UpdatedBy = User.Identity?.Name ?? "Preparation";
                
                // Tambahkan notes jika ada
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                        ? $"[Enter Dock] {notes}" 
                        : schedule.Notes + $"\n[Enter Dock] {notes}";
                }

                await _context.SaveChangesAsync();

                // Log activity
                await _logService.LogConfirm(
                    "Preparation",
                    schedule.ScheduleNumber ?? "UNKNOWN",
                    schedule.ScheduleId,
                    $"Konfirmasi masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                    User.Identity?.Name ?? "Preparation"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "enterDock",
                    Message = $"Truk telah masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                    Timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Masuk dock berhasil dikonfirmasi pada {enterDockTime:HH:mm}!";
                // Redirect ke tanggal enter dock (bukan scheduled date)
                var redirectDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate;
                return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(EnterDock), new { id });
            }
        }

        // GET: Preparation/Details/5 - Detail schedule untuk preparation
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

            // LOGIC: Hitung Prepared Qty
            // Ambil tanggal referensi (Enter Dock Time atau Scheduled Date)
            var refDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate.Date;

            // Ambil preparation records pada tanggal tersebut untuk items yang ada di schedule ini
            // Kita ambil semua record hari ini untuk efisiensi query, lalu filter di memori atau query specific tags
            var relevantTags = schedule.DeliveryItems.Select(di => di.Item.ItemCode.ToUpper()).ToList();
            
            var preparationsToday = await _context.PreparationRecords
                .Where(p => p.CreatedDate.Date == refDate)
                .Select(p => p.Tag)
                .ToListAsync();

            // Hitung frequency per Tag (Case Insensitive)
            var prepCounts = preparationsToday
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.PrepCounts = prepCounts;

            return View(schedule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickEnterDock(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Determine redirect date (enter dock date, not scheduled date)
            var redirectDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate;

            if (schedule.ActualEnterDockTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi masuk dock!";
                return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
            }

            var enterDockTime = DateTime.Now;
            schedule.ActualEnterDockTime = enterDockTime;
            
            // Jika Driver belum confirm arrival, otomatis set Arrival = Enter Dock
            if (!schedule.ActualStartTime.HasValue)
            {
                schedule.ActualStartTime = enterDockTime;
                schedule.DriverStatus = "In Progress";
            }

            schedule.PreparationStatus = "In Progress";
            schedule.UpdatedDate = enterDockTime;
            schedule.UpdatedBy = User.Identity?.Name ?? "Preparation";

            await _context.SaveChangesAsync();

            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();

            // Log activity
            await _logService.LogConfirm(
                "Preparation",
                schedule.ScheduleNumber,
                schedule.ScheduleId,
                $"Quick konfirmasi masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                User.Identity?.Name ?? "Preparation"
            );

            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
            {
                ScheduleNumber = schedule.ScheduleNumber,
                Action = "enterDock",
                Message = $"Truk telah masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                Timestamp = enterDockTime
            });

            TempData["SuccessMessage"] = $"✅ Masuk dock dikonfirmasi pada {enterDockTime:HH:mm}!";
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
        }

        // POST: Preparation/ConfirmPickup/5 - Quick action untuk konfirmasi keberangkatan truk (Pickup)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPickup(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            if (schedule.ActualPickupTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi Pickup!";
                return RedirectToAction(nameof(Index));
            }

            var pickupTime = DateTime.Now;
            schedule.ActualPickupTime = pickupTime;
            schedule.Status = "In Progress"; // Jika belum In Progress
            schedule.UpdatedDate = pickupTime;
            schedule.UpdatedBy = User.Identity?.Name ?? "Preparation";

            await _context.SaveChangesAsync();

            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();

            // Log activity
            await _logService.LogConfirm(
                "Preparation",
                schedule.ScheduleNumber,
                schedule.ScheduleId,
                $"Konfirmasi Pickup (Truk Berangkat) untuk {schedule.Customer?.CustomerName} pada {pickupTime:HH:mm}",
                User.Identity?.Name ?? "Preparation"
            );

            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("DeliveryUpdated", new
            {
                ScheduleNumber = schedule.ScheduleNumber,
                Action = "pickup",
                Message = $"Truk telah berangkat (Pickup) untuk {schedule.Customer?.CustomerName} pada {pickupTime:HH:mm}",
                Timestamp = pickupTime
            });

            TempData["SuccessMessage"] = $"✅ Pickup berhasil dikonfirmasi pada {pickupTime:HH:mm}!";
            return RedirectToActionResultOrCurrent(schedule);
        }

        private IActionResult RedirectToActionResultOrCurrent(DeliverySchedule schedule)
        {
            // Jika request datang dari referer dashboard, balik ke dashboard
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && (referer.Contains("/Home") || referer.Contains("/DeliverySchedules")))
            {
                return Redirect(referer);
            }
            
            var redirectDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate;
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
        }
    }
}

