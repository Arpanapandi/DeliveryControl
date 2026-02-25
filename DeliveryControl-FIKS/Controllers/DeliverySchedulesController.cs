using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels;
using DeliveryControl.Hubs;
using System.Globalization;

namespace DeliveryControl.Controllers
{
    public class DeliverySchedulesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public DeliverySchedulesController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // GET: DeliverySchedules/GanttChart - Visualisasi Gantt Chart
        public async Task<IActionResult> GanttChart(DateTime? startDate, DateTime? endDate, int? customerId)
        {
            var scheduleDate = startDate ?? DateTime.Today;
            var endScheduleDate = endDate ?? DateTime.Today;
            
            ViewData["StartDate"] = scheduleDate.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endScheduleDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            var query = _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= scheduleDate.Date && s.ScheduledDate.Date <= endScheduleDate.Date)
                .AsQueryable();

            if (customerId.HasValue)
            {
                query = query.Where(s => s.CustomerId == customerId.Value);
            }

            var rawSchedules = await query.ToListAsync();

            // Urutkan: yang belum complete di atas, yang sudah Complete selalu di bawah
            var schedules = rawSchedules
                .OrderBy(s => (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue)) ? 0 : 
                             (s.Status == "Completed" || s.ActualEndTime.HasValue) ? 2 : 1)
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.PickupTime ?? s.ETD ?? s.ScheduledDate)
                .ToList();

            return View(schedules);
        }

        // GET: DeliverySchedules/DelayChart - Grafik batang total delay per jam + tren harian
        public async Task<IActionResult> DelayChart(DateTime? date, int? customerId)
        {
            var selectedDate = (date ?? DateTime.Today).Date;

            ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            // Ambil data untuk rentang 14 hari terakhir (termasuk selectedDate) untuk grafik harian
            var rangeEnd = selectedDate;
            var rangeStart = selectedDate.AddDays(-13); // 14 hari ke belakang

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= rangeStart && s.ScheduledDate.Date <= rangeEnd);

            if (customerId.HasValue)
            {
                query = query.Where(s => s.CustomerId == customerId.Value);
            }

            var schedules = await query.ToListAsync();

            // ================== DATA PER JAM (HANYA UNTUK selectedDate) ==================

            var schedulesForSelectedDate = schedules
                .Where(s => s.ScheduledDate.Date == selectedDate)
                .ToList();

            // Siapkan bucket 24 jam (00:00 - 23:59) untuk Pickup dan Dock In (Delay)
            var pickupBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartHourData
                {
                    Hour = h,
                    TotalDelayMinutes = 0,
                    Customers = new List<string>()
                })
                .ToList();

            var dockInBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartHourData
                {
                    Hour = h,
                    TotalDelayMinutes = 0,
                    Customers = new List<string>()
                })
                .ToList();

            // Bucket untuk ON TIME (jumlah order, bukan menit)
            var onTimePickupBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartViewModel.OnTimeChartHourData
                {
                    Hour = h,
                    TotalCount = 0,
                    Customers = new List<string>()
                })
                .ToList();

            var onTimeDockBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartViewModel.OnTimeChartHourData
                {
                    Hour = h,
                    TotalCount = 0,
                    Customers = new List<string>()
                })
                .ToList();

            foreach (var s in schedulesForSelectedDate)
            {
                // ====== DELAY PICKUP (ActualStartTime vs PickupTime) ======
                if (s.PickupTime.HasValue && s.ActualStartTime.HasValue)
                {
                    var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                    if (diff.TotalMinutes > 0)
                    {
                        int hourIndex = s.PickupTime.Value.Hour;
                        var bucket = pickupBuckets[hourIndex];

                        var minutes = (int)Math.Round(diff.TotalMinutes);
                        bucket.TotalDelayMinutes += minutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = minutes
                                });
                            }
                            else
                            {
                                existingDetail.DelayMinutes += minutes;
                            }
                        }
                    }
                    else if (diff.TotalMinutes < 0)
                    {
                        // ON TIME / lebih cepat (advance) - simpan selisih menit positif
                        int hourIndex = s.PickupTime.Value.Hour;
                        var bucket = onTimePickupBuckets[hourIndex];

                        var advanceMinutes = (int)Math.Round(Math.Abs(diff.TotalMinutes));
                        bucket.TotalCount += advanceMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = advanceMinutes
                                });
                            }
                            else
                            {
                                existingDetail.DelayMinutes += advanceMinutes;
                            }
                        }
                    }
                }

                // ====== DELAY DOCK IN (ActualEnterDockTime vs EnterDockTime) ======
                if (s.EnterDockTime.HasValue && s.ActualEnterDockTime.HasValue)
                {
                    var dockDiff = s.ActualEnterDockTime.Value - s.EnterDockTime.Value;
                    if (dockDiff.TotalMinutes > 0)
                    {
                        int hourIndexDock = s.EnterDockTime.Value.Hour;
                        var dockBucket = dockInBuckets[hourIndexDock];

                        var dockMinutes = (int)Math.Round(dockDiff.TotalMinutes);
                        dockBucket.TotalDelayMinutes += dockMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!dockBucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                dockBucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDockDetail = dockBucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDockDetail == null)
                            {
                                dockBucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = dockMinutes
                                });
                            }
                            else
                            {
                                existingDockDetail.DelayMinutes += dockMinutes;
                            }
                        }
                    }
                    else if (dockDiff.TotalMinutes < 0)
                    {
                        // ON TIME / lebih cepat Dock In - selisih menit positif sebagai advance
                        int hourIndexDock = s.EnterDockTime.Value.Hour;
                        var bucket = onTimeDockBuckets[hourIndexDock];

                        var advanceMinutes = (int)Math.Round(Math.Abs(dockDiff.TotalMinutes));
                        bucket.TotalCount += advanceMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDockDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDockDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = advanceMinutes
                                });
                            }
                            else
                            {
                                existingDockDetail.DelayMinutes += advanceMinutes;
                            }
                        }
                    }
                }
            }

            // ================== DATA HARIAN DELAY PICKUP (UNTUK TREND) ==================

            var dailyDelayDict = new Dictionary<DateTime, DailyPickupDelayData>();

            foreach (var s in schedules)
            {
                if (!s.PickupTime.HasValue || !s.ActualStartTime.HasValue)
                    continue;

                var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                if (diff.TotalMinutes <= 0)
                    continue; // hanya hitung yang delay (positif)

                var scheduleDate = s.ScheduledDate.Date;
                var minutes = (int)Math.Round(diff.TotalMinutes);
                var customerName = s.Customer?.CustomerName ?? "(Unknown)";

                if (!dailyDelayDict.TryGetValue(scheduleDate, out var dailyData))
                {
                    dailyData = new DailyPickupDelayData
                    {
                        Date = scheduleDate,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    };
                    dailyDelayDict[scheduleDate] = dailyData;
                }

                dailyData.TotalDelayMinutes += minutes;

                var existingCustomer = dailyData.CustomerDetails
                    .FirstOrDefault(c => c.CustomerName == customerName);
                if (existingCustomer == null)
                {
                    dailyData.CustomerDetails.Add(new DelayChartCustomerDetail
                    {
                        CustomerName = customerName,
                        DelayMinutes = minutes
                    });
                }
                else
                {
                    existingCustomer.DelayMinutes += minutes;
                }
            }

            // Pastikan semua hari dalam range muncul di grafik (meski tanpa delay)
            var dailyPickupDelays = new List<DailyPickupDelayData>();
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                if (dailyDelayDict.TryGetValue(d.Date, out var data))
                {
                    // Urutkan detail customer descending by delay
                    data.CustomerDetails = data.CustomerDetails
                        .OrderByDescending(c => c.DelayMinutes)
                        .ToList();
                    dailyPickupDelays.Add(data);
                }
                else
                {
                    dailyPickupDelays.Add(new DailyPickupDelayData
                    {
                        Date = d.Date,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    });
                }
            }

            var viewModel = new DelayChartViewModel
            {
                SelectedDate = selectedDate,
                CustomerId = customerId,
                StartDate = rangeStart,
                EndDate = rangeEnd,
                PickupHourlyData = pickupBuckets,
                DockInHourlyData = dockInBuckets,
                TotalPickupDelayMinutes = pickupBuckets.Sum(x => x.TotalDelayMinutes),
                TotalDockInDelayMinutes = dockInBuckets.Sum(x => x.TotalDelayMinutes),
                OnTimePickupHourlyData = onTimePickupBuckets,
                OnTimeDockInHourlyData = onTimeDockBuckets,
                TotalOnTimePickupCount = onTimePickupBuckets.Sum(x => x.TotalCount),
                TotalOnTimeDockInCount = onTimeDockBuckets.Sum(x => x.TotalCount),
                DailyPickupDelays = dailyPickupDelays
            };

            return View(viewModel);
        }

        // GET: DeliverySchedules/DelayPickupTrend - Grafik harian + tabel schedule delay pickup
        public async Task<IActionResult> DelayPickupTrend(DateTime? date, int? customerId)
        {
            var selectedDate = (date ?? DateTime.Today).Date;

            // Range 1 bulan penuh berdasarkan bulan dari selectedDate
            var rangeStart = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            var rangeEnd = rangeStart.AddMonths(1).AddDays(-1);

            ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= rangeStart && s.ScheduledDate.Date <= rangeEnd);

            if (customerId.HasValue)
            {
                query = query.Where(s => s.CustomerId == customerId.Value);
            }

            var schedules = await query.ToListAsync();

            // ================== DATA HARIAN DELAY PICKUP (UNTUK TREND) ==================
            var dailyDelayDict = new Dictionary<DateTime, DailyPickupDelayData>();
            var delayedSchedules = new List<DeliverySchedule>();

            foreach (var s in schedules)
            {
                if (!s.PickupTime.HasValue || !s.ActualStartTime.HasValue)
                    continue;

                var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                if (diff.TotalMinutes <= 0)
                    continue; // hanya hitung yang delay (positif)

                var minutes = (int)Math.Round(diff.TotalMinutes);
                var scheduleDate = s.ScheduledDate.Date;
                var customerName = s.Customer?.CustomerName ?? "(Unknown)";

                // Masuk list schedule delay
                delayedSchedules.Add(s);

                if (!dailyDelayDict.TryGetValue(scheduleDate, out var dailyData))
                {
                    dailyData = new DailyPickupDelayData
                    {
                        Date = scheduleDate,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    };
                    dailyDelayDict[scheduleDate] = dailyData;
                }

                dailyData.TotalDelayMinutes += minutes;

                var existingCustomer = dailyData.CustomerDetails
                    .FirstOrDefault(c => c.CustomerName == customerName);
                if (existingCustomer == null)
                {
                    dailyData.CustomerDetails.Add(new DelayChartCustomerDetail
                    {
                        CustomerName = customerName,
                        DelayMinutes = minutes
                    });
                }
                else
                {
                    existingCustomer.DelayMinutes += minutes;
                }
            }

            // Pastikan semua hari dalam range muncul di grafik (meski tanpa delay)
            var dailyPickupDelays = new List<DailyPickupDelayData>();
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                if (dailyDelayDict.TryGetValue(d.Date, out var data))
                {
                    data.CustomerDetails = data.CustomerDetails
                        .OrderByDescending(c => c.DelayMinutes)
                        .ToList();
                    dailyPickupDelays.Add(data);
                }
                else
                {
                    dailyPickupDelays.Add(new DailyPickupDelayData
                    {
                        Date = d.Date,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    });
                }
            }

            // Urutkan schedule delay berdasarkan tanggal dan jam pickup
            delayedSchedules = delayedSchedules
                .OrderBy(s => s.ScheduledDate.Date)
                .ThenBy(s => s.PickupTime ?? s.ETD ?? s.ScheduledDate)
                .ToList();

            var vm = new DailyPickupTrendViewModel
            {
                SelectedDate = selectedDate,
                StartDate = rangeStart,
                EndDate = rangeEnd,
                CustomerId = customerId,
                DailyPickupDelays = dailyPickupDelays,
                DelayedSchedules = delayedSchedules
            };

            return View(vm);
        }

        // GET: DeliverySchedules
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, int? customerId, string status, int pageNumber = 1)
        {
            var customerList = await _context.Customers
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToListAsync();

            ViewData["Customers"] = new SelectList(customerList, "CustomerId", "DisplayName");
            ViewData["Statuses"] = new List<string> { "Scheduled", "In Progress", "Completed", "Cancelled", "Delayed" };
            
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Status"] = status;

            var schedules = _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .AsQueryable();

            // Default filter - tampilkan schedule hari ini jika tidak ada filter
            if (!startDate.HasValue && !endDate.HasValue)
            {
                startDate = DateTime.Today;
                endDate = DateTime.Today.AddDays(7);
            }

            if (startDate.HasValue)
            {
                schedules = schedules.Where(s => s.ScheduledDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                schedules = schedules.Where(s => s.ScheduledDate <= endDate.Value);
            }

            if (customerId.HasValue)
            {
                schedules = schedules.Where(s => s.CustomerId == customerId.Value);
            }

            if (!string.IsNullOrEmpty(status))
            {
                schedules = schedules.Where(s => s.Status == status);
            }

            var totalItems = await schedules.CountAsync();
            int pageSize = 20;

            var rawResults = await schedules.ToListAsync();
            var items = rawResults
                .OrderBy(s =>
                {
                    // In Progress (truk di jalan / sedang berlangsung) → paling atas
                    if (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue))
                        return 0;
                    // Completed (selesai delivery) → paling bawah
                    if (s.Status == "Completed" || s.ActualEndTime.HasValue)
                        return 2;
                    // Cancelled → paling bawah sekali
                    if (s.Status == "Cancelled")
                        return 3;
                    // Scheduled / Prepared (menunggu) → tengah
                    return 1;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ETD ?? s.PickupTime ?? s.EnterDockTime ?? DateTime.MaxValue)
                .ThenBy(s => s.ScheduleId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { 
                { "startDate", startDate?.ToString("yyyy-MM-dd") },
                { "endDate", endDate?.ToString("yyyy-MM-dd") },
                { "customerId", customerId?.ToString() },
                { "status", status }
            };

            return View(items);
        }

        // GET: DeliverySchedules/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var today = DateTime.Today;
            
            // Statistics
            ViewBag.TodaySchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.ScheduledDate.Date == today && s.Status != "Cancelled");
            
            ViewBag.InProgressCount = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.Status == "In Progress");
            
            ViewBag.CompletedToday = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.ScheduledDate.Date == today && s.Status == "Completed");
            
            ViewBag.DelayedCount = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.Status == "Delayed");

            // Today's schedules
            var todaySchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Where(s => s.ScheduledDate.Date == today)
                .OrderBy(s => s.ETD)
                .ToListAsync();

            return View(todaySchedules);
        }

        // GET: DeliverySchedules/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Fetch grouped schedules for the same "Trip" (Customer + Area + Date)
            var groupSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.CustomerId == schedule.CustomerId && 
                           s.Area == schedule.Area && 
                           s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                           s.ScheduleId != schedule.ScheduleId)
                .ToListAsync();

            ViewBag.GroupSchedules = groupSchedules;

            return View(schedule);
        }

        // GET: DeliverySchedules/BulkCreate - Bulk scheduling dengan tabel checklist
        public async Task<IActionResult> BulkCreate(DateTime? selectedDate)
        {
            var scheduledDate = selectedDate ?? DateTime.Today;
            
            // Get all active customers
            var customers = await _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode)
                .ToListAsync();
            
            var customerItems = customers.Select(c => new CustomerScheduleItem
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Route = c.Route,
                Cycle = c.Cycle,
                Docking = c.Docking,
                Pickup = c.Pickup,
                ETD = c.ETD,
                Range = c.Range,
                SKID = c.SKID,
                Area = c.Area,
                IsSelected = false,
                // Check apakah customer seharusnya dijadwalkan di hari ini (berdasarkan Cycle)
                IsMatchingDay = ShouldScheduleCustomerOnDate(c, scheduledDate)
            }).ToList();
            
            var viewModel = new BulkScheduleViewModel
            {
                ScheduledDate = scheduledDate,
                AvailableCustomers = customerItems,
                IsAutoGenerate = true
            };
            
            return View(viewModel);
        }
        
        // POST: DeliverySchedules/BulkCreate - Process bulk scheduling
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkCreate(BulkScheduleViewModel model)
        {
            try
            {
                var schedules = new List<DeliverySchedule>();
                List<Customer> customers;

                // MODE OTOMATIS: generate berdasarkan Cycle & hari, hanya untuk hari kerja
                if (model.IsAutoGenerate)
                {
                    if (model.ScheduledDate.DayOfWeek == DayOfWeek.Saturday || model.ScheduledDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        TempData["ErrorMessage"] = "Mode otomatis hanya berlaku untuk hari Senin–Jumat. Untuk Sabtu/Minggu silakan gunakan mode manual.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .AsNoTracking() // Added AsNoTracking
                        .Where(c => c.IsActive)
                        .ToListAsync();

                    customers = customers
                        .Where(c => ShouldScheduleCustomerOnDate(c, model.ScheduledDate))
                        .ToList();

                    if (!customers.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang memenuhi kriteria Cycle untuk tanggal ini.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }
                }
                else
                {
                    if (model.SelectedCustomerIds == null || !model.SelectedCustomerIds.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang dipilih! Silakan checklist minimal 1 customer.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .AsNoTracking() // Added AsNoTracking
                        .Where(c => model.SelectedCustomerIds.Contains(c.CustomerId))
                        .ToListAsync();
                }
                
                // Get starting sequence
                int sequenceNumber = await GetNextSequenceInternal(model.ScheduledDate);
                
                foreach (var customer in customers)
                {
                    var prefix = $"SCH-{model.ScheduledDate:yyyyMMdd}";
                    var scheduleNumber = $"{prefix}{sequenceNumber:D3}";
                    sequenceNumber++;

                    // Parse waktu berdasarkan tanggal jadwal (Pickup dianggap sebagai hari H)
                    // Parse waktu berdasarkan tanggal jadwal (H0)
                    var pickupTime = ParseTimeToDateTime(customer.Pickup, model.ScheduledDate);
                    var enterDockTime = ParseTimeToDateTime(customer.Docking, model.ScheduledDate);
                    var etdTime = ParseTimeToDateTime(customer.ETD, model.ScheduledDate);
                    
                    // Gunakan StartPrepare sebagai anchor untuk menentukan H+1
                    // Jika jam milestone < jam mulai persiapan, maka itu adalah besok harinya
                    var startPrepMinutes = customer.StartPrepareTime;
                    var startPrepTime = model.ScheduledDate.Date.AddMinutes(startPrepMinutes);

                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                    {
                        enterDockTime = enterDockTime.Value.AddDays(1);
                    }

                    if (pickupTime.HasValue && pickupTime.Value < startPrepTime)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }

                    if (etdTime.HasValue && etdTime.Value < startPrepTime)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }

                    // Double Checks: Pastikan urutan logis tetap terjaga (Dock -> Pickup -> ETD)
                    if (pickupTime.HasValue && enterDockTime.HasValue && pickupTime.Value < enterDockTime.Value)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }
                    if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value < pickupTime.Value)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }
                    
                    var schedule = new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = customer.CustomerId,
                        ScheduledDate = model.ScheduledDate,
                        Route = customer.Route,
                        Cycle = customer.Cycle,
                        EnterDockTime = enterDockTime,
                        PickupTime = pickupTime,
                        ETD = etdTime,
                        Range = customer.Range,
                        SKID = ParseSKID(customer.SKID),
                        Area = customer.Area,
                        StartPrepareTime = customer.StartPrepareTime,
                        StdPrepareTime = customer.StdPrepareTime,
                        Status = "Scheduled",
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "System"
                    };
                    
                    schedules.Add(schedule);
                }
                
                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        
                        // Notify Dashboard via SignalR
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                        {
                            action = "bulk_create",
                            message = $"Berhasil membuat {schedules.Count} schedule delivery untuk tanggal {model.ScheduledDate:dd/MM/yyyy}",
                            timestamp = DateTime.Now
                        });

                        TempData["SuccessMessage"] = $"✅ Berhasil membuat {schedules.Count} schedule delivery untuk tanggal {model.ScheduledDate:dd/MM/yyyy}!";
                        return RedirectToAction(nameof(Index), new { startDate = model.ScheduledDate.ToString("yyyy-MM-dd"), endDate = model.ScheduledDate.ToString("yyyy-MM-dd") });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat bulk create schedule: {ex.Message}";
            }
            
            return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
        }
        
        // Helper: Get nama hari dalam bahasa Indonesia
        private string GetIndonesianDayName(DateTime date)
        {
            return date.DayOfWeek switch
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
        }
        
        // Helper: Get kode hari 3 huruf (Inggris) misal MON, TUE, WED
        private string GetEnglishShortDayCode(DateTime date)
        {
            return date.DayOfWeek switch
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
        }

        // Helper: Check apakah cycle berisi informasi hari (Indonesia atau kode 3 huruf Inggris)
        private bool IsValidDayCycle(string? cycle)
        {
            if (string.IsNullOrWhiteSpace(cycle))
                return false;
                
            var daysId = new[] { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };
            var daysEn = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
            return daysId.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase)) ||
                   daysEn.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase));
        }

        // Helper: logika utama apakah customer dijadwalkan pada tanggal tertentu
        private bool ShouldScheduleCustomerOnDate(Customer customer, DateTime date)
        {
            // Customer tanpa cycle dianggap bisa setiap hari kerja
            if (string.IsNullOrWhiteSpace(customer.Cycle))
                return true;

            var cycle = customer.Cycle.Trim();
            var hariId = GetIndonesianDayName(date);
            var hariEn = GetEnglishShortDayCode(date);

            // Jika cycle eksplisit menyebut hari (Indonesia / Inggris 3 huruf)
            if (cycle.Contains(hariId, StringComparison.OrdinalIgnoreCase) ||
                cycle.Contains(hariEn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Jika cycle tidak valid sebagai info hari, anggap berlaku setiap hari kerja
            if (!IsValidDayCycle(cycle))
                return true;

            // Selain itu, tidak dijadwalkan di hari ini
            return false;
        }
        
        // Helper: Parse waktu HH:mm ke DateTime
        private DateTime? ParseTimeToDateTime(string? timeString, DateTime baseDate)
        {
            if (string.IsNullOrWhiteSpace(timeString))
                return null;
                
            if (TimeSpan.TryParse(timeString, out TimeSpan time))
            {
                return baseDate.Date.Add(time);
            }
            
            return null;
        }
        
        // Helper: Get SKID string
        private string? ParseSKID(string? skidString)
        {
            if (string.IsNullOrWhiteSpace(skidString))
                return null;
                
            return skidString.Trim();
        }

        // GET: DeliverySchedules/Create
        public IActionResult Create()
        {
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName");
            
            // Generate Schedule Number
            var scheduleNumber = GenerateScheduleNumber();
            
            var model = new DeliverySchedule
            {
                ScheduleNumber = scheduleNumber,
                ScheduledDate = DateTime.Today,
                Status = "Scheduled"
            };
            
            return View(model);
        }
        
        // Helper method untuk generate schedule number
        private string GenerateScheduleNumber()
        {
            var today = DateTime.Today;
            var prefix = $"SCH-{today:yyyyMMdd}";
            
            // Cari schedule number terakhir hari ini
            var lastSchedule = _context.DeliverySchedules
                .AsNoTracking() // Added AsNoTracking
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefault();
            
            int sequenceNumber = 1;
            if (lastSchedule != null)
            {
                // Extract sequence number dari schedule terakhir
                var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
                if (int.TryParse(lastSequence, out int lastNum))
                {
                    sequenceNumber = lastNum + 1;
                }
            }
            
            return $"{prefix}{sequenceNumber:D3}";
        }
        
        // API untuk get customer details
        [HttpGet]
        public IActionResult GetCustomerDetails(int customerId)
        {
            var customer = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.CustomerId == customerId)
                .Select(c => new {
                    route = c.Route ?? "",
                    cycle = c.Cycle ?? "",
                    skid = c.SKID ?? "",
                    area = c.Area ?? "",
                    startPrepareTime = c.StartPrepareTime,
                    stdPrepareTime = c.StdPrepareTime
                })
                .FirstOrDefault();
            
            if (customer == null)
            {
                return NotFound();
            }
            
            return Json(customer);
        }

        private async Task<int> GetNextSequenceInternal(DateTime date)
        {
            var prefix = $"SCH-{date:yyyyMMdd}";
            var lastSchedule = await _context.DeliverySchedules
                .AsNoTracking() // Added AsNoTracking
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();

            if (lastSchedule == null) return 1;

            var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
            if (int.TryParse(lastSequence, out int lastNum))
            {
                return lastNum + 1;
            }
            return 1;
        }

        // POST: DeliverySchedules/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,ActualEnterDockTime,PickupTime,ETD,Range,SKID,Area,VehicleNumber,DriverName,DriverPhone,Notes,Status,TotalTargetQuantity,TotalActualQuantity")] DeliverySchedule schedule)
        {
            // Remove validation for optional fields
            ModelState.Remove("Route");
            ModelState.Remove("Cycle");
            ModelState.Remove("PickupTime");
            ModelState.Remove("ETD");
            ModelState.Remove("Range");
            ModelState.Remove("SKID");
            ModelState.Remove("Area");
            ModelState.Remove("VehicleNumber");
            ModelState.Remove("DriverName");
            ModelState.Remove("DriverPhone");
            ModelState.Remove("Notes");
            
            // Remove validation for navigation properties (will be loaded from database)
            ModelState.Remove("Customer");
            ModelState.Remove("Dock");
            ModelState.Remove("DeliveryItems");
            
            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Generate Schedule Number if not provided
                    if (string.IsNullOrEmpty(schedule.ScheduleNumber) || schedule.ScheduleNumber == "SCH-001")
                    {
                        var sequence = await GetNextSequenceInternal(schedule.ScheduledDate);
                        schedule.ScheduleNumber = $"SCH-{schedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }

                    // Check for collision and retry once if needed
                    var exists = await _context.DeliverySchedules.AnyAsync(s => s.ScheduleNumber == schedule.ScheduleNumber);
                    if (exists)
                    {
                        var sequence = await GetNextSequenceInternal(schedule.ScheduledDate);
                        schedule.ScheduleNumber = $"SCH-{schedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }

                    schedule.CreatedDate = DateTime.Now;
                    schedule.CreatedBy = User.Identity?.Name ?? "System";
                    _context.Add(schedule);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    
                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "create",
                        message = $"Schedule baru {schedule.ScheduleNumber} telah dibuat",
                        timestamp = DateTime.Now
                    });

                    TempData["SuccessMessage"] = "Schedule berhasil ditambahkan!";
                    return RedirectToAction(nameof(Index), new { startDate = schedule.ScheduledDate.ToString("yyyy-MM-dd"), endDate = schedule.ScheduledDate.ToString("yyyy-MM-dd") });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = $"Gagal menyimpan schedule: {ex.Message}";
                    // Fallthrough to return view
                }
            }
            
            // Log errors for debugging
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (errors.Any())
            {
                TempData["ErrorMessage"] = $"Validasi gagal: {string.Join(", ", errors)}";
            }
            
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // GET: DeliverySchedules/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == id);
            
            if (schedule == null)
            {
                return NotFound();
            }
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // POST: DeliverySchedules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,ActualEnterDockTime,PickupTime,ETD,Range,SKID,Area,ActualStartTime,ActualEndTime,ActualPickupTime,VehicleNumber,DriverName,DriverPhone,Notes,Status,CreatedDate,CreatedBy,TotalTargetQuantity,TotalActualQuantity,StartPrepareTime,StdPrepareTime")] DeliverySchedule schedule)
        {
            if (id != schedule.ScheduleId)
            {
                return NotFound();
            }

            // Remove validation for navigation properties
            ModelState.Remove("Customer");
            ModelState.Remove("Dock");
            ModelState.Remove("DeliveryItems");

            if (ModelState.IsValid)
            {
                try
                {
                    schedule.UpdatedDate = DateTime.Now;
                    schedule.UpdatedBy = User.Identity?.Name ?? "System";
                    _context.Update(schedule);
                    await _context.SaveChangesAsync();
                    
                    // Load customer data untuk SignalR message (jika belum loaded)
                    if (schedule.Customer == null)
                    {
                        await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
                    }
                    
                    TempData["SuccessMessage"] = "Schedule berhasil diupdate!";
                    
                    // Kirim SignalR notification untuk update dashboard real-time
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "update",
                        message = $"Schedule {schedule.ScheduleNumber} ({schedule.Customer?.CustomerName ?? "N/A"}) telah diupdate",
                        timestamp = DateTime.Now
                    });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ScheduleExists(schedule.ScheduleId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // GET: DeliverySchedules/StartDelivery/5
        public async Task<IActionResult> StartDelivery(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null)
            {
                return NotFound();
            }

            schedule.ActualStartTime = DateTime.Now;
            schedule.Status = "In Progress";
            schedule.UpdatedDate = DateTime.Now;
            schedule.UpdatedBy = User.Identity?.Name ?? "System";
            
            await _context.SaveChangesAsync();
            
            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
            
            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "arrival",
                message = $"Delivery ke {schedule.Customer?.CustomerName} dimulai",
                timestamp = DateTime.Now
            });
            
            TempData["SuccessMessage"] = "Delivery dimulai!";
            
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: DeliverySchedules/CompleteDelivery/5
        public async Task<IActionResult> CompleteDelivery(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null)
            {
                return NotFound();
            }

            schedule.ActualEndTime = DateTime.Now;
            schedule.Status = "Completed";
            schedule.UpdatedDate = DateTime.Now;
            schedule.UpdatedBy = User.Identity?.Name ?? "System";
            
            await _context.SaveChangesAsync();
            
            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
            
            // Hitung durasi jika ada ActualStartTime
            string durationText = "";
            if (schedule.ActualStartTime.HasValue)
            {
                var duration = schedule.ActualEndTime.Value - schedule.ActualStartTime.Value;
                durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";
            }
            
            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "departure",
                message = $"Delivery ke {schedule.Customer?.CustomerName} selesai{(string.IsNullOrEmpty(durationText) ? "" : $". Durasi: {durationText}")}",
                timestamp = DateTime.Now
            });
            
            TempData["SuccessMessage"] = "Delivery selesai!";
            
            return RedirectToAction(nameof(Index));
        }

        // GET: DeliverySchedules/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);
            
            if (schedule == null)
            {
                return NotFound();
            }

            return View(schedule);
        }

        // POST: DeliverySchedules/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Include(s => s.PreparationRecords)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule != null)
            {
                try
                {
                    // 1. Hapus delivery items (manual backup jika cascade terhambat)
                    if (schedule.DeliveryItems != null && schedule.DeliveryItems.Any())
                    {
                        _context.DeliveryItems.RemoveRange(schedule.DeliveryItems);
                    }

                    // 2. Hapus preparation records
                    if (schedule.PreparationRecords != null && schedule.PreparationRecords.Any())
                    {
                        _context.PreparationRecords.RemoveRange(schedule.PreparationRecords);
                    }

                    // 3. Hapus schedule utama
                    _context.DeliverySchedules.Remove(schedule);
                    await _context.SaveChangesAsync();

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "delete",
                        message = $"Schedule {schedule.ScheduleNumber} telah dihapus",
                        timestamp = DateTime.Now
                    });

                    TempData["SuccessMessage"] = "Schedule berhasil dihapus!";
                }
                catch (DbUpdateException ex)
                {
                    TempData["ErrorMessage"] = $"Error saat menghapus schedule: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ScheduleExists(int id)
        {
            return _context.DeliverySchedules.Any(e => e.ScheduleId == id);
        }

        // POST: Bulk Delete Schedules
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada schedule yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Parse IDs dengan error handling
                var ids = new List<int>();
                foreach (var idStr in selectedIds.Split(','))
                {
                    if (int.TryParse(idStr.Trim(), out int id))
                    {
                        ids.Add(id);
                    }
                }

                if (!ids.Any())
                {
                    TempData["ErrorMessage"] = "Tidak ada ID schedule yang valid!";
                    return RedirectToAction(nameof(Index));
                }

                // Load schedules dengan dependent items
        var schedulesToDelete = await _context.DeliverySchedules
            .Include(s => s.DeliveryItems)
            .Include(s => s.PreparationRecords)
            .Where(s => ids.Contains(s.ScheduleId))
            .ToListAsync();

        if (!schedulesToDelete.Any())
        {
            TempData["ErrorMessage"] = "Schedule tidak ditemukan!";
            return RedirectToAction(nameof(Index));
        }

        int totalItems = schedulesToDelete.Sum(s => s.DeliveryItems?.Count ?? 0);
        int totalPrepRecords = schedulesToDelete.Sum(s => s.PreparationRecords?.Count ?? 0);
        int deletedSchedules = schedulesToDelete.Count;

        // 1. Hapus delivery items terlebih dahulu
        var allDeliveryItems = schedulesToDelete
            .Where(s => s.DeliveryItems != null && s.DeliveryItems.Any())
            .SelectMany(s => s.DeliveryItems)
            .ToList();

        if (allDeliveryItems.Any())
        {
            _context.DeliveryItems.RemoveRange(allDeliveryItems);
        }

        // 2. Hapus preparation records
        var allPrepRecords = schedulesToDelete
            .Where(s => s.PreparationRecords != null && s.PreparationRecords.Any())
            .SelectMany(s => s.PreparationRecords)
            .ToList();

        if (allPrepRecords.Any())
        {
            _context.PreparationRecords.RemoveRange(allPrepRecords);
        }

        // 3. Hapus schedules
        _context.DeliverySchedules.RemoveRange(schedulesToDelete);
        await _context.SaveChangesAsync();

                // Notify Dashboard via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "bulk_delete",
                    message = $"Berhasil menghapus {deletedSchedules} schedule dan {totalItems} item",
                    timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {deletedSchedules} schedule{(deletedSchedules > 1 ? "" : "")} dan {totalItems} delivery item{(totalItems != 1 ? "s" : "")}!";
            }
            catch (DbUpdateException dbEx)
            {
                TempData["ErrorMessage"] = $"❌ Error database saat menghapus: {dbEx.InnerException?.Message ?? dbEx.Message}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error saat bulk delete: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Schedule Template");

            // Header
            worksheet.Cell(1, 1).Value = "CUST";
            worksheet.Cell(1, 2).Value = "ROUTE";
            worksheet.Cell(1, 3).Value = "CYCLE";
            worksheet.Cell(1, 4).Value = "ENTER DOCK";
            worksheet.Cell(1, 5).Value = "PICKUP";
            worksheet.Cell(1, 6).Value = "ETD";
            worksheet.Cell(1, 7).Value = "Range";
            worksheet.Cell(1, 8).Value = "SKID";
            worksheet.Cell(1, 9).Value = "AREA";
            worksheet.Cell(1, 10).Value = "START PREP";
            worksheet.Cell(1, 11).Value = "END PREP";
            worksheet.Cell(1, 12).Value = "QTY TARGET";

            // Style header
            var headerRange = worksheet.Range(1, 1, 1, 12);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            // Add sample data
            worksheet.Cell(2, 1).Value = "CUST001";
            worksheet.Cell(2, 2).Value = "Route A";
            worksheet.Cell(2, 3).Value = "Cycle 1";
            worksheet.Cell(2, 4).Value = "2025-01-20 07:30";
            worksheet.Cell(2, 5).Value = "2025-01-20 08:00";
            worksheet.Cell(2, 6).Value = "2025-01-20 09:00";
            worksheet.Cell(2, 7).Value = "10-15 KM";
            worksheet.Cell(2, 8).Value = 10;
            worksheet.Cell(2, 9).Value = "Area 1";
            worksheet.Cell(2, 10).Value = 15;
            worksheet.Cell(2, 11).Value = 30;
            worksheet.Cell(2, 12).Value = 100;

            // Auto fit columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DeliverySchedule_Template.xlsx");
        }

        // Import Excel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid!";
                return RedirectToAction(nameof(Create));
            }

            if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            {
                TempData["ErrorMessage"] = "File harus berformat Excel (.xlsx atau .xls)!";
                return RedirectToAction(nameof(Create));
            }

            try
            {
                var schedules = new List<DeliverySchedule>();
                var errors = new List<string>();
                var batchSequences = new Dictionary<string, int>();
                var allCustomers = await _context.Customers.AsNoTracking().ToListAsync();
                var customersByCode = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode))
                    .GroupBy(c => c.CustomerCode.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.First());

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1); // Skip header

                int rowNumber = 1;
                foreach (var row in rows)
                {
                    rowNumber++;
                    try
                    {
                        var custCode = row.Cell(1).GetString().Trim();
                        var route = row.Cell(2).GetString().Trim();
                        var cycle = row.Cell(3).GetString().Trim();
                        var enterDockStr = row.Cell(4).GetString().Trim();
                        var pickupStr = row.Cell(5).GetString().Trim();
                        var etdStr = row.Cell(6).GetString().Trim();
                        var range = row.Cell(7).GetString().Trim();
                        var skidStr = row.Cell(8).GetString().Trim();
                        var area = row.Cell(9).GetString().Trim();
                        var targetQtyStr = row.Cell(12).GetString().Trim();
                        var startPrepStr = row.Cell(10).GetString().Trim();
                        var endPrepStr = row.Cell(11).GetString().Trim();

                        // Validasi customer
                        if (!customersByCode.ContainsKey(custCode))
                        {
                            errors.Add($"Baris {rowNumber}: Customer '{custCode}' tidak ditemukan!");
                            continue;
                        }

                        var customer = customersByCode[custCode];
                        var customerId = customer.CustomerId;

                        // Parse datetime
                        DateTime? enterDockTime = null;
                        DateTime? pickupTime = null;
                        DateTime? etd = null;

                        if (!string.IsNullOrEmpty(enterDockStr) && DateTime.TryParse(enterDockStr, out var enterDock))
                        {
                            enterDockTime = enterDock;
                        }

                        if (!string.IsNullOrEmpty(pickupStr) && DateTime.TryParse(pickupStr, out var pickup))
                        {
                            pickupTime = pickup;
                        }

                        if (!string.IsNullOrEmpty(etdStr) && DateTime.TryParse(etdStr, out var etdParsed))
                        {
                            etd = etdParsed;
                        }

                        // Get SKID
                        string? skid = string.IsNullOrWhiteSpace(skidStr) ? null : skidStr;

                        // Parse Target Qty
                        decimal targetQty = 0;
                        if (!string.IsNullOrEmpty(targetQtyStr) && decimal.TryParse(targetQtyStr, out var targetParsed))
                        {
                            targetQty = targetParsed;
                        }

                        // Generate schedule number
                        var scheduledDate = etd?.Date ?? DateTime.Today;
                        var prefix = $"SCH-{scheduledDate:yyyyMMdd}";
                        
                        // We use a local sequence counter for the batch to avoid duplicate IDs 
                        // before they are committed to the DB
                        if (!batchSequences.ContainsKey(prefix))
                        {
                            batchSequences[prefix] = await GetNextSequenceInternal(scheduledDate);
                        }
                        
                        var sequence = batchSequences[prefix]++;
                        var scheduleNumber = $"{prefix}{sequence:D3}";

                        var schedule = new DeliverySchedule
                        {
                            ScheduleNumber = scheduleNumber,
                            CustomerId = customerId,
                            Route = string.IsNullOrWhiteSpace(route) ? customer.Route : route,
                            Cycle = string.IsNullOrWhiteSpace(cycle) ? customer.Cycle : cycle,
                            Range = string.IsNullOrWhiteSpace(range) ? customer.Range : range,
                            SKID = skid ?? customer.SKID,
                            Area = string.IsNullOrWhiteSpace(area) ? enterDockStr : area,
                            StartPrepareTime = ParsePrepTime(startPrepStr) != 0 ? ParsePrepTime(startPrepStr) : customer.StartPrepareTime,
                            StdPrepareTime = ParsePrepTime(endPrepStr) != 0 ? ParsePrepTime(endPrepStr) : customer.StdPrepareTime,
                            TotalTargetQuantity = targetQty,
                            ScheduledDate = etd?.Date ?? DateTime.Today,
                            Status = "Scheduled",
                            CreatedDate = DateTime.Now,
                            CreatedBy = User.Identity?.Name ?? "System"
                        };

                        var eDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                        var pTime = pickupTime ?? ParseTimeToDateTime(customer.Pickup, scheduledDate);
                        var eTime = etd ?? ParseTimeToDateTime(customer.ETD, scheduledDate);

                        // Gunakan StartPrepare sebagai anchor untuk menentukan H+1
                        var sPrepMinutes = schedule.StartPrepareTime;
                        var sPrepTime = schedule.ScheduledDate.Date.AddMinutes(sPrepMinutes);

                        if (eDockTime.HasValue && eDockTime.Value < sPrepTime)
                            eDockTime = eDockTime.Value.AddDays(1);

                        if (pTime.HasValue && pTime.Value < sPrepTime)
                            pTime = pTime.Value.AddDays(1);

                        if (eTime.HasValue && eTime.Value < sPrepTime)
                            eTime = eTime.Value.AddDays(1);

                        // Double checks for logic consistency
                        if (pTime.HasValue && eDockTime.HasValue && pTime.Value < eDockTime.Value)
                            pTime = pTime.Value.AddDays(1);
                        if (eTime.HasValue && pTime.HasValue && eTime.Value < pTime.Value)
                            eTime = eTime.Value.AddDays(1);

                        schedule.EnterDockTime = eDockTime;
                        schedule.PickupTime = pTime;
                        schedule.ETD = eTime;

                        schedules.Add(schedule);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Baris {rowNumber}: {ex.Message}");
                    }
                }

                if (errors.Any())
                {
                    TempData["ErrorMessage"] = $"Import gagal! Ditemukan {errors.Count} error:\n" + string.Join("\n", errors.Take(10));
                    return RedirectToAction(nameof(Create));
                }

                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        action = "import",
                        message = $"Berhasil import {schedules.Count} schedule dari Excel",
                        timestamp = DateTime.Now
                    });

                        TempData["SuccessMessage"] = $"Berhasil import {schedules.Count} schedule dari Excel!";
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = "Tidak ada data yang valid untuk diimport!";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error saat import Excel: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
        // GET: DeliverySchedules/ExportExcel
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate, int? customerId, string status)
        {
            var schedules = _context.DeliverySchedules
                .Include(d => d.Customer)
                .AsNoTracking()
                .AsQueryable();

            if (startDate.HasValue) schedules = schedules.Where(s => s.ScheduledDate >= startDate.Value);
            if (endDate.HasValue) schedules = schedules.Where(s => s.ScheduledDate <= endDate.Value);
            if (customerId.HasValue) schedules = schedules.Where(s => s.CustomerId == customerId.Value);
            if (!string.IsNullOrEmpty(status)) schedules = schedules.Where(s => s.Status == status);

            var data = await schedules.OrderBy(s => s.ScheduledDate).ThenBy(s => s.ETD).ToListAsync();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Delivery Schedules");

            // Headers
            string[] headers = { "SCH NO", "DATE", "CUSTOMER", "ROUTE", "CYCLE", "DOCK IN", "PICKUP", "ETD", "START PREP", "END PREP", "QTY TARGET", "QTY ACTUAL", "STATUS" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            }

            // Data
            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.ScheduleNumber;
                worksheet.Cell(row, 2).Value = item.ScheduledDate.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 3).Value = item.Customer?.CustomerName ?? "-";
                worksheet.Cell(row, 4).Value = item.Route;
                worksheet.Cell(row, 5).Value = item.Cycle;
                worksheet.Cell(row, 6).Value = item.EnterDockTime?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 7).Value = item.PickupTime?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 8).Value = item.ETD?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 9).Value = FormatPrepTime(item.StartPrepareTime);
                worksheet.Cell(row, 10).Value = FormatPrepTime(item.StdPrepareTime);
                worksheet.Cell(row, 11).Value = (double)item.TotalTargetQuantity;
                worksheet.Cell(row, 12).Value = (double)item.TotalActualQuantity;
                worksheet.Cell(row, 13).Value = item.Status;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DeliverySchedules_{DateTime.Now:yyyyMMdd}.xlsx");
        }
        /// <summary>
        /// API endpoint untuk mendapatkan data tabel history schedule terbaru via AJAX.
        /// Mendukung filter yang sama dengan Index.
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetHistoryScheduleTableData(DateTime? startDate, DateTime? endDate, int? customerId, string? status)
        {
            // Default filter - tampilkan schedule hari ini jika tidak ada filter (konsisten dengan Index)
            if (!startDate.HasValue && !endDate.HasValue)
            {
                startDate = DateTime.Today;
                endDate = DateTime.Today.AddDays(7);
            }

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(d => d.ScheduledDate.Date >= startDate.Value.Date);
            }
            if (endDate.HasValue)
            {
                query = query.Where(d => d.ScheduledDate.Date <= endDate.Value.Date);
            }
            if (customerId.HasValue)
            {
                query = query.Where(d => d.CustomerId == customerId.Value);
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(d => d.Status == status);
            }

            var results = await query.ToListAsync();
            var sortedSchedules = results
                .OrderBy(s =>
                {
                    if (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue))
                        return 0;
                    if (s.Status == "Completed" || s.ActualEndTime.HasValue)
                        return 2;
                    if (s.Status == "Cancelled")
                        return 3;
                    return 1;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ETD ?? s.PickupTime ?? s.EnterDockTime ?? DateTime.MaxValue)
                .ThenBy(s => s.ScheduleId)
                .ToList();

            return PartialView("_HistoryScheduleTablePartial", sortedSchedules);
        }
        private int ParsePrepTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            if (value.Contains(":"))
            {
                // Try format HH:mm or H:mm
                var parts = value.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int mins))
                {
                    return (hours * 60) + mins;
                }
                
                if (TimeSpan.TryParse(value, out var ts))
                {
                    return (int)ts.TotalMinutes;
                }
            }
            
            // Try numeric
            if (double.TryParse(value, out double numericVal))
            {
                // If it's a fractional number like 0.8263888 (Excel's way of representing time)
                if (numericVal < 1 && numericVal > 0)
                {
                    return (int)Math.Round(numericVal * 24 * 60);
                }
                return (int)Math.Round(numericVal);
            }
            
            return 0;
        }

        private string FormatPrepTime(int totalMinutes)
        {
            if (totalMinutes <= 0) return "00:00";
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }
    }
}

