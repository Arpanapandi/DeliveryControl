using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Controllers
{
    public class PoolingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> _hubContext;

        public PoolingController(ApplicationDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PoolingRecord record)
        {
            if (ModelState.IsValid)
            {
                // Trim strings
                record.Tag = (record.Tag ?? "").Trim();
                record.Label = (record.Label ?? "").Trim();

                // 1. Duplicate Check (Tag + Label)
                bool exist = await _context.PoolingRecords
                    .AnyAsync(r => r.Tag == record.Tag && r.Label == record.Label);
                
                if (exist)
                {
                    return Json(new { success = false, message = "Data DUPLIKAT! Barang ini sudah pernah di-scan pooling." });
                }

                // 2. Lookup Item Master (Primary key of automation)
                // Match by Tag (ItemCode). Label is used for extra validation if present.
                var item = await _context.Items
                    .FirstOrDefaultAsync(i => i.ItemCode == record.Tag);
                
                if (item != null)
                {
                    record.ItemId = item.ItemId;
                    
                    // Force copy location from Master Item
                    // This ensures the operator only needs to input Tag/Label
                    record.Plant = item.Plant ?? "Unknown";
                    record.Rack = item.Rack ?? "-";
                    record.Column = item.NoRack ?? 0;
                    
                    // If the master item has a specific Label snapshot, it should match the scanned label
                    // but we allow scanning any label if master snapshot is empty.
                }
                else
                {
                    // Item not found in master
                    return Json(new { success = false, message = $"Item '{record.Tag}' tidak ditemukan di Master Data. Silakan hubungi Admin." });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";
                
                _context.PoolingRecords.Add(record);
                await _context.SaveChangesAsync();

                // Notify all clients via SignalR
                await _hubContext.Clients.All.SendAsync("UpdateStock");
                
                return Json(new { success = true, message = "Data berhasil disimpan!" });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }
    }
}
