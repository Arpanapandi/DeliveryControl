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

        public IActionResult Index()
        {
            return RedirectToAction("Molded");
        }

        public async Task<IActionResult> Molded()
        {
            return View(await GetStockViewModel("Molded"));
        }

        public async Task<IActionResult> Hose()
        {
            return View(await GetStockViewModel("Hose"));
        }

        public async Task<IActionResult> RVI()
        {
            return View(await GetStockViewModel("RVI"));
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
                    item.MinStock = update.MinStock;
                    item.MaxStock = update.MaxStock;
                    item.UpdatedDate = DateTime.Now;
                    _context.Update(item);
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Stock targets updated successfully.";
            return RedirectToAction(nameof(Molded));
        }

        public class ItemTargetUpdate
        {
            public int ItemId { get; set; }
            public int MinStock { get; set; }
            public int MaxStock { get; set; }
        }

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant)
        {
            var today = DateTime.Today;

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
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));

            foreach (var item in allItems)
            {
                var count = piecesByItem.ContainsKey(item.ItemId) ? piecesByItem[item.ItemId] : 0;
                string status;
                
                if (count < item.MinStock)
                {
                    status = "Shortage";
                }
                else if (count > item.MaxStock)
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

                stockDetails.Add(new StockItemDetail
                {
                    Tag = piece.Tag,
                    Label = piece.Label,
                    Time = piece.CreatedDate.ToString("HH:mm:ss"),
                    Date = piece.CreatedDate.ToString("dd-MM-yyyy"),
                    Location = $"{piece.Rack}-{piece.Column}",
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
                RecentPooling = allPooling.OrderByDescending(r => r.CreatedDate).Take(5).ToList(),
                RecentPreparation = await _context.PreparationRecords
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
