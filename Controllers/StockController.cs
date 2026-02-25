using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    public class StockController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly StockSnapshotService _snapshotService;

        public StockController(ApplicationDbContext context, StockSnapshotService snapshotService)
        {
            _context = context;
            _snapshotService = snapshotService;
        }

        public async Task<IActionResult> Index(string plant = "Overall", DateTime? date = null, string period = "Day", int pageNumber = 1)
        {
            return View(await GetStockViewModel(plant, date, period, pageNumber));
        }

        public async Task<IActionResult> Molded(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("Molded", date, period, pageNumber));
        }

        public async Task<IActionResult> Hose(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("Hose", date, period, pageNumber));
        }

        public async Task<IActionResult> RVI(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("RVI", date, period, pageNumber));
        }

        public async Task<IActionResult> ExportToExcel(string plant = "Overall", DateTime? date = null, string period = "Day")
        {
            var viewModel = await GetStockViewModel(plant, date, period);
            var dateStr = (date ?? DateTime.Today).ToString("dd-MM-yyyy");
            
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Stock Report");
                
                // Header
                var headers = new string[] { "No", "Plant", "Tag", "Label", "Item Name", "Stock", "Level", "Location", "Time", "Date", "Operator" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                }

                // Data
                for (int i = 0; i < viewModel.StockDetails.Count; i++)
                {
                    var detail = viewModel.StockDetails[i];
                    worksheet.Cell(i + 2, 1).Value = detail.No;
                    worksheet.Cell(i + 2, 2).Value = detail.Plant;
                    worksheet.Cell(i + 2, 3).Value = detail.Tag;
                    worksheet.Cell(i + 2, 4).Value = detail.Label;
                    worksheet.Cell(i + 2, 5).Value = detail.ItemName;
                    worksheet.Cell(i + 2, 6).Value = detail.CurrentStock;
                    worksheet.Cell(i + 2, 7).Value = detail.LevelStock;
                    worksheet.Cell(i + 2, 8).Value = detail.Location;
                    worksheet.Cell(i + 2, 9).Value = detail.Time;
                    worksheet.Cell(i + 2, 10).Value = detail.Date;
                    worksheet.Cell(i + 2, 11).Value = detail.Operator;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"StockReport_{plant}_{dateStr}.xlsx");
                }
            }
        }

        public async Task<IActionResult> Trend(string period = "Month", string mode = "Activity")
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfYear = new DateTime(now.Year, 1, 1);

            var viewModel = new StockTrendViewModel
            {
                FilterPeriod = period,
                ViewMode = mode
            };

            // 1. Calculate Total FG Activity based on selected period for relevant plants
            var activePlants = new[] { "Molded", "Hose", "RVI" };
            DateTime filterStartDate = period switch
            {
                "Year" => startOfYear,
                "Day" => today,
                _ => startOfMonth // Default to "Month"
            };

            viewModel.TotalFGStock = await _context.PullingRecords
                .CountAsync(r => activePlants.Contains(r.Plant) && r.CreatedDate >= filterStartDate);

            if (mode == "Level")
            {
                viewModel.CategoryTrends = await GetHistoricalCategoryTrends(filterStartDate, now, period);
            }

            // Fetch current stock summaries for recent activity status mapping
            var moldedStockVM = await GetStockViewModel("Molded");
            var hoseStockVM = await GetStockViewModel("Hose");
            var rviStockVM = await GetStockViewModel("RVI");

            // Fetch pulling records based on the selected period for each plant
            // Fetch pulling records based on the selected period for each plant
            var moldedPulling = await _context.PullingRecords
                .Where(r => r.Plant == "Molded" && r.CreatedDate >= filterStartDate)
                .Include(r => r.Item)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            var hosePulling = await _context.PullingRecords
                .Where(r => r.Plant == "Hose" && r.CreatedDate >= filterStartDate)
                .Include(r => r.Item)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            var rviPulling = await _context.PullingRecords
                .Where(r => r.Plant == "RVI" && r.CreatedDate >= filterStartDate)
                .Include(r => r.Item)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            // Map to StockItemDetail for the view (Conditional: Shortage Grouping for Level, Transactional for Activity)
            var moldedTransList = moldedPulling.Select(p => new StockItemDetail {
                ItemName = p.Item?.ItemName ?? "N/A",
                VIN = p.Item?.VIN ?? "-",
                LevelStock = p.Item != null && moldedStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? moldedStockVM.StockDetails.First(d => d.Tag == p.Tag).LevelStock : 0,
                Status = p.Item != null && moldedStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? moldedStockVM.StockDetails.First(d => d.Tag == p.Tag).Status : "Normal"
            }).ToList();
            viewModel.RecentMolded = mode == "Level" 
                ? moldedStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList() 
                : moldedTransList;

            var hoseTransList = hosePulling.Select(p => new StockItemDetail {
                ItemName = p.Item?.ItemName ?? "N/A",
                VIN = p.Item?.VIN ?? "-",
                LevelStock = p.Item != null && hoseStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? hoseStockVM.StockDetails.First(d => d.Tag == p.Tag).LevelStock : 0,
                Status = p.Item != null && hoseStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? hoseStockVM.StockDetails.First(d => d.Tag == p.Tag).Status : "Normal"
            }).ToList();
            viewModel.RecentHose = mode == "Level" 
                ? hoseStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList() 
                : hoseTransList;

            var rviTransList = rviPulling.Select(p => new StockItemDetail {
                ItemName = p.Item?.ItemName ?? "N/A",
                VIN = p.Item?.VIN ?? "-",
                LevelStock = p.Item != null && rviStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? rviStockVM.StockDetails.First(d => d.Tag == p.Tag).LevelStock : 0,
                Status = p.Item != null && rviStockVM.StockDetails.Any(d => d.Tag == p.Tag) 
                                ? rviStockVM.StockDetails.First(d => d.Tag == p.Tag).Status : "Normal"
            }).ToList();
            viewModel.RecentRVI = mode == "Level" 
                ? rviStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList() 
                : rviTransList;

            // 2. Trend Data Calculation
            // Pre-fetch Category Snapshots ONCE for high performance
            var allSnapshots = await GetHistoricalCategorySnapshots(filterStartDate, now, period);

            foreach (var plant in activePlants)
            {
                var plantTrend = new PlantTrendData { PlantName = plant.ToUpper() };
                var pullingQuery = _context.PullingRecords.Where(r => r.Plant == plant);
                var preparationQuery = _context.PreparationRecords.Where(r => r.Plant == plant);
                plantTrend.TotalStock = await pullingQuery.CountAsync(r => r.CreatedDate >= filterStartDate);

                if (period == "Day")
                {
                    var pullingData = await pullingQuery.Where(r => r.CreatedDate >= today).GroupBy(r => r.CreatedDate.Hour).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    var preparationData = await preparationQuery.Where(r => r.CreatedDate >= today).GroupBy(r => r.CreatedDate.Hour).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    for (int i = 0; i < 24; i++) {
                        plantTrend.DataPoints.Add(new TrendDataPoint { Label = $"{i:D2}:00", PullingCount = pullingData.GetValueOrDefault(i, 0), PreparationCount = preparationData.GetValueOrDefault(i, 0) });
                    }
                }
                else if (period == "Month")
                {
                    var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                    var pullingData = await pullingQuery.Where(r => r.CreatedDate >= startOfMonth).GroupBy(r => r.CreatedDate.Day).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    var preparationData = await preparationQuery.Where(r => r.CreatedDate >= startOfMonth).GroupBy(r => r.CreatedDate.Day).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    for (int i = 1; i <= daysInMonth; i++) {
                        plantTrend.DataPoints.Add(new TrendDataPoint { Label = i.ToString(), PullingCount = pullingData.GetValueOrDefault(i, 0), PreparationCount = preparationData.GetValueOrDefault(i, 0) });
                    }
                }
                else // Year
                {
                    var pullingData = await pullingQuery.Where(r => r.CreatedDate >= startOfYear).GroupBy(r => r.CreatedDate.Month).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    var preparationData = await preparationQuery.Where(r => r.CreatedDate >= startOfYear).GroupBy(r => r.CreatedDate.Month).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
                    for (int i = 1; i <= 12; i++) {
                        plantTrend.DataPoints.Add(new TrendDataPoint { Label = new DateTime(now.Year, i, 1).ToString("MMM"), PullingCount = pullingData.GetValueOrDefault(i, 0), PreparationCount = preparationData.GetValueOrDefault(i, 0) });
                    }
                }

                if (plantTrend.DataPoints.Any()) {
                    plantTrend.PeakActivity = plantTrend.DataPoints.Max(p => Math.Max(p.PullingCount, p.PreparationCount));
                }

                plantTrend.CategoryTrends = allSnapshots.Select(s => {
                    var pStats = s.PlantStats.GetValueOrDefault(plant.ToUpper()) ?? new CategoryStats();
                    return new CategoryTrendPoint {
                        Label = s.Label,
                        CatLess1 = pStats.Less1,
                        CatLess1_5 = pStats.Less1_5,
                        CatRange1_5_2 = pStats.Range1_5_2,
                        CatRange2_3 = pStats.Range2_3,
                        CatMore3 = pStats.More3,
                        ShortageCount = pStats.Less1 + pStats.Less1_5,
                        NormalCount = pStats.Range1_5_2 + pStats.Range2_3,
                        OverCount = pStats.More3
                    };
                }).ToList();

                viewModel.PlantTrends.Add(plantTrend);
            }

            // Calculate Overall Trend
            if (viewModel.PlantTrends.Any())
            {
                var numPoints = viewModel.PlantTrends[0].DataPoints.Count;
                for (int i = 0; i < numPoints; i++)
                {
                    var point = new TrendDataPoint { Label = viewModel.PlantTrends[0].DataPoints[i].Label };
                    foreach (var pt in viewModel.PlantTrends) {
                        point.PullingCount += pt.DataPoints[i].PullingCount;
                        point.PreparationCount += pt.DataPoints[i].PreparationCount;
                    }
                    viewModel.OverallTrend.Add(point);
                }

                // Populate Overall Category Trends FROM THE SAME pre-fetched snapshots
                viewModel.CategoryTrends = allSnapshots.Select(s => new CategoryTrendPoint {
                    Label = s.Label,
                    CatLess1 = s.TotalStats.Less1,
                    CatLess1_5 = s.TotalStats.Less1_5,
                    CatRange1_5_2 = s.TotalStats.Range1_5_2,
                    CatRange2_3 = s.TotalStats.Range2_3,
                    CatMore3 = s.TotalStats.More3,
                    ShortageCount = s.TotalStats.Less1 + s.TotalStats.Less1_5,
                    NormalCount = s.TotalStats.Range1_5_2 + s.TotalStats.Range2_3,
                    OverCount = s.TotalStats.More3
                }).ToList();
            }
            
            if (viewModel.OverallTrend.Any()) {
                viewModel.HighestActivityTarget = viewModel.OverallTrend.Max(p => Math.Max(p.PullingCount, p.PreparationCount));
            }

            return View(viewModel);
        }


        public async Task<IActionResult> Targets()
        {
            var items = await _context.Items.OrderBy(i => i.ItemCode).ToListAsync();
            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTargets(List<ItemTargetUpdate> updates)
        {
            if (updates == null || !updates.Any()) return RedirectToAction(nameof(Molded));

            foreach (var update in updates)
            {
                var item = await _context.Items.FindAsync(update.ItemId);
                if (item != null)
                {
                    item.Rack = update.Rack;
                    item.NoRack = update.NoRack;
                    item.QtyLot = update.QtyLot;
                    item.RackMin = update.RackMin;
                    item.RackMax = update.RackMax;
                    item.UpdatedDate = DateTime.Now;
                    _context.Update(item);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Stock targets updated successfully.";
            return RedirectToAction("Index", "Items");
        }

        public class ItemTargetUpdate
        {
            public int ItemId { get; set; }
            public string? Rack { get; set; }
            public int? NoRack { get; set; }
            public int? QtyLot { get; set; }
            public int? RackMin { get; set; }
            public int? RackMax { get; set; }
        }

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant, DateTime? searchDate = null, string period = "Day", int pageNumber = 1)
        {
            var today = DateTime.Today;
            var isFilteredByDate = searchDate.HasValue;
            var filterDate = searchDate ?? today;

            DateTime startDate, endDate;
            if (period == "Week")
            {
                startDate = filterDate.Date.AddDays(-6);
                endDate = filterDate.Date.AddDays(1).AddSeconds(-1);
            }
            else if (period == "Month")
            {
                startDate = new DateTime(filterDate.Year, filterDate.Month, 1);
                endDate = startDate.AddMonths(1).AddSeconds(-1);
            }
            else if (period == "Year")
            {
                startDate = new DateTime(filterDate.Year, 1, 1);
                endDate = startDate.AddYears(1).AddSeconds(-1);
            }
            else // Day
            {
                startDate = filterDate.Date;
                endDate = startDate.AddDays(1).AddSeconds(-1);
            }

            // STOCK CALCULATION LOGIC (FIFO)
            // 1. Pulling: Ambil SEMUA data historis (Tanpa batas waktu) untuk pencocokan FIFO yang akurat
            //    Karena barang yang ditarik 1 bulan lalu bisa saja baru disiapkan hari ini.
            var pullingQueryAll = _context.PullingRecords.AsNoTracking().Include(r => r.Item).AsQueryable();
            if (plant != "Overall") pullingQueryAll = pullingQueryAll.Where(r => r.Plant == plant);
            
            // 2. Preparation (STOCK CALCULATION): Use ALL TIME data to ensure accurate stock balance
            //    We must deduct ALL preparations that have ever happened, not just those in the selected period.
            var preparationQueryAll = _context.PreparationRecords.AsNoTracking().AsQueryable();
            if (plant != "Overall") preparationQueryAll = preparationQueryAll.Where(r => r.Plant == plant);

            // 3. Preparation & Pulling (DISPLAY/ACTIVITY): Filter by date for the "Recent Activity" list and "Counts"
            var pullingQueryInRange = pullingQueryAll.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);
            var preparationQueryFiltered = preparationQueryAll.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            // Execute queries
            var allPullingPotential = await pullingQueryAll.OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId).ToListAsync();
            // Critical Change: Fetch ALL preparations for calculation
            var allPreparationAllTime = await preparationQueryAll.OrderBy(r => r.CreatedDate).ThenBy(r => r.PreparationId).ToListAsync(); 
            // Fetch filtered for display
            var allPullingInRange = await pullingQueryInRange.OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId).ToListAsync();
            var allPreparationInRange = await preparationQueryFiltered.OrderBy(r => r.CreatedDate).ThenBy(r => r.PreparationId).ToListAsync();

            var consumedPullingIds = new HashSet<int>();

            // Pencocokan FIFO: Untuk setiap Preparation (ALL TIME), cari Pulling tertua yang belum terpakai
            // Ini memastikan saldo stok BENAR, tidak peduli kapan barang itu disiapkan.
            foreach (var prep in allPreparationAllTime)
            {
                var prepTag = (prep.Tag ?? "").Trim().ToUpper();
                var prepLabel = (prep.Label ?? "").Trim().ToUpper();

                // Cari kecocokan Pulling TERTUA (FIFO)
                // Syarat: Tag & Label sama, Id belum terpakai, dan waktu Pulling <= waktu Preparation
                var match = allPullingPotential.FirstOrDefault(p => 
                    !consumedPullingIds.Contains(p.PullingId) && 
                    (p.Tag ?? "").Trim().ToUpper() == prepTag && 
                    (p.Label ?? "").Trim().ToUpper() == prepLabel && 
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10)); // Tolerance for sync delays

                if (match != null) consumedPullingIds.Add(match.PullingId);
            }

            // Stok yang MASIH ADA = Semua Pulling historis - Pulling yang sudah dikonsumsi oleh Preparation mana pun
            var inStockPieces = allPullingPotential.Where(p => !consumedPullingIds.Contains(p.PullingId)).ToList();
            var allItems = await _context.Items.AsNoTracking().ToListAsync();
            var itemStatuses = new Dictionary<int, string>();
            
            // piecesByItem: Tetap per ItemId untuk perhitungan status (Shortage/Over)
            var piecesByItem = inStockPieces.Where(p => p.ItemId.HasValue)
                .GroupBy(p => p.ItemId!.Value)
                .ToDictionary(g => g.Key, g => (decimal)g.Count());

            foreach (var item in allItems)
            {
                var count = piecesByItem.GetValueOrDefault(item.ItemId, 0);
                var min = (decimal)(item.RackMin ?? 5);
                var level = min > 0 ? count / min : 0;

                if (level < 1.5m) itemStatuses[item.ItemId] = "Shortage";
                else if (count > (item.RackMax ?? 20)) itemStatuses[item.ItemId] = "Over";
                else itemStatuses[item.ItemId] = "Normal";
            }

            // --- COUNT UNIQUE ITEMS FOR INDICATORS (Based on Item status - ONLY FOR IN-STOCK ITEMS) ---
            var inStockItemIds = piecesByItem.Keys;
            var shortageCount = itemStatuses.Where(kv => inStockItemIds.Contains(kv.Key)).Count(v => v.Value == "Shortage");
            var normalCount = itemStatuses.Where(kv => inStockItemIds.Contains(kv.Key)).Count(v => v.Value == "Normal");
            var overCount = itemStatuses.Where(kv => inStockItemIds.Contains(kv.Key)).Count(v => v.Value == "Over");
            // ------------------------------------------------------------------

            var stockDetails = new List<StockItemDetail>();

            // GROUP BY LABEL: Agar tampilan di dashboard digabung per Label
            var groupedByLabel = inStockPieces.GroupBy(p => new { p.ItemId, Label = (p.Label ?? "").Trim().ToUpper() });

            foreach (var group in groupedByLabel)
            {
                var latestPiece = group.OrderByDescending(p => p.CreatedDate).First();
                var status = latestPiece.ItemId.HasValue ? itemStatuses.GetValueOrDefault(latestPiece.ItemId.Value, "None") : "None";
                
                // Hitung jumlah box UNTUK LABEL INI SAJA (Permintaan user: agregasi per label)
                var labelStockCount = (decimal)group.Count();
                
                // (shortageCount, normalCount, overCount calculations moved outside loop to count unique items)

                // SYNC LEVEL STOCK WITH ITEM STATUS: 
                // Permintaan user: Ikon/Badge di tabel harus konsisten dengan status global item.
                // Maka LevelStock di sini adalah level TOTAL item, bukan cuma label ini.
                var totalItemStock = piecesByItem.GetValueOrDefault(latestPiece.ItemId ?? -1, 0);
                var itemLevelStock = (latestPiece.Item?.RackMin ?? 5) > 0 ? totalItemStock / (latestPiece.Item?.RackMin ?? 5) : 0;

                stockDetails.Add(new StockItemDetail
                {
                    Tag = latestPiece.Tag, 
                    Label = latestPiece.Label, 
                    ItemName = latestPiece.Item?.ItemName ?? "N/A", 
                    Time = latestPiece.CreatedDate.ToString("HH:mm:ss"), 
                    Date = latestPiece.CreatedDate.ToString("dd-MM-yyyy"),
                    Location = latestPiece.Item != null ? $"{latestPiece.Item.Rack}.{latestPiece.Item.NoRack}" : $"{latestPiece.Rack}.{latestPiece.Column}", 
                    Plant = latestPiece.Plant ?? string.Empty, 
                    QtyLot = latestPiece.Item?.QtyLot,
                    RackInfo = latestPiece.Item != null ? $"{latestPiece.Item.Rack}.{latestPiece.Item.NoRack}" : "-",
                    Customer = latestPiece.Item?.Customer ?? "-",
                    Category = latestPiece.Item?.Category ?? "-",
                    VIN = latestPiece.Item?.VIN ?? "-",
                    IsActive = latestPiece.Item?.IsActive ?? true,
                    Min = latestPiece.Item?.RackMin ?? 5, 
                    Rop = latestPiece.Item?.ROP ?? 0,
                    Max = latestPiece.Item?.RackMax ?? 20, 
                    CurrentStock = labelStockCount, // TETAP: Jumlah box fisik label ini
                    LevelStock = itemLevelStock, // SYNC: Level total item (untuk ikon)
                    Operator = latestPiece.CreatedBy ?? "-", 
                    Status = status,
                    LastActivityDate = latestPiece.CreatedDate
                });
            }
            
            // Sort by LastActivityDate DESC (Newest Scan first) -> Then stability sorts
            stockDetails = stockDetails
                .OrderByDescending(s => s.LastActivityDate)
                .ThenBy(s => s.Plant)
                .ThenBy(s => s.Location)
                .ToList();

            var totalItems = stockDetails.Count;
            int pageSize = 20;

            // IF IN TRANSACTION LOG VIEW (Plant != Overall), total items = max count between In and Out logs
            if (plant != "Overall")
            {
                totalItems = Math.Max(allPullingInRange.Count, allPreparationInRange.Count);
            }

            var pagedStockDetails = stockDetails
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            for (int i = 0; i < pagedStockDetails.Count; i++) pagedStockDetails[i].No = ((pageNumber - 1) * pageSize) + i + 1;

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { 
                { "plant", plant },
                { "date", searchDate?.ToString("yyyy-MM-dd") },
                { "period", period }
            };

            return new StockDashboardViewModel
            {
                PlantName = plant, StockDetails = pagedStockDetails, ShortageCount = shortageCount, NormalCount = normalCount, OverCount = overCount, SearchDate = searchDate,
                RecentPulling = allPullingInRange.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.PullingId).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                RecentPreparation = allPreparationInRange.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.PreparationId).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalPullingToday = await _context.PullingRecords.CountAsync(r => (plant == "Overall" || r.Plant == plant) && r.CreatedDate >= startDate && r.CreatedDate <= endDate),
                TotalPreparationToday = await _context.PreparationRecords.CountAsync(r => (plant == "Overall" || r.Plant == plant) && r.CreatedDate >= startDate && r.CreatedDate <= endDate),
                Period = period
            };
        }

        public async Task<IActionResult> ExportTrendToExcel()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var endOfMonth = new DateTime(now.Year, now.Month, daysInMonth, 23, 59, 59);

            var snapshots = await GetHistoricalCategorySnapshots(startOfMonth, endOfMonth);

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Trend Level Stock");
                string[] categories = { "< 1 D", "< 1.5 D", "1.5 - 2 D", "2 - 3 D", "> 3 D" };
                int currentRow = 1;

                foreach (var cat in categories)
                {
                    // Block Heading
                    var titleRange = worksheet.Range(currentRow, 1, currentRow, daysInMonth + 1);
                    titleRange.Merge().Value = $"SUMMARY STOCK FG VIN {cat} DAY";
                    titleRange.Style.Font.Bold = true;
                    titleRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    titleRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.AliceBlue;
                    currentRow++;

                    // Table Header
                    worksheet.Cell(currentRow, 1).Value = cat;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.Red;
                    worksheet.Cell(currentRow, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;

                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        var cell = worksheet.Cell(currentRow, d + 1);
                        cell.Value = $"{d:D2}-{now:MMM}";
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                        cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                    }
                    currentRow++;

                    // Rows: Plan Total, Act Hose, Act Mold, Act RVI, Act BTR, Act Total
                    string[] rows = { "Plan Total", "Act Hose", "Act Mold", "Act RVI", "Act BTR", "Act Total" };
                    foreach (var rowName in rows)
                    {
                        worksheet.Cell(currentRow, 1).Value = rowName;
                        if (rowName == "Act Total") worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGreen;
                        worksheet.Cell(currentRow, 1).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                        for (int d = 1; d <= daysInMonth; d++)
                        {
                            var dayData = snapshots.FirstOrDefault(s => s.Date.Day == d);
                            int val = 0;
                            if (dayData != null)
                            {
                                if (rowName == "Plan Total") val = 0; // Static context, can be updated if Plan data exists
                                else if (rowName == "Act Hose") val = dayData.PlantStats.GetValueOrDefault("HOSE")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act Mold") val = dayData.PlantStats.GetValueOrDefault("MOLDED")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act RVI") val = dayData.PlantStats.GetValueOrDefault("RVI")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act BTR") val = dayData.PlantStats.GetValueOrDefault("BTR")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act Total") val = dayData.TotalStats.GetCount(cat);
                            }
                            var dataCell = worksheet.Cell(currentRow, d + 1);
                            dataCell.Value = val;
                            dataCell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                        }
                        currentRow++;
                    }
                    currentRow += 2; // Spacer
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TrendStockReport_{now:MMM_yyyy}.xlsx");
                }
            }
        }

        private async Task<List<CategoryTrendPoint>> GetHistoricalCategoryTrends(DateTime start, DateTime end, string period)
        {
            var snapshots = await GetHistoricalCategorySnapshots(start, end, period);
            return snapshots.Select(s => new CategoryTrendPoint
            {
                Label = s.Label,
                CatLess1 = s.TotalStats.Less1,
                CatLess1_5 = s.TotalStats.Less1_5,
                CatRange1_5_2 = s.TotalStats.Range1_5_2,
                CatRange2_3 = s.TotalStats.Range2_3,
                CatMore3 = s.TotalStats.More3,
                
                HoseShortage = (s.PlantStats.GetValueOrDefault("HOSE")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("HOSE")?.Less1_5 ?? 0),
                MoldedShortage = (s.PlantStats.GetValueOrDefault("MOLDED")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("MOLDED")?.Less1_5 ?? 0),
                RviShortage = (s.PlantStats.GetValueOrDefault("RVI")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("RVI")?.Less1_5 ?? 0)
            }).ToList();
        }

        private async Task<List<DaySnapshot>> GetHistoricalCategorySnapshots(DateTime start, DateTime end, string period = "Month")
        {
            var items = await _context.Items.ToListAsync();
            var pullings = await _context.PullingRecords.Where(p => p.CreatedDate <= end).OrderBy(p => p.CreatedDate).ToListAsync();
            var preps = await _context.PreparationRecords.Where(p => p.CreatedDate <= end).OrderBy(p => p.CreatedDate).ToListAsync();

            var snapshots = new List<DaySnapshot>();
            var availablePullings = new List<PullingRecord>();
            int pIdx = 0, rIdx = 0;

            var intervalEnds = new List<DateTime>();
            if (period == "Day") {
                for (int i = 1; i <= 24; i++) intervalEnds.Add(start.Date.AddHours(i));
            } else if (period == "Month") {
                for (int i = 1; i <= DateTime.DaysInMonth(start.Year, start.Month); i++) 
                    intervalEnds.Add(new DateTime(start.Year, start.Month, i, 23, 59, 59));
            } else {
                for (int i = 1; i <= 12; i++) 
                    intervalEnds.Add(new DateTime(start.Year, i, DateTime.DaysInMonth(start.Year, i), 23, 59, 59));
            }

            foreach (var tEnd in intervalEnds)
            {
                while (pIdx < pullings.Count && pullings[pIdx].CreatedDate <= tEnd) { availablePullings.Add(pullings[pIdx]); pIdx++; }
                while (rIdx < preps.Count && preps[rIdx].CreatedDate <= tEnd)
                {
                    var prep = preps[rIdx];
                    var tag = (prep.Tag ?? "").Trim().ToUpper();
                    var lbl = (prep.Label ?? "").Trim().ToUpper();
                    var match = availablePullings.FirstOrDefault(p => 
                        (p.Tag ?? "").Trim().ToUpper() == tag && (p.Label ?? "").Trim().ToUpper() == lbl && p.CreatedDate <= prep.CreatedDate.AddSeconds(5));
                    if (match != null) availablePullings.Remove(match);
                    rIdx++;
                }

                var snapshot = new DaySnapshot { Date = tEnd, Label = period == "Day" ? $"{tEnd.Hour-1:D2}:00" : period == "Month" ? tEnd.Day.ToString() : tEnd.ToString("MMM") };
                var stockByItem = availablePullings.Where(p => p.ItemId.HasValue).GroupBy(p => p.ItemId!.Value).ToDictionary(g => g.Key, g => (decimal)g.Count());
                
                foreach (var item in items)
                {
                    var stock = stockByItem.GetValueOrDefault(item.ItemId, 0);
                    var min = (decimal)(item.RackMin ?? 5);
                    var level = min > 0 ? stock / min : 0;
                    var plant = (availablePullings.FirstOrDefault(p => p.ItemId == item.ItemId)?.Plant ?? "Unknown").ToUpper();

                    var stats = snapshot.PlantStats.GetOrAdd(plant, () => new CategoryStats());
                    UpdateStats(stats, level);
                    UpdateStats(snapshot.TotalStats, level);
                }
                snapshots.Add(snapshot);
            }
            return snapshots;
        }

        private void UpdateStats(CategoryStats s, decimal level)
        {
            if (level < 1) s.Less1++;
            else if (level < 1.5m) s.Less1_5++;
            else if (level >= 1.5m && level <= 2m) s.Range1_5_2++;
            else if (level > 2m && level <= 3m) s.Range2_3++;
            else if (level > 3m) s.More3++;
        }

        private class CategoryStats {
            public int Less1 { get; set; }
            public int Less1_5 { get; set; }
            public int Range1_5_2 { get; set; }
            public int Range2_3 { get; set; }
            public int More3 { get; set; }
            public int GetCount(string cat) => cat switch { "< 1 D" => Less1, "< 1.5 D" => Less1_5, "1.5 - 2 D" => Range1_5_2, "2 - 3 D" => Range2_3, "> 3 D" => More3, _ => 0 };
        }

        private class DaySnapshot {
            public DateTime Date { get; set; }
            public string Label { get; set; } = "";
            public Dictionary<string, CategoryStats> PlantStats { get; set; } = new();
            public CategoryStats TotalStats { get; set; } = new();
        }

        // [ResetStockData removed for safety]

        /// <summary>Halaman Critical Stock Trend — membaca StockSnapshot</summary>
        public async Task<IActionResult> TrendCriticalStock(string level = "1.5D", string period = "30d")
        {
            var endDate   = DateTime.Today;
            var startDate = period switch
            {
                "7d"  => endDate.AddDays(-6),
                "14d" => endDate.AddDays(-13),
                "30d" => endDate.AddDays(-29),
                "90d" => endDate.AddDays(-89),
                _     => endDate.AddDays(-29)
            };

            var allPlants = await _context.StockSnapshots
                .Where(s => !s.IsManual)
                .Select(s => s.Plant)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();

            var snapshots = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date >= startDate
                         && s.SnapshotDate.Date <= endDate
                         && !s.IsManual)
                .ToListAsync();

            var dateRange = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(i => startDate.AddDays(i))
                .ToList();

            // Breakdown per plant (untuk chart & cards)
            var plantBreakdown = allPlants.Select(p =>
            {
                var ps = snapshots.Where(s => s.Plant == p).ToList();

                var trend = dateRange.Select(date =>
                {
                    var daySnaps = ps.Where(s => s.SnapshotDate.Date == date).ToList();
                    return new
                    {
                        Date        = date.ToString("dd/MM"),
                        DateFull    = date.ToString("dd/MM/yyyy"),
                        HasSnapshot = daySnaps.Any(),
                        Below1D     = daySnaps.Count(s => s.StockLevel == "<1D"),
                        Below1_5D   = daySnaps.Count(s => s.StockLevel == "<1D" || s.StockLevel == "<1.5D"),
                    };
                }).ToList();

                // Top 5 item paling sering kritis (<1D dalam periode ini)
                var criticalItems = ps
                    .Where(s => s.StockLevel == "<1D")
                    .GroupBy(s => new { s.ItemCode, s.ItemName })
                    .Select(g => new
                    {
                        g.Key.ItemCode,
                        g.Key.ItemName,
                        CriticalDays = g.Select(x => x.SnapshotDate.Date).Distinct().Count(),
                    })
                    .OrderByDescending(x => x.CriticalDays)
                    .Take(5)
                    .ToList();

                var todayPs = ps.Where(s => s.SnapshotDate.Date == endDate).ToList();

                return new
                {
                    Plant         = p,
                    Trend         = trend,
                    CriticalItems = criticalItems,
                    TotalBelow1D  = todayPs.Count(s => s.StockLevel == "<1D"),
                    TotalBelow1_5D = todayPs.Count(s => s.StockLevel == "<1D" || s.StockLevel == "<1.5D"),
                };
            }).ToList();

            // Summary hari ini
            var todayAll = snapshots.Where(s => s.SnapshotDate.Date == endDate).ToList();
            ViewBag.TodayBelow1D   = todayAll.Count(s => s.StockLevel == "<1D");
            ViewBag.TodayBelow1_5D = todayAll.Count(s => s.StockLevel == "<1D" || s.StockLevel == "<1.5D");
            ViewBag.TodayTotal     = todayAll.Count;
            ViewBag.LastSnapshot   = await _context.StockSnapshots
                .Where(s => !s.IsManual)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => (DateTime?)s.CreatedAt)
                .FirstOrDefaultAsync();

            ViewBag.AllPlants      = allPlants;
            ViewBag.SelectedLevel  = level;
            ViewBag.SelectedPeriod = period;
            ViewBag.PlantBreakdown = JsonSerializer.Serialize(plantBreakdown);
            ViewBag.DateLabels     = JsonSerializer.Serialize(dateRange.Select(d => d.ToString("dd/MM")).ToList());

            return View();
        }

        /// <summary>Manual trigger snapshot — hanya Admin</summary>
        [HttpPost]
        public async Task<IActionResult> TriggerSnapshot()
        {
            try
            {
                var (count, message) = await _snapshotService.TakeSnapshotAsync(isManual: true);
                return Json(new { success = count > 0, message, itemCount = count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, itemCount = 0 });
            }
        }
    }

    public static class DictExtensions {
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TValue> factory) where TKey : notnull
        {
            if (!dict.TryGetValue(key, out var val)) { val = factory(); dict[key] = val; }
            return val;
        }
    }
}
