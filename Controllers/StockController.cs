using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Controllers
{
    public class StockController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StockController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Dashboard FG can default to showing data for any plant or a aggregate, 
            // but currently the view is built for a specific plant data.
            // Let's default to "Molded" for the indicators but use the Index view.
            return View(await GetStockViewModel("Molded"));
        }

        public async Task<IActionResult> Molded(DateTime? date)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            return View(await GetStockViewModel("Molded", date));
        }

        public async Task<IActionResult> Hose(DateTime? date)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            return View(await GetStockViewModel("Hose", date));
        }

        public async Task<IActionResult> RVI(DateTime? date)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            return View(await GetStockViewModel("RVI", date));
        }

        public async Task<IActionResult> Trend(string period = "Day")
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfYear = new DateTime(now.Year, 1, 1);

            var viewModel = new StockTrendViewModel
            {
                FilterPeriod = period
            };

            // 1. Total FG Stock (Sum of current stock in all plants)
            var moldedStockVM = await GetStockViewModel("Molded");
            var hoseStockVM = await GetStockViewModel("Hose");
            var rviStockVM = await GetStockViewModel("RVI");
            
            viewModel.TotalFGStock = moldedStockVM.NetStock + hoseStockVM.NetStock + rviStockVM.NetStock;

            // Populate Recent Shortage Lists
            viewModel.RecentMolded = moldedStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList();
            viewModel.RecentHose = hoseStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList();
            viewModel.RecentRVI = rviStockVM.StockDetails.Where(d => d.Status == "Shortage").ToList();

            // 2. Trend Data Calculation
            var plants = new[] { "Molded", "Hose", "RVI" };
            
            foreach (var plant in plants)
            {
                var plantTrend = new PlantTrendData { PlantName = plant.ToUpper() };
                
                // Map current stock
                if (plant == "Molded") plantTrend.TotalStock = moldedStockVM.NetStock;
                else if (plant == "Hose") plantTrend.TotalStock = hoseStockVM.NetStock;
                else if (plant == "RVI") plantTrend.TotalStock = rviStockVM.NetStock;

                var poolingQuery = _context.PoolingRecords.Where(r => r.Plant == plant);
                var preparationQuery = _context.PreparationRecords.Where(r => r.Plant == plant);

                if (period == "Day")
                {
                    // Group by Hour for Today
                    var poolingData = await poolingQuery
                        .Where(r => r.CreatedDate >= today)
                        .GroupBy(r => r.CreatedDate.Hour)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    var preparationData = await preparationQuery
                        .Where(r => r.CreatedDate >= today)
                        .GroupBy(r => r.CreatedDate.Hour)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    for (int i = 0; i < 24; i++)
                    {
                        plantTrend.DataPoints.Add(new TrendDataPoint 
                        { 
                            Label = $"{i:D2}:00", 
                            PoolingCount = poolingData.ContainsKey(i) ? poolingData[i] : 0,
                            PreparationCount = preparationData.ContainsKey(i) ? preparationData[i] : 0
                        });
                    }
                }
                else if (period == "Month")
                {
                    // Group by Day for current Month
                    var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                    var poolingData = await poolingQuery
                        .Where(r => r.CreatedDate >= startOfMonth)
                        .GroupBy(r => r.CreatedDate.Day)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    var preparationData = await preparationQuery
                        .Where(r => r.CreatedDate >= startOfMonth)
                        .GroupBy(r => r.CreatedDate.Day)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    for (int i = 1; i <= daysInMonth; i++)
                    {
                        plantTrend.DataPoints.Add(new TrendDataPoint 
                        { 
                            Label = i.ToString(), 
                            PoolingCount = poolingData.ContainsKey(i) ? poolingData[i] : 0,
                            PreparationCount = preparationData.ContainsKey(i) ? preparationData[i] : 0
                        });
                    }
                }
                else // Year
                {
                    // Group by Month for current Year
                    var poolingData = await poolingQuery
                        .Where(r => r.CreatedDate >= startOfYear)
                        .GroupBy(r => r.CreatedDate.Month)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    var preparationData = await preparationQuery
                        .Where(r => r.CreatedDate >= startOfYear)
                        .GroupBy(r => r.CreatedDate.Month)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count);

                    for (int i = 1; i <= 12; i++)
                    {
                        plantTrend.DataPoints.Add(new TrendDataPoint 
                        { 
                            Label = new DateTime(now.Year, i, 1).ToString("MMM"), 
                            PoolingCount = poolingData.ContainsKey(i) ? poolingData[i] : 0,
                            PreparationCount = preparationData.ContainsKey(i) ? preparationData[i] : 0
                        });
                    }
                }

                if (plantTrend.DataPoints.Any())
                {
                    plantTrend.PeakActivity = plantTrend.DataPoints.Max(p => Math.Max(p.PoolingCount, p.PreparationCount));
                }
                
                viewModel.PlantTrends.Add(plantTrend);
            }

            // Calculate Overall Trend (Aggregate from all plants)
            if (viewModel.PlantTrends.Any())
            {
                var numPoints = viewModel.PlantTrends[0].DataPoints.Count;
                for (int i = 0; i < numPoints; i++)
                {
                    var point = new TrendDataPoint { Label = viewModel.PlantTrends[0].DataPoints[i].Label };
                    foreach (var pt in viewModel.PlantTrends)
                    {
                        point.PoolingCount += pt.DataPoints[i].PoolingCount;
                        point.PreparationCount += pt.DataPoints[i].PreparationCount;
                    }
                    viewModel.OverallTrend.Add(point);
                }
            }
            
            if (viewModel.OverallTrend.Any())
            {
                viewModel.HighestActivityTarget = viewModel.OverallTrend.Max(p => Math.Max(p.PoolingCount, p.PreparationCount));
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

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant, DateTime? searchDate = null)
        {
            var today = DateTime.Today;
            var filterDate = searchDate ?? DateTime.Today;
            var isFiltered = searchDate.HasValue;

            // 1. Get all pieces currently in stock for this plant
            var allPooling = await _context.PoolingRecords
                .Where(r => r.Plant == plant)
                .Include(r => r.Item)
                .OrderBy(r => r.CreatedDate)
                .ToListAsync();

            var allPreparation = await _context.PreparationRecords
                .Where(r => r.Plant == plant)
                .OrderBy(r => r.CreatedDate)
                .ToListAsync();

            // FIFO matching logic: each PreparationRecord consumes exactly one oldest PoolingRecord 
            // that matches BOTH Tag and Label (case-insensitive & trimmed).
            // A preparation can only consume a piece that was pooled BEFORE or AT the time of preparation.
            // A 5-second buffer is added to handle potential timestamp precision issues between operations.
            var inStockPieces = new List<PoolingRecord>();
            var consumedPoolingIds = new HashSet<int>();

            foreach (var prep in allPreparation)
            {
                var prepTag = (prep.Tag ?? "").Trim().ToUpper();
                var prepLabel = (prep.Label ?? "").Trim().ToUpper();

                // Find oldest available pooling record that matches our criteria
                var match = allPooling.FirstOrDefault(p => 
                    !consumedPoolingIds.Contains(p.PoolingId) &&
                    (p.Tag ?? "").Trim().ToUpper() == prepTag && 
                    (p.Label ?? "").Trim().ToUpper() == prepLabel &&
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(5));

                if (match != null)
                {
                    consumedPoolingIds.Add(match.PoolingId);
                }
            }

            inStockPieces = allPooling
                .Where(p => !consumedPoolingIds.Contains(p.PoolingId))
                .ToList();

            // 2. Map Items to their status
            var allItems = await _context.Items.ToListAsync();
            var itemStatuses = new Dictionary<int, string>();
            var shortageCount = 0;
            var normalCount = 0;
            var overCount = 0;

            // Group pieces by ItemId to calculate status
            var piecesByItem = inStockPieces
                .Where(p => p.ItemId.HasValue)
                .GroupBy(p => p.ItemId!.Value)
                .ToDictionary(g => g.Key, g => (decimal)g.Count());

            foreach (var item in allItems)
            {
                var count = piecesByItem.ContainsKey(item.ItemId) ? piecesByItem[item.ItemId] : 0;
                string status;
                
                if (count < (item.RackMin ?? 5))
                {
                    status = "Shortage";
                }
                else if (count > (item.RackMax ?? 20))
                {
                    status = "Over";
                }
                else
                {
                    status = "Normal";
                }
                itemStatuses[item.ItemId] = status;
            }

            // 3. Build Detailed Table Data and Calculate Counts per Piece (Row)
            var stockDetails = new List<StockItemDetail>();
            shortageCount = 0;
            normalCount = 0;
            overCount = 0;

            foreach (var piece in inStockPieces.OrderByDescending(p => p.CreatedDate))
            {
                var status = piece.ItemId.HasValue && itemStatuses.ContainsKey(piece.ItemId.Value) 
                    ? itemStatuses[piece.ItemId.Value] 
                    : "None";

                if (status == "Shortage") shortageCount++;
                else if (status == "Normal") normalCount++;
                else if (status == "Over") overCount++;

                var currentStock = piece.ItemId.HasValue && piecesByItem.ContainsKey(piece.ItemId.Value) 
                    ? piecesByItem[piece.ItemId.Value] 
                    : 0;

                stockDetails.Add(new StockItemDetail
                {
                    Tag = piece.Tag,
                    Label = piece.Label,
                    ItemName = piece.Item?.ItemName ?? "N/A",
                    Time = piece.CreatedDate.ToString("HH:mm:ss"),
                    Date = piece.CreatedDate.ToString("dd-MM-yyyy"),
                    Location = piece.Item != null ? $"{piece.Item.Rack}.{piece.Item.NoRack}" : $"{piece.Rack}.{piece.Column}",
                    QtyLot = piece.Item?.QtyLot,
                    Min = piece.Item?.RackMin ?? 5,
                    Max = piece.Item?.RackMax ?? 20,
                    CurrentStock = currentStock,
                    LevelStock = (piece.Item?.RackMin ?? 5) > 0 
                        ? currentStock / (piece.Item?.RackMin ?? 5) 
                        : 0,
                    Operator = piece.CreatedBy ?? "-",
                    Status = status
                });
            }

            // Assign numbers (1 to N)
            for (int i = 0; i < stockDetails.Count; i++) stockDetails[i].No = i + 1;

            var viewModel = new StockDashboardViewModel
            {
                PlantName = plant,
                StockDetails = stockDetails,
                ShortageCount = shortageCount,
                NormalCount = normalCount,
                OverCount = overCount,
                RecentPooling = isFiltered 
                    ? inStockPieces.Where(r => r.CreatedDate.Date == filterDate.Date).OrderByDescending(r => r.CreatedDate).ToList()
                    : inStockPieces.OrderByDescending(r => r.CreatedDate).Take(10).ToList(),
                RecentPreparation = isFiltered
                    ? await _context.PreparationRecords
                        .Where(r => r.Plant == plant && r.CreatedDate.Date == filterDate.Date)
                        .OrderByDescending(r => r.CreatedDate)
                        .ToListAsync()
                    : await _context.PreparationRecords
                        .Where(r => r.Plant == plant)
                        .OrderByDescending(r => r.CreatedDate)
                        .Take(5)
                        .ToListAsync(),
                TotalPoolingToday = await _context.PoolingRecords
                    .CountAsync(r => r.Plant == plant && r.CreatedDate >= today),
                TotalPreparationToday = await _context.PreparationRecords
                    .CountAsync(r => r.Plant == plant && r.CreatedDate >= today)
            };

            return viewModel;
        }
    }
}
