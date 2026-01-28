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

                // 2. Auto-map ItemId and Location based on FgMapping
                var item = await _context.Items
                    .FirstOrDefaultAsync(i => i.ItemCode == record.Tag || i.ItemCode == record.Label);
                
                if (item != null)
                {
                    record.ItemId = item.ItemId;
                    
                    // Get FG Mapping for location automatic fill
                    var mapping = await _context.FgMappings.FirstOrDefaultAsync(m => m.ItemId == item.ItemId);
                    if (mapping != null)
                    {
                        record.Plant = mapping.Plant;
                        record.Rack = mapping.Rack;
                        record.Column = mapping.NoRack;
                    }
                    else
                    {
                        // Fallback if no mapping exists
                        record.Plant = record.Plant ?? "N/A";
                        record.Rack = record.Rack ?? "-";
                    }
                }
                else
                {
                    record.Plant = record.Plant ?? "N/A";
                    record.Rack = record.Rack ?? "-";
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
