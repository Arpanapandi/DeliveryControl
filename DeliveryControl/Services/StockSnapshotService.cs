using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeliveryControl.Data;
using DeliveryControl.Models;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Background service yang otomatis menjalankan stock snapshot setiap hari jam 08:00.
    /// Data snapshot disimpan ke tabel StockSnapshots dan digunakan untuk grafik Trend Critical Stock.
    /// </summary>
    public class StockSnapshotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StockSnapshotService> _logger;

        // Cutoff time: 08:00 setiap hari
        private readonly TimeSpan _cutoffTime = new TimeSpan(8, 0, 0);

        public StockSnapshotService(
            IServiceScopeFactory scopeFactory,
            ILogger<StockSnapshotService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StockSnapshotService started. Cutoff: 08:00 setiap hari.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var todayCutoff = now.Date.Add(_cutoffTime);

                // Jadwal run berikutnya
                var nextRun = now < todayCutoff
                    ? todayCutoff               // belum jam 08 hari ini
                    : todayCutoff.AddDays(1);   // jam 08 besok

                var delay = nextRun - now;
                _logger.LogInformation(
                    "Next stock snapshot: {Next:dd/MM/yyyy HH:mm} (dalam {Min:F0} menit)",
                    nextRun, delay.TotalMinutes);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (!stoppingToken.IsCancellationRequested)
                    await TakeSnapshotAsync(isManual: false);
            }
        }

        /// <summary>
        /// Ambil snapshot stock saat ini untuk semua item.
        /// </summary>
        /// <param name="isManual">true = dipicu manual, false = auto jam 08:00</param>
        public async Task<(int itemCount, string message)> TakeSnapshotAsync(bool isManual = false)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var snapshotDate = DateTime.Today;
                var snapshotTime = isManual ? DateTime.Now.TimeOfDay : _cutoffTime;

                // Hindari duplikat snapshot otomatis di hari yang sama
                if (!isManual)
                {
                    var exists = await context.StockSnapshots
                        .AnyAsync(s => s.SnapshotDate.Date == snapshotDate && !s.IsManual);
                    if (exists)
                    {
                        _logger.LogWarning("Auto-snapshot hari ini sudah ada. Skip.");
                        return (0, "Snapshot otomatis hari ini sudah ada.");
                    }
                }

                // ── Hitung stock tiap item (logika sama dengan GetStockViewModel) ──
                // Ambil semua pulling records & preparation records
                var pullings = await context.PullingRecords
                    .Include(p => p.Item)
                    .ToListAsync();

                var preps = await context.PreparationRecords
                    .OrderBy(p => p.CreatedDate)
                    .ToListAsync();

                // Simulasikan stock: pulling masuk dikurangi yang sudah keluar (prep/delivery)
                // Gunakan Tag+Label matching (sesuai logika existing StockController)
                var availablePullings = new List<PullingRecord>(pullings.OrderBy(p => p.CreatedDate));

                // Kurangi item yang sudah di-prepare (keluar dari stock)
                foreach (var prep in preps.OrderBy(p => p.CreatedDate))
                {
                    var tag = (prep.Tag ?? "").Trim().ToUpper();
                    var lbl = (prep.Label ?? "").Trim().ToUpper();
                    var match = availablePullings.FirstOrDefault(p =>
                        (p.Tag ?? "").Trim().ToUpper() == tag &&
                        (p.Label ?? "").Trim().ToUpper() == lbl &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(5));
                    if (match != null)
                        availablePullings.Remove(match);
                }

                // Group per item
                var stockByItem = availablePullings
                    .Where(p => p.ItemId.HasValue)
                    .GroupBy(p => p.ItemId!.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                var items = await context.Items
                    .Where(i => i.IsActive)
                    .ToListAsync();

                var snapshots = new List<StockSnapshot>();

                foreach (var item in items)
                {
                    var stockQty = stockByItem.GetValueOrDefault(item.ItemId, 0);
                    var rackMin  = item.RackMin ?? 5;

                    decimal daysCoverage = rackMin > 0
                        ? Math.Round((decimal)stockQty / rackMin, 4)
                        : 0;

                    var level = daysCoverage switch
                    {
                        < 1.0m  => "<1D",
                        < 1.5m  => "<1.5D",
                        < 2.0m  => "1.5-2D",
                        < 3.0m  => "2-3D",
                        _       => ">3D"
                    };

                    // Ambil Plant dari pulling record terbaru item ini
                    var plant = availablePullings
                        .LastOrDefault(p => p.ItemId == item.ItemId)?.Plant
                        ?? item.Plant
                        ?? "Unknown";

                    snapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = snapshotDate,
                        SnapshotTime = snapshotTime,
                        ItemCode     = item.VIN ?? item.ItemCode,
                        ItemName     = item.ItemName,
                        Plant        = plant,
                        StockQty     = stockQty,
                        DaysCoverage = daysCoverage,
                        StockLevel   = level,
                        IsManual     = isManual,
                        CreatedAt    = DateTime.Now,
                    });
                }

                await context.StockSnapshots.AddRangeAsync(snapshots);
                await context.SaveChangesAsync();

                var msg = $"Snapshot selesai: {snapshots.Count} item " +
                          $"({(isManual ? "Manual" : "Auto jam 08:00")}) " +
                          $"pada {DateTime.Now:dd/MM/yyyy HH:mm}";
                _logger.LogInformation(msg);
                return (snapshots.Count, msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saat mengambil stock snapshot");
                return (0, $"Error: {ex.Message}");
            }
        }
    }
}
