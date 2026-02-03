using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Controllers
{
    public class PullingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> _hubContext;

        public PullingController(ApplicationDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return Json(new { success = false });

            tag = tag.Trim();

            // Lookup by ItemName (LOKASI RACK/TAG) - as requested by user
            var item = await _context.Items
                .FirstOrDefaultAsync(i => i.ItemName == tag);

            if (item == null)
            {
                return Json(new { success = false, message = "Item (LOKASI RACK) tidak ditemukan." });
            }

            // Calculate current stock for status
            var inStockCount = await _context.PullingRecords
                .CountAsync(r => r.ItemId == item.ItemId && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

            string status = "Normal";
            if (inStockCount < (item.RackMin ?? 5)) status = "Shortage";
            else if (inStockCount > (item.RackMax ?? 20)) status = "Over";

            return Json(new { 
                success = true, 
                itemName = item.ItemName,
                plant = item.Plant ?? "Unknown",
                rack = item.Rack ?? "-",
                noRack = item.NoRack ?? 0,
                customer = item.Customer ?? "-",
                category = item.Category ?? "-",
                vin = item.VIN ?? "-",
                qtyLot = item.QtyLot ?? 0,
                rackMin = item.RackMin ?? 0,
                rop = item.ROP ?? 0,
                rackMax = item.RackMax ?? 0,
                status = status,
                currentStock = inStockCount
            });
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PullingRecord record)
        {
            if (ModelState.IsValid)
            {
                // Trim strings
                record.Tag = (record.Tag ?? "").Trim();
                record.Label = (record.Label ?? "").Trim();

                if (string.IsNullOrEmpty(record.Tag))
                {
                    return Json(new { success = false, message = "Input Tag/VIN tidak boleh kosong." });
                }

                // 1. Strict Unique Label Check (Label must be unique across all Pulling transactions)
                bool exist = await _context.PullingRecords
                    .AnyAsync(r => r.Label == record.Label);
                
                if (exist)
                {
                    return Json(new { success = false, message = "LABEL DUPLIKAT! Label ini sudah pernah digunakan." });
                }

                // 2. Lookup Item Master - Using ItemName (LOKASI RACK) as Tag
                Item? item = await _context.Items
                    .FirstOrDefaultAsync(i => i.ItemName == record.Tag);
                
                if (item != null)
                {
                    record.ItemId = item.ItemId;
                    
                    // Force copy details from Master Item
                    record.Plant = item.Plant ?? "Unknown";
                    record.Rack = item.Rack ?? "-";
                    record.Column = item.NoRack ?? 0;
                }
                else
                {
                    // Item not found in master
                    return Json(new { success = false, message = $"Tag (LOKASI RACK) '{record.Tag}' tidak ditemukan di Master Data." });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";
                
                _context.PullingRecords.Add(record);
                await _context.SaveChangesAsync();

                // Notify all clients via SignalR
                await _hubContext.Clients.All.SendAsync("UpdateStock");
                
                return Json(new { success = true, message = $"Data {item.ItemName} berhasil disimpan!" });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }
    }
}
