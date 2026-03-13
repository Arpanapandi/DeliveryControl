using DeliveryControl.Data;
using DeliveryControl.Helpers;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Menyinkronisasi PreparationRecord pending (ScheduleId = null) ke DeliverySchedule yang sesuai.
    /// Logika: iterasi dari sisi pending records → cari jadwal FIFO yang cocok (sama seperti LookupSchedule).
    /// </summary>
    public class PreparationSyncService
    {
        private readonly ApplicationDbContext _context;

        public PreparationSyncService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Sync semua pending PreparationRecords (ScheduleId == null) ke jadwal yang tersedia.
        /// Dipanggil setelah ImportExcel, BulkCreate, atau manual trigger.
        /// Return jumlah records yang berhasil ditautkan ke jadwal.
        /// </summary>
        public async Task<int> SyncAllPendingAsync()
        {
            var today = DateTime.Today;

            // Load semua pending records FIFO
            var pendingRecords = await _context.PreparationRecords
                .Where(p => p.ScheduleId == null)
                .OrderBy(p => p.CreatedDate)
                .ToListAsync();

            if (!pendingRecords.Any()) return 0;

            // Load semua Items untuk matching Tag → ItemId
            var allItems = await _context.Items.ToListAsync();

            int synced = 0;

            foreach (var rec in pendingRecords)
            {
                string normalizedTag = VinHelper.Normalize(rec.Tag);

                // Cari item yang cocok dengan Tag (VIN/ItemCode)
                var item = allItems.FirstOrDefault(i =>
                    VinHelper.Normalize(i.VIN) == normalizedTag ||
                    VinHelper.IsMatch(i.VIN, rec.Tag) ||
                    (!string.IsNullOrEmpty(i.ItemCode) && VinHelper.Normalize(i.ItemCode) == normalizedTag));

                if (item == null) continue;

                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;

                // Cari jadwal FIFO — sama persis dengan logika Save di PreparationWorkflowController
                var schedule = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress") &&
                                 s.ScheduledDate.Date >= today &&
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity))
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .FirstOrDefaultAsync();

                if (schedule == null) continue;

                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                if (dItem == null) continue;

                // Assign ke jadwal ini
                rec.ScheduleId = schedule.ScheduleId;
                dItem.ActualQuantity = (dItem.ActualQuantity ?? 0) + qpc;
                if (dItem.ActualQuantity >= dItem.Quantity) dItem.IsCompleted = true;

                schedule.TotalActualQuantity += qpc;
                if (schedule.Status == "Scheduled") schedule.Status = "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                // Jika semua item schedule selesai
                if (schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    schedule.PreparationStatus = "Prepared";
                    if (!schedule.ReadyToDockTime.HasValue)
                        schedule.ReadyToDockTime = DateTime.Now;
                }

                synced++;

                // Simpan per-record agar iterasi berikutnya melihat ActualQuantity yang sudah update
                await _context.SaveChangesAsync();
            }

            return synced;
        }

        /// <summary>
        /// Wrapper untuk ImportExcel/BulkCreate — selalu jalankan SyncAllPendingAsync.
        /// </summary>
        public Task<int> SyncPendingForSchedulesAsync(IEnumerable<int> scheduleIds)
            => SyncAllPendingAsync();

        /// <summary>
        /// Hitung berapa banyak pending records yang belum punya ScheduleId.
        /// </summary>
        public async Task<int> CountPendingAsync()
            => await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null);
    }
}
