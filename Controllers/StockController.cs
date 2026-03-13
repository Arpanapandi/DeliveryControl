using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DeliveryControl.Services;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "Pulling", "Leader", "User")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class StockController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly StockSnapshotService _snapshotService;

        public StockController(ApplicationDbContext context, StockSnapshotService snapshotService)
        {
            _context = context;
            _snapshotService = snapshotService;
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Index(string plant = "Overall", DateTime? date = null, string period = "Day", int pageNumber = 1, string status = "All")
        {
            return View(await GetStockViewModel(plant, date, period, pageNumber, status));
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Molded(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("Molded", date, period, pageNumber));
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Hose(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("Hose", date, period, pageNumber));
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> RVI(DateTime? date, string period = "Day", int pageNumber = 1)
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            return View(await GetStockViewModel("RVI", date, period, pageNumber));
        }

        [DeliveryControl.Filters.AuthorizeRoles("Admin", "User")]
        public async Task<IActionResult> LogScanNG(DateTime? date, string period = "Day", int pageNumber = 1, string search = "")
        {
            var today = DateTime.Today;
            var filterDate = date ?? today;
            ViewBag.SelectedDate = filterDate.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.Search = search;

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

            int pageSize = 25;

            var ngPullingQuery = _context.ScanNGLogs.AsNoTracking()
                .Where(r => r.Module == "Pulling" && r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            var ngPreparationQuery = _context.ScanNGLogs.AsNoTracking()
                .Where(r => r.Module == "Preparation" && r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                ngPullingQuery = ngPullingQuery.Where(r =>
                    r.Tag.ToLower().Contains(s) ||
                    r.Label.ToLower().Contains(s) ||
                    r.CreatedBy.ToLower().Contains(s));
                ngPreparationQuery = ngPreparationQuery.Where(r =>
                    r.Tag.ToLower().Contains(s) ||
                    r.Label.ToLower().Contains(s) ||
                    r.Kanban.ToLower().Contains(s) ||
                    r.CreatedBy.ToLower().Contains(s));
            }

            ngPullingQuery = ngPullingQuery.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id);
            ngPreparationQuery = ngPreparationQuery.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id);

            var totalNGPulling = await ngPullingQuery.CountAsync();
            var totalNGPreparation = await ngPreparationQuery.CountAsync();
            var totalItems = totalNGPulling + totalNGPreparation;

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(Math.Max(totalNGPulling, totalNGPreparation) / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string>
            {
                { "date", date?.ToString("yyyy-MM-dd") },
                { "period", period },
                { "search", search }
            };

            var viewModel = new StockDashboardViewModel
            {
                PlantName = "Overall",
                SearchDate = date,
                Period = period,
                NGPulling = await ngPullingQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(),
                NGPreparation = await ngPreparationQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(),
                TotalNGPulling = totalNGPulling,
                TotalNGPreparation = totalNGPreparation
            };

            return View(viewModel);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Pulling", "Leader", "User", "Preparation")]
        public async Task<IActionResult> LogNG([FromBody] NGLogRequest req)
        {
            // Fallback endpoint — seharusnya tidak dipakai lagi (masing-masing controller punya LogNG sendiri)
            if (string.IsNullOrWhiteSpace(req?.Tag)) return Json(new { ok = false });
            var createdBy = HttpContext.Session.GetString("FullName") 
                         ?? HttpContext.Session.GetString("Username") 
                         ?? "Operator";
            var rec = new ScanNGLog
            {
                Module = req.Module ?? "Unknown",
                Tag = req.Tag.Trim(),
                Label = req.Label?.Trim() ?? "",
                Kanban = req.Kanban?.Trim() ?? "",
                Reason = req.Reason?.Trim() ?? "",
                CreatedBy = createdBy,
                CreatedDate = DateTime.Now
            };
            _context.ScanNGLogs.Add(rec);
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
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

        public IActionResult Trend(string period = "Month", string mode = "Activity")
        {
            return RedirectToAction("TrendCriticalStock");
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

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant, DateTime? searchDate = null, string period = "Day", int pageNumber = 1, string status = "All")
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
            //    EXCLUDE Mismatch records — they are log-only and don't affect stock
            var pullingQueryAll = _context.PullingRecords.AsNoTracking().Include(r => r.Item)
                .Where(r => r.Remark != "Mismatch").AsQueryable();
            if (plant != "Overall") pullingQueryAll = pullingQueryAll.Where(r => r.Plant == plant);
            
            // 2. Preparation (STOCK CALCULATION): Use ALL TIME data to ensure accurate stock balance
            //    We must deduct ALL preparations that have ever happened, not just those in the selected period.
            //    EXCLUDE Mismatch records — they are log-only and don't affect stock
            var preparationQueryAll = _context.PreparationRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch").AsQueryable();
            if (plant != "Overall") preparationQueryAll = preparationQueryAll.Where(r => r.Plant == plant);

            // 3. Preparation & Pulling (DISPLAY/ACTIVITY): Filter by date for the "Recent Activity" list and "Counts"
            //    NOTE: Display includes ALL records (Match + Mismatch) so users can see the full transaction log
            var pullingQueryDisplay = _context.PullingRecords.AsNoTracking().Include(r => r.Item).AsQueryable();
            if (plant != "Overall") pullingQueryDisplay = pullingQueryDisplay.Where(r => r.Plant == plant);
            var preparationQueryDisplay = _context.PreparationRecords.AsNoTracking().AsQueryable();
            if (plant != "Overall") preparationQueryDisplay = preparationQueryDisplay.Where(r => r.Plant == plant);

            var pullingQueryInRange = pullingQueryDisplay.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);
            var preparationQueryFiltered = preparationQueryDisplay.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);

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

                // Prioritas 1: Cari Pulling dengan Tag + Label SAMA PERSIS (FIFO normal scan)
                // Batasan waktu: Pulling harus ada sebelum Preparation (dengan toleransi 10 detik)
                var match = allPullingPotential.FirstOrDefault(p => 
                    !consumedPullingIds.Contains(p.PullingId) && 
                    (p.Tag ?? "").Trim().ToUpper() == prepTag && 
                    (p.Label ?? "").Trim().ToUpper() == prepLabel && 
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                // Prioritas 2: Fallback untuk Manual Stock Adjustment
                // Manual Adjust menyimpan Label = VIN + "LB" (misal: NA1490LB),
                // sedangkan label scan preparation bisa berupa NA1490LB12312 (ada nomor seri).
                // Tidak ada batasan waktu karena Manual Adjust bisa dibuat kapan saja sebelum preparation.
                if (match == null)
                {
                    match = allPullingPotential.FirstOrDefault(p =>
                        !consumedPullingIds.Contains(p.PullingId) &&
                        (p.Tag ?? "").Trim().ToUpper() == prepTag);
                }

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

            // --- COUNT FOR INDICATORS: Hitung per baris label (sama seperti tampilan tabel) ---
            // Sehingga angka Shortage / Normal / Over konsisten dengan jumlah baris yang tampil saat filter diklik
            var groupedForCount = inStockPieces.GroupBy(p => new { p.ItemId, Label = (p.Label ?? "").Trim().ToUpper() });
            int shortageCount = 0, normalCount = 0, overCount = 0;
            foreach (var grp in groupedForCount)
            {
                var sid = grp.First().ItemId;
                if (!sid.HasValue) continue;
                var rowStatus = itemStatuses.GetValueOrDefault(sid.Value, "None");
                if (rowStatus == "Shortage") shortageCount++;
                else if (rowStatus == "Normal") normalCount++;
                else if (rowStatus == "Over") overCount++;
            }
            // ------------------------------------------------------------------

            var stockDetails = new List<StockItemDetail>();

            // GROUP BY LABEL: Agar tampilan di dashboard digabung per Label
            var groupedByLabel = inStockPieces.GroupBy(p => new { p.ItemId, Label = (p.Label ?? "").Trim().ToUpper() });

            foreach (var group in groupedByLabel)
            {
                var latestPiece = group.OrderByDescending(p => p.CreatedDate).First();
                var itemStatus = latestPiece.ItemId.HasValue ? itemStatuses.GetValueOrDefault(latestPiece.ItemId.Value, "None") : "None";
                
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
                    Status = itemStatus,
                    LastActivityDate = latestPiece.CreatedDate,
                    IsManualAdjust = latestPiece.IsManualAdjust,
                    AdjustNote = latestPiece.AdjustNote
                });
            }
            
            // Sort by LastActivityDate DESC (Newest Scan first) -> Then stability sorts
            stockDetails = stockDetails
                .OrderByDescending(s => s.LastActivityDate)
                .ThenBy(s => s.Plant)
                .ThenBy(s => s.Location)
                .ToList();

            // Filter by status if specified
            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                stockDetails = stockDetails.Where(s => s.Status == status).ToList();
            }

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
                { "period", period },
                { "status", status }
            };
            ViewBag.CurrentStatus = status;

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
            var pullings = await _context.PullingRecords.Where(p => p.CreatedDate <= end && p.Remark != "Mismatch").OrderBy(p => p.CreatedDate).ToListAsync();
            var preps = await _context.PreparationRecords.Where(p => p.CreatedDate <= end && p.Remark != "Mismatch").OrderBy(p => p.CreatedDate).ToListAsync();

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
                    // Prioritas 1: match Tag + Label persis (FIFO normal scan)
                    var match = availablePullings.FirstOrDefault(p => 
                        (p.Tag ?? "").Trim().ToUpper() == tag && (p.Label ?? "").Trim().ToUpper() == lbl && p.CreatedDate <= prep.CreatedDate.AddSeconds(5));
                    // Prioritas 2: match Tag saja tanpa batasan waktu (untuk Manual Adjust — label bisa berbeda)
                    if (match == null)
                        match = availablePullings.FirstOrDefault(p => 
                            (p.Tag ?? "").Trim().ToUpper() == tag);
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
                .Select(s => s.Plant)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();

            // Ambil semua snapshot (otomatis & manual) dalam range tanggal
            // Untuk tiap hari, jika ada otomatis → pakai otomatis. Jika hanya manual → pakai manual.
            var allSnapshots = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date >= startDate
                         && s.SnapshotDate.Date <= endDate)
                .ToListAsync();

            // Prioritas per hari per item: auto > manual, jika sama type → ambil yang TERBARU
            var snapshots = allSnapshots
                .GroupBy(s => new { s.SnapshotDate.Date, s.ItemCode })
                .Select(g => g.OrderBy(x => x.IsManual ? 1 : 0)   // auto dulu
                              .ThenByDescending(x => x.CreatedAt)  // terbaru dulu
                              .First())
                .ToList();

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

                // Top item paling sering kritis (<1D dalam periode ini)
                var criticalItems = ps
                    .Where(s => s.StockLevel == "<1D")
                    .GroupBy(s => new { s.ItemCode, s.ItemName })
                    .Select(g => {
                        var latest = ps.Where(x => x.ItemCode == g.Key.ItemCode)
                                      .OrderByDescending(x => x.SnapshotDate)
                                      .First();
                        var vinCode = _context.Items
                                      .Where(it => it.ItemCode == g.Key.ItemCode)
                                      .Select(it => it.VIN)
                                      .FirstOrDefault() ?? g.Key.ItemCode;
                        return new
                        {
                            g.Key.ItemCode,
                            g.Key.ItemName,
                            VIN = vinCode,
                            CriticalDays = g.Select(x => x.SnapshotDate.Date).Distinct().Count(),
                            CurrentStockLevel = (double)latest.DaysCoverage
                        };
                    })
                    .OrderByDescending(x => x.CriticalDays)
                    .Take(50)
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

            // Summary hari ini — per level kategori sesuai request user
            var todayAll = snapshots.Where(s => s.SnapshotDate.Date == endDate).ToList();
            ViewBag.ShortageCount = todayAll.Count(s => s.StockLevel == "<1D" || s.StockLevel == "<1.5D");
            ViewBag.NormalCount   = todayAll.Count(s => s.StockLevel == "1.5-2D");
            ViewBag.OverCount     = todayAll.Count(s => s.StockLevel == "2-3D" || s.StockLevel == ">3D");
            ViewBag.TodayTotal    = todayAll.Count;

            // Backward-compat alias untuk grafik jika diperlukan
            ViewBag.TodayBelow1D   = todayAll.Count(s => s.StockLevel == "<1D");
            ViewBag.TodayBelow1_5D = ViewBag.ShortageCount;
            ViewBag.LastSnapshot   = await _context.StockSnapshots
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => (DateTime?)s.CreatedAt)
                .FirstOrDefaultAsync();

            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            ViewBag.AllPlants      = allPlants;
            ViewBag.SelectedLevel  = level;
            ViewBag.SelectedPeriod = period;
            ViewBag.PlantBreakdown = JsonSerializer.Serialize(plantBreakdown, jsonOpts);
            ViewBag.DateLabels     = JsonSerializer.Serialize(dateRange.Select(d => d.ToString("dd/MM")).ToList());
            ViewBag.FullDates      = JsonSerializer.Serialize(dateRange.Select(d => d.ToString("yyyy-MM-dd")).ToList());

            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetCriticalItemsByDay(DateTime date, string level = "1.5D")
        {
            var snapshots = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date == date.Date)
                .ToListAsync();

            // Filter data yang sama (auto vs manual)
            var daySnaps = snapshots
                .GroupBy(s => s.ItemCode)
                .Select(g => g.OrderBy(x => x.IsManual ? 1 : 0)
                              .ThenByDescending(x => x.CreatedAt)
                              .First())
                .ToList();

            var plants = daySnaps.Select(s => s.Plant).Distinct().OrderBy(p => p).ToList();

            // Pre-load VIN map untuk item yang ada di snapshot hari ini
            var itemCodes = daySnaps.Select(s => s.ItemCode).Distinct().ToList();
            var vinMap = await _context.Items
                .Where(it => itemCodes.Contains(it.ItemCode))
                .Select(it => new { it.ItemCode, it.VIN })
                .ToDictionaryAsync(it => it.ItemCode, it => it.VIN ?? it.ItemCode);

            var result = plants.Select(p => {
                var ps = daySnaps.Where(s => s.Plant == p).ToList();
                
                // Filter items based on selected level
                var items = ps.Where(s => {
                    if (level == "1D") return s.StockLevel == "<1D";
                    return s.StockLevel == "<1D" || s.StockLevel == "<1.5D";
                })
                .Select(s => new {
                    ItemCode = s.ItemCode,
                    ItemName = s.ItemName,
                    VIN = vinMap.TryGetValue(s.ItemCode, out var v) ? v : s.ItemCode,
                    CurrentStockLevel = (double)s.DaysCoverage
                })
                .OrderBy(s => s.CurrentStockLevel)
                .ToList();

                return new {
                    Plant = p,
                    Items = items,
                    Count = items.Count
                };
            }).ToList();

            return Json(result);
        }

        /// <summary>Manual trigger snapshot — hanya Admin</summary>
        [HttpPost]
        [AuthorizeAdmin]
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

        // ── Clear FG Data (Pulling + Preparation) ──────────────────────
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> ClearFGData()
        {
            try
            {
                var pullingCount   = await _context.PullingRecords.CountAsync();
                var snapshotCount  = await _context.StockSnapshots.CountAsync();

                // Clear Data Dashboard FG: hanya PullingRecords dan StockSnapshots
                _context.PullingRecords.RemoveRange(_context.PullingRecords);
                _context.StockSnapshots.RemoveRange(_context.StockSnapshots);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Berhasil menghapus {pullingCount} data Pulling dan {snapshotCount} data Stock Snapshot. Data Preparation tidak terpengaruh." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Gagal: " + ex.Message });
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

    public class NGLogRequest
    {
        public string Module { get; set; } = "Preparation";
        public string? Tag { get; set; }
        public string? Label { get; set; }
        public string? Kanban { get; set; }
        public string? Reason { get; set; }
    }
}
