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
        private readonly DeliveryControl.Services.ActivityLogService _activityLogService;

        public PullingController(ApplicationDbContext context, 
            Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> hubContext,
            DeliveryControl.Services.ActivityLogService activityLogService)
        {
            _context = context;
            _hubContext = hubContext;
            _activityLogService = activityLogService;
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

            // Lookup by VIN (Tag input) - as requested by user
            // User inputs Tag which maps to VIN
            var item = await _context.Items
                .FirstOrDefaultAsync(i => i.VIN == tag);

            if (item == null)
            {
                return Json(new { success = false, message = "VIN tidak ditemukan di Master Data" });
            }

            // Calculate current stock for status
            var inStockCount = await _context.PullingRecords
                .CountAsync(r => r.ItemId == item.ItemId && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

            string status = "Normal";
            if (inStockCount < (item.RackMin ?? 5)) status = "Shortage";
            else if (inStockCount > (item.RackMax ?? 20)) status = "Over";

            return Json(new { 
                success = true, 
                // Return ALL master item properties for dashboard display
                itemId = item.ItemId,
                itemCode = item.ItemCode,
                itemName = item.ItemName,
                description = item.Description ?? "-",
                unit = item.Unit ?? "-",
                
                // Location & Mapping Info
                plant = item.Plant ?? "-",
                rack = item.Rack ?? "-",
                noRack = item.NoRack,
                customer = item.Customer ?? "-",
                category = item.Category ?? "-",
                vin = item.VIN ?? "-",
                qtyLot = item.QtyLot ?? 0,
                
                // Limits
                rackMin = item.RackMin ?? 0,
                rop = item.ROP ?? 0,
                rackMax = item.RackMax ?? 0,
                
                // Status
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
                    return Json(new { success = false, message = "Input TAG / VIN kosong!" });
                }

                // 1. Identification & Item Lookup
                // Note: Label duplicate check removed as per user request to allow redundant scans.

                // 2. Lookup Item Master - Using VIN as Tag
                Item? item = await _context.Items
                    .FirstOrDefaultAsync(i => i.VIN == record.Tag);
                
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
                    return Json(new { success = false, message = "VIN tidak ditemukan di Master Data" });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";
                
                _context.PullingRecords.Add(record);
                await _context.SaveChangesAsync();

                // Log Activity per Plant as requested
                await _activityLogService.LogActivity(
                    module: "Pulling",
                    action: "Create",
                    entityName: item.ItemName + " (" + item.VIN + ")",
                    entityId: record.PullingId,
                    description: $"Plant: {record.Plant}, Rack: {record.Rack}, Label: {record.Label}",
                    performedBy: record.CreatedBy
                );

                // Notify all clients via SignalR
                await _hubContext.Clients.All.SendAsync("updateStock");
                
                // Get updated stock for feedback
                var updatedStockCount = await _context.PullingRecords
                    .CountAsync(r => r.ItemId == item.ItemId && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

                return Json(new { success = true, message = $"Data {item.ItemName} berhasil disimpan!", newStock = updatedStockCount });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }
    }
}
