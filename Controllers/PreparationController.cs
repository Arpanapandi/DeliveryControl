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
    [AuthorizeRoles("Admin", "Preparation", "Leader", "User")]
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

            // 1. Dapatkan izin dock user
            var userIdStr = HttpContext.Session.GetString("UserId");
            var sessionRole = HttpContext.Session.GetString("Role");
            var isUserAdmin = sessionRole == "Admin";
            var isLeader = sessionRole == "Leader";
            
            // Cek apakah user punya HasAllDockAccess flag
            bool hasAllDockAccess = false;
            if (int.TryParse(userIdStr, out int userId))
            {
                var user = await _context.Users.FindAsync(userId);
                hasAllDockAccess = user?.HasAllDockAccess ?? false;
            }
            
            List<int> allowedDockIds = new List<int>();
            // Admin, Leader, atau user dengan HasAllDockAccess tidak perlu filter dock - bisa lihat semua
            if (!isUserAdmin && !isLeader && !hasAllDockAccess && int.TryParse(userIdStr, out int userIdForDock))
            {
                allowedDockIds = await _context.UserDockAccesses
                    .Where(uda => uda.UserId == userIdForDock)
                    .Select(uda => uda.DockId)
                    .ToListAsync();
            }

            // 2. Ambil SEMUA schedule aktif
            var query = _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Filter oleh dock jika bukan admin, bukan leader, dan tidak HasAllDockAccess
            if (!isUserAdmin && !isLeader && !hasAllDockAccess)
            {
                if (!allowedDockIds.Any())
                {
                    // Tidak ada dock yang di-assign → tampilkan kosong
                    query = query.Where(s => false);
                }
                else
                {
                    // Ambil semua kode & nama dock yang diizinkan (case-insensitive)
                    var dockData = await _context.Docks
                        .Where(d => allowedDockIds.Contains(d.DockId))
                        .Select(d => new { d.DockCode, d.DockName })
                        .ToListAsync();

                    var allowedCodeSet = dockData
                        .SelectMany(d => new[] { d.DockCode ?? "", d.DockName ?? "" })
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim().ToUpper())
                        .ToHashSet();

                    // Filter: cocokkan schedule ke dock via Area, Docking, CustomerName, atau Customer.Area
                    var allMatching = await query.ToListAsync();
                    var filteredIds = allMatching
                        .Where(s =>
                            allowedCodeSet.Contains((s.Area ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.Docking ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.Area ?? "").Trim().ToUpper()))
                        .Select(s => s.ScheduleId)
                        .ToHashSet();

                    query = query.Where(s => filteredIds.Contains(s.ScheduleId));
                }
            }


            var allSchedules = await query.ToListAsync();
            
            // Filter berdasarkan TANGGAL OPERASIONAL (ScheduledDate)
            var schedulesForToday = allSchedules
                .Where(s => s.ScheduledDate.Date == enterDockDate.Date)
                .ToList();
            
            // Self-Correct Status for Old Data (In-Memory Fix)
            foreach (var s in schedulesForToday)
            {
                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.PreparationStatus != "Prepared")
                    {
                        s.PreparationStatus = "Prepared";
                    }

                    if (s.ActualEnterDockTime.HasValue)
                    {
                        // Only "Completed" if it has ActualEndTime (Delivery Finished)
                        if (s.ActualEndTime.HasValue) 
                        {
                            s.Status = "Completed";
                        }
                        else
                        {
                            s.Status = "In Progress";
                        }
                    }
                    else
                    {
                        s.Status = "In Progress";
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

            // Data cycle untuk dropdown filter cycle
            var availableCycles = allSchedules
                .Where(s => s.ScheduledDate.Date == enterDockDate.Date)
                .Select(s => s.Cycle)
                .Where(cy => !string.IsNullOrWhiteSpace(cy))
                .Distinct()
                .OrderBy(cy => cy)
                .ToList();
            ViewBag.Cycles = availableCycles;

            // =====================================================================
            // GROUPING LOGIC - Kartu digroup berdasarkan:
            // Manifest Base (sebelum '/') + Customer + Cycle + Route + Effective Dock
            // KRITIS: s.Area bisa kosong di beberapa record, maka fallback ke Customer.Docking
            // =====================================================================
            var groupedSchedules = schedulesForToday
                .GroupBy(s => new {
                    // Grouping per TRIP, bukan per manifest.
                    // Semua manifest yang pergi ke rute/dock/cycle yang sama akan masuk 1 kartu.
                     Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                     Route = (s.Route ?? "").Trim().ToUpper(),
                     // Effective Dock: gunakan Area jika ada, fallback ke Customer.Docking
                     Area = (string.IsNullOrWhiteSpace(s.Area)
                                 ? (s.Customer?.Docking ?? "")
                                 : s.Area).Trim().ToUpper()
                 })
                .Select(g => {
                    var first       = g.First();
                    var sortedGroup = g.OrderBy(x => x.ScheduleNumber).ToList();

                    var uniqueCustomers = g.Select(x => x.Customer?.CustomerName ?? "-").Distinct().ToList();
                    var customerDisplay = uniqueCustomers.Count > 1
                        ? string.Join(", ", uniqueCustomers)
                        : (uniqueCustomers.FirstOrDefault() ?? "-");

                    // Status Logic for the Group
                    var isAllPrepared     = g.All(x => x.PreparationStatus == "Prepared");
                    var hasAnyEnterDock   = g.Any(x => x.ActualEnterDockTime.HasValue);
                    var hasAnyScanProgress = g.Any(x => x.PreparationStatus == "In Progress" || x.PreparationStatus == "Prepared");

                    var groupStatus = "Scheduled";
                    if (hasAnyEnterDock)        groupStatus = "Completed";  // Hijau
                    else if (isAllPrepared)      groupStatus = "Prepared";   // Biru
                    else if (hasAnyScanProgress) groupStatus = "In Progress"; // Oren

                    return new DeliveryControl.Models.ViewModels.DriverTripViewModel
                    {
                        RepresentativeScheduleId = first.ScheduleId,
                        CustomerName   = customerDisplay,
                        Cycle          = first.Cycle ?? "",
                        Route          = first.Route ?? "",
                        Area           = first.Area ?? "",
                        PickupTime     = first.PickupTime,
                        ETD            = first.ETD,
                        ActualStartTime = first.ActualStartTime,
                        ActualEndTime  = first.ActualEndTime,
                        DriverStatus   = first.DriverStatus ?? "Scheduled",
                        OverallStatus  = groupStatus,
                        Schedules      = sortedGroup
                    };
                })
                .OrderBy(vm => vm.OverallStatus == "In Progress" ? 0 : (vm.OverallStatus == "Prepared" ? 1 : (vm.OverallStatus == "Scheduled" ? 2 : 3)))
                .ThenBy(vm => vm.PickupTime ?? vm.ETD ?? DateTime.MaxValue)
                .ToList();


            // Kelompokkan menjadi:
            // 1. BUTUH AKSI PREPARATION (BELUM MASUK DOCK/PREPARED)
            // 2. SUDAH MASUK DOCK / PREPARED
            
            // Using logic from previous code: needAction are those NOT completed (not all entered dock)
            // But wait, the previous logic was: !s.ActualEnterDockTime.HasValue
            
            var needAction = groupedSchedules
                .Where(vm => vm.OverallStatus != "Completed")
                .ToList();

            // Role Leader: hanya tampilkan card yang status "Prepared" (butuh verifikasi leader)
            // Card "Scheduled" dan "In Progress" tidak relevan untuk Leader
            if (sessionRole == "Leader")
            {
                needAction = needAction
                    .Where(vm => vm.OverallStatus == "Prepared")
                    .ToList();
            }
            
            var completed = groupedSchedules
                .Where(vm => vm.OverallStatus == "Completed")
                .ToList();
            
            var finalModel = needAction.Concat(completed).ToList();

            // Statistics untuk tampilan (Count of Groups)
            ViewBag.TotalSchedules = finalModel.Count;
            ViewBag.NotEnteredYet = needAction.Count;
            ViewBag.AlreadyEntered = completed.Count;

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;
            
            return View(finalModel);
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
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
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
                
                // Saat Enter Dock, selalu set PreparationStatus = Prepared
                // agar card muncul di Driver Portal (filter: ActualEnterDockTime.HasValue)
                schedule.PreparationStatus = "Prepared";
                
                // Set Status = In Progress agar card aktif di Driver Portal
                if (schedule.Status != "Completed")
                {
                    schedule.Status = "In Progress";
                }
                
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
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
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
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> GroupReadyToDock(int id)
        {
            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null) return NotFound();

            // Find all schedules in the same group (Manifest base prefix + Date + Cycle + Route + Area)
            // But for simplicity, we treat the RepresentativeScheduleId as the anchor.
            // Requirement says "Ready to Dock in" button.
            
            var manifestPrefix = (repSchedule.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper();
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area &&
                            (s.ScheduleNumber ?? "").ToUpper().StartsWith(manifestPrefix))
                .ToListAsync();

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                if (s.PreparationStatus != "Prepared")
                {
                    s.PreparationStatus = "Prepared";
                    s.Status = "In Progress"; // MUST be In Progress for Driver Portal visibility
                }
                
                // Only record ReadyToDockTime when kanban is 100% scanned
                var allItemsDone = s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
                if (!s.ReadyToDockTime.HasValue && allItemsDone)
                {
                    s.ReadyToDockTime = now;
                }
                
                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
                
                // Calculate ActPrepareTime (minutes) from first preparation record if available
                var firstPrep = await _context.PreparationRecords
                    .Where(pr => pr.ScheduleId == s.ScheduleId)
                    .OrderBy(pr => pr.CreatedDate)
                    .FirstOrDefaultAsync();
                
                if (firstPrep != null)
                {
                    s.ActPrepareTime = (now - firstPrep.CreatedDate).TotalMinutes;
                }
            }

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "readyToDock", manifest = manifestPrefix });

            return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> QuickEnterDock(int id)
        {
            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null) return NotFound();

            var manifestPrefix = (repSchedule.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper();
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area &&
                            (s.ScheduleNumber ?? "").ToUpper().StartsWith(manifestPrefix))
                .ToListAsync();

            // Guard: semua schedule dalam group harus sudah Leader Verified
            var notVerified = schedules.Where(s => !s.IsLeaderVerified).ToList();
            if (notVerified.Any())
            {
                TempData["ErrorMessage"] = $"❌ {notVerified.Count} manifest belum diverifikasi leader. Lakukan Leader Verification terlebih dahulu.";
                return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
            }

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                // Always set timestamps even if "Completed" (scan done)
                s.ActualEnterDockTime = now;
                s.Status = "In Progress"; // Corrected: Must be In Progress to show PICKUP button on dashboard
                s.PreparationStatus = "Prepared";
                

                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
            }

            await _context.SaveChangesAsync();

            // Log activity for the group
            await _logService.LogConfirm(
                "Preparation",
                repSchedule.ScheduleNumber ?? "GROUP",
                repSchedule.ScheduleId,
                $"Group konfirmasi masuk dock untuk {repSchedule.Customer?.CustomerName} pada {now:HH:mm}",
                User.Identity?.Name ?? "Preparation"
            );

            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { 
                action = "enterDock", 
                scheduleNumber = repSchedule.ScheduleNumber,
                timestamp = now 
            });

            return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
        }

        // POST: Preparation/ConfirmPickup/5 - Quick action untuk konfirmasi keberangkatan truk (Pickup)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
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
            
            // Sync with ActualStartTime for dashboard/driver portal consistency
            if (!schedule.ActualStartTime.HasValue)
            {
                schedule.ActualStartTime = pickupTime;
            }

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
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "pickup",
                message = $"Truk telah berangkat (Pickup) untuk {schedule.Customer?.CustomerName} pada {pickupTime:HH:mm}",
                timestamp = pickupTime
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

        // ─────────────────────────────────────────────────────────────────────
        // LEADER VERIFICATION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET: Ambil data verifikasi untuk modal leader verification
        /// Mengembalikan list manifest + kanban + status scan preparation untuk satu group card
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetVerificationData(int id)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            if (sessionRole != "Admin" && sessionRole != "Leader")
                return Json(new { success = false, message = "Akses ditolak. Hanya Admin atau Leader." });

            var repSchedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (repSchedule == null)
                return Json(new { success = false, message = "Schedule tidak ditemukan." });

            // Effective area untuk grouping (sama seperti di Index)
            var effectiveArea = !string.IsNullOrWhiteSpace(repSchedule.Area)
                ? repSchedule.Area.Trim().ToUpper()
                : (repSchedule.Customer?.Docking ?? "").Trim().ToUpper();

            // Cari semua schedule dalam TRIP yang sama (Cycle + Route + Area + Tanggal)
            // Ini memastikan semua manifest dalam 1 card ditampilkan
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date
                         && (s.Cycle ?? "").Trim().ToUpper() == (repSchedule.Cycle ?? "").Trim().ToUpper()
                         && (s.Route ?? "").Trim().ToUpper() == (repSchedule.Route ?? "").Trim().ToUpper()
                         && ((!string.IsNullOrWhiteSpace(s.Area) ? s.Area.Trim().ToUpper() : (s.Customer != null ? s.Customer.Docking ?? "" : "").Trim().ToUpper()) == effectiveArea))
                .OrderBy(s => s.ScheduleNumber)
                .ToListAsync();

            // Hitung kanban per manifest + detail parts
            var manifestList = schedules.Select(s =>
            {
                double kanbanTarget = 0;
                var partsList = new List<object>();
                
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        var partKanban = qpc > 0 ? (int)Math.Ceiling((double)di.Quantity / qpc) : 0;
                        kanbanTarget += partKanban;
                        
                        // Gunakan CustomerPartNumber jika ada, jika tidak pakai ItemCode
                        var displayPartNo = !string.IsNullOrWhiteSpace(di.Item?.CustomerPartNumber) 
                            ? di.Item.CustomerPartNumber 
                            : di.Item?.ItemCode ?? "-";
                        
                        partsList.Add(new
                        {
                            itemId = di.ItemId,
                            partNumber = displayPartNo,
                            partName = di.Item?.ItemName ?? "-",
                            quantity = di.Quantity,
                            qtyLot = qpc,
                            kanbanCount = partKanban,
                            unit = di.Unit ?? "PCS"
                        });
                    }
                }
                var kanbanTarget_i = (int)Math.Round(kanbanTarget);
                var kanbanScanned  = s.LeaderVerifiedKanbanCount;
                var isVerified     = s.IsLeaderVerified || kanbanScanned >= kanbanTarget_i && kanbanTarget_i > 0;
                var mnKey          = ParseManifestKey(s.ScheduleNumber ?? "");
                return new
                {
                    scheduleId     = s.ScheduleId,
                    scheduleNumber = s.ScheduleNumber ?? "",
                    manifestKey    = mnKey,
                    kanbanTarget   = kanbanTarget_i,
                    kanbanScanned,
                    isPrepared     = s.PreparationStatus == "Prepared",
                    isVerified,
                    parts          = partsList
                };
            }).ToList();

            // Progress: total kanban di semua manifest, berapa yang sudah di-scan Leader
            var totalKanban    = manifestList.Sum(m => m.kanbanTarget);
            var scannedKanban  = manifestList.Sum(m => Math.Min(m.kanbanScanned, m.kanbanTarget));
            var totalManifests = manifestList.Count;
            var verifiedManifests = manifestList.Count(m => m.isVerified);

            return Json(new
            {
                success          = true,
                representativeId = id,
                dockName         = repSchedule.Area ?? repSchedule.Customer?.CustomerName ?? "—",
                customerName     = repSchedule.Customer?.CustomerName ?? "—",
                cycle            = repSchedule.Cycle ?? "",
                totalKanban,
                scannedKanban,
                totalManifests,
                verifiedManifests,
                isComplete       = verifiedManifests >= totalManifests && totalManifests > 0,
                manifests        = manifestList
            });
        }

        /// <summary>
        /// POST: Proses scan manifest saat Leader Verification
        /// scanInput format: "1231454334/2234" → key = "1231454334" (sebelum /)
        /// Setiap scan menambah LeaderVerifiedKanbanCount. Manifest dianggap VERIFIED
        /// ketika LeaderVerifiedKanbanCount >= kanbanTarget manifest tersebut.
        /// </summary>
        [HttpPost]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> ScanManifestVerification(int representativeId, string scanInput)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            var sessionUser = HttpContext.Session.GetString("Username") ?? "Leader";

            if (sessionRole != "Admin" && sessionRole != "Leader")
                return Json(new { success = false, message = "Akses ditolak. Hanya Admin atau Leader." });

            if (string.IsNullOrWhiteSpace(scanInput))
                return Json(new { success = false, message = "Input scan kosong." });

            var repSchedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == representativeId);
            if (repSchedule == null)
                return Json(new { success = false, message = "Schedule tidak ditemukan." });

            // Bersihkan input scan (Part Number dari kanban)
            var scannedPartNo = scanInput.Trim().ToUpper();

            // Effective area untuk grouping (sama seperti di Index)
            var effectiveArea = !string.IsNullOrWhiteSpace(repSchedule.Area)
                ? repSchedule.Area.Trim().ToUpper()
                : (repSchedule.Customer?.Docking ?? "").Trim().ToUpper();

            // Cari semua schedule dalam TRIP yang sama (Cycle + Route + Area + Tanggal)
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date
                         && (s.Cycle ?? "").Trim().ToUpper() == (repSchedule.Cycle ?? "").Trim().ToUpper()
                         && (s.Route ?? "").Trim().ToUpper() == (repSchedule.Route ?? "").Trim().ToUpper()
                         && ((!string.IsNullOrWhiteSpace(s.Area) ? s.Area.Trim().ToUpper() : (s.Customer != null ? s.Customer.Docking ?? "" : "").Trim().ToUpper()) == effectiveArea))
                .ToListAsync();

            // Cari schedule yang mengandung Part Number yang di-scan
            // Match berdasarkan: ItemCode, CustomerPartNumber - dengan partial match
            DeliverySchedule? matched = null;
            string matchedPartName = "";
            
            foreach (var s in schedules)
            {
                if (s.DeliveryItems != null)
                {
                    var matchedItem = s.DeliveryItems.FirstOrDefault(di => 
                        di.Item != null && (
                            // Exact match ItemCode
                            (di.Item.ItemCode ?? "").ToUpper() == scannedPartNo ||
                            // Partial match: ItemCode starts with scanned OR scanned starts with ItemCode
                            (di.Item.ItemCode ?? "").ToUpper().StartsWith(scannedPartNo) ||
                            scannedPartNo.StartsWith((di.Item.ItemCode ?? "").ToUpper()) ||
                            // Match CustomerPartNumber
                            (di.Item.CustomerPartNumber ?? "").ToUpper() == scannedPartNo ||
                            (di.Item.CustomerPartNumber ?? "").ToUpper().StartsWith(scannedPartNo) ||
                            scannedPartNo.StartsWith((di.Item.CustomerPartNumber ?? "").ToUpper())
                        ));
                    
                    if (matchedItem != null)
                    {
                        matched = s;
                        matchedPartName = matchedItem.Item?.ItemName ?? matchedItem.Item?.ItemCode ?? scannedPartNo;
                        break;
                    }
                }
            }

            if (matched == null)
                return Json(new { success = false, message = $"Part \"{scannedPartNo}\" tidak ditemukan di manifest ini." });

            // Validasi: preparation harus sudah Prepared
            if (matched.PreparationStatus != "Prepared")
                return Json(new { success = false, message = $"Manifest \"{matched.ScheduleNumber}\" belum selesai preparation (status: {matched.PreparationStatus ?? "Scheduled"})." });

            // Hitung kanbanTarget untuk manifest ini
            double kanbanTargetDouble = 0;
            if (matched.DeliveryItems != null)
            {
                foreach (var di in matched.DeliveryItems)
                {
                    var qpc = di.Item?.QtyLot ?? 1;
                    if (qpc > 0) kanbanTargetDouble += Math.Ceiling((double)di.Quantity / qpc);
                }
            }
            var kanbanTarget = (int)Math.Round(kanbanTargetDouble);

            // Cek apakah sudah fully verified (semua kanban terpenuhi)
            var manifestKey = ParseManifestKey(matched.ScheduleNumber ?? "");
            if (matched.IsLeaderVerified || (kanbanTarget > 0 && matched.LeaderVerifiedKanbanCount >= kanbanTarget))
                return Json(new { success = false, isDuplicate = true, message = $"Manifest \"{manifestKey}\" sudah sepenuhnya diverifikasi ({kanbanTarget}/{kanbanTarget} kanban)." });

            // Tambah count
            matched.LeaderVerifiedKanbanCount += 1;
            matched.UpdatedDate = DateTime.Now;
            matched.UpdatedBy   = sessionUser;

            var newCount   = matched.LeaderVerifiedKanbanCount;
            var remaining  = kanbanTarget - newCount;
            var isManifestComplete = kanbanTarget > 0
                ? newCount >= kanbanTarget
                : true; // jika tidak ada kanban target, anggap langsung verified

            // Jika kanban manifest ini terpenuhi → tandai IsLeaderVerified
            if (isManifestComplete)
            {
                matched.IsLeaderVerified = true;
                matched.LeaderVerifiedAt = DateTime.Now;
                matched.LeaderVerifiedBy = sessionUser;
            }

            await _context.SaveChangesAsync();

            // Hitung total kanban progress di seluruh group
            // (ulang hitung karena data sudah ter-update in-memory)
            int totalKanban   = 0;
            int scannedKanban = 0;
            int totalManifests    = schedules.Count;
            int verifiedManifests = 0;

            foreach (var s in schedules)
            {
                double kt = 0;
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        if (qpc > 0) kt += Math.Ceiling((double)di.Quantity / qpc);
                    }
                }
                var ktInt = (int)Math.Round(kt);
                totalKanban   += ktInt;
                scannedKanban += Math.Min(s.LeaderVerifiedKanbanCount, ktInt);
                if (s.IsLeaderVerified || (ktInt > 0 && s.LeaderVerifiedKanbanCount >= ktInt))
                    verifiedManifests++;
            }

            var isGroupComplete = verifiedManifests >= totalManifests && totalManifests > 0;

            if (isGroupComplete)
            {
                // Pastikan semua ter-flag IsLeaderVerified dan set Enter Dock time
                var enterDockTime = DateTime.Now;
                foreach (var s in schedules)
                {
                    if (!s.IsLeaderVerified)
                    {
                        s.IsLeaderVerified = true;
                        s.LeaderVerifiedAt = DateTime.Now;
                        s.LeaderVerifiedBy = sessionUser;
                    }
                    
                    // Otomatis set Enter Dock saat verifikasi selesai
                    if (s.ActualEnterDockTime == null)
                    {
                        s.ActualEnterDockTime = enterDockTime;
                    }
                    
                    // Update status ke Loading
                    if (s.Status == "Prepared" || s.Status == "Ready")
                    {
                        s.Status = "Loading";
                    }
                    
                    s.UpdatedDate = DateTime.Now;
                    s.UpdatedBy = sessionUser;
                }
                await _context.SaveChangesAsync();

                await _logService.LogConfirm(
                    "LeaderVerification",
                    repSchedule.ScheduleNumber ?? "GROUP",
                    repSchedule.ScheduleId,
                    $"Leader Verification selesai - semua {totalManifests} manifest ({totalKanban} kanban) terverifikasi oleh {sessionUser}. Otomatis Dock In.",
                    sessionUser
                );

                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action         = "leaderVerificationComplete",
                    scheduleNumber = repSchedule.ScheduleNumber,
                    message        = $"Leader Verification selesai untuk {repSchedule.Area}. Status: Loading (Dock In otomatis).",
                    timestamp      = DateTime.Now
                });
            }
            else
            {
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action         = "manifestVerified",
                    scheduleNumber = repSchedule.ScheduleNumber,
                    manifestKey    = manifestKey,
                    scanned        = scannedKanban,
                    total          = totalKanban,
                    timestamp      = DateTime.Now
                });
            }

            // Kembalikan data progress terbaru
            var updatedManifests = schedules.Select(s =>
            {
                double kt = 0;
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        if (qpc > 0) kt += Math.Ceiling((double)di.Quantity / qpc);
                    }
                }
                var ktInt  = (int)Math.Round(kt);
                var isVer  = s.IsLeaderVerified || (ktInt > 0 && s.LeaderVerifiedKanbanCount >= ktInt);
                return new
                {
                    scheduleId    = s.ScheduleId,
                    manifestKey   = ParseManifestKey(s.ScheduleNumber ?? ""),
                    scheduleNumber= s.ScheduleNumber ?? "",
                    kanbanTarget  = ktInt,
                    kanbanScanned = s.LeaderVerifiedKanbanCount,
                    isVerified    = isVer
                };
            }).ToList();

            // Pesan feedback berdasarkan kondisi
            string feedbackMessage;
            if (isGroupComplete)
                feedbackMessage = $"✅ Semua {totalManifests} manifest ({totalKanban} kanban) terverifikasi!";
            else if (isManifestComplete)
                feedbackMessage = $"✅ Manifest \"{manifestKey}\" selesai ({kanbanTarget}/{kanbanTarget} kanban).";
            else
                feedbackMessage = $"✅ Kanban {newCount}/{kanbanTarget} — Part \"{matchedPartName}\" ({remaining} lagi).";

            return Json(new
            {
                success        = true,
                message        = feedbackMessage,
                scannedKey     = scannedPartNo,
                kanbanTarget,
                kanbanScanned  = newCount,
                isManifestComplete,
                totalKanban,
                scannedKanban,
                totalManifests,
                verifiedManifests,
                isComplete     = isGroupComplete,
                manifests      = updatedManifests
            });
        }

        /// <summary>
        /// Helper: Parse key manifest dari format "PREFIX/SUFFIX" → ambil PREFIX (sebelum '/')
        /// </summary>
        private static string ParseManifestKey(string scheduleNumber)
        {
            if (string.IsNullOrWhiteSpace(scheduleNumber)) return string.Empty;
            var idx = scheduleNumber.IndexOf('/');
            return idx >= 0
                ? scheduleNumber.Substring(0, idx).Trim()
                : scheduleNumber.Trim();
        }
    }
}

