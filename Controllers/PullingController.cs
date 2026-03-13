using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Helpers;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "Pulling", "User")]
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
            var role = HttpContext.Session.GetString("Role");
            var isAdmin = role == "Admin";
            var isUser = role == "User";
            ViewData["IsAdmin"] = isAdmin;
            ViewData["IsUser"]  = isUser;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return Json(new { success = false });

            tag = tag.Trim();

            // Lookup by VIN (Tag input) - as requested by user
            // User inputs Tag which maps to VIN
            var normalizedTag = VinHelper.Normalize(tag);
            var itemCandidates = await _context.Items
                .Where(i => i.VIN.Contains(normalizedTag))
                .ToListAsync();

            var item = itemCandidates.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, tag));

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
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Pulling")]
        public async Task<IActionResult> Save([FromBody] PullingRecord record)
        {
            if (ModelState.IsValid)
            {
                // Trim strings and remove visual X
                record.Tag = VinHelper.Normalize(record.Tag);
                record.Label = (record.Label ?? "").Trim();

                if (string.IsNullOrEmpty(record.Tag))
                {
                    return Json(new { success = false, message = "Input TAG / VIN kosong!" });
                }

                // 1. Identification & Item Lookup
                // Note: Label duplicate check removed as per user request to allow redundant scans.

                // 2. Lookup Item Master - Using VIN as Tag
                var normalizedSaveTag = VinHelper.Normalize(record.Tag);
                var normalizedLabel = VinHelper.Normalize(record.Label);

                bool labelMismatch = !normalizedLabel.Contains(normalizedSaveTag);

                var itemSaveCandidates = await _context.Items
                    .Where(i => i.VIN.Contains(normalizedSaveTag))
                    .ToListAsync();

                Item? item = itemSaveCandidates.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, record.Tag));
                
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
                if (labelMismatch)
                {
                    var ngLog = new ScanNGLog
                    {
                        Module = "Pulling",
                        Tag = record.Tag ?? "",
                        Label = record.Label ?? "",
                        Kanban = "",
                        Reason = $"Label tidak mengandung VIN: {VinHelper.Normalize(record.Tag)} (Server-Side Validation)",
                        CreatedBy = record.CreatedBy,
                        CreatedDate = DateTime.Now
                    };
                    _context.ScanNGLogs.Add(ngLog);
                    await _context.SaveChangesAsync();
                    await _hubContext.Clients.All.SendAsync("updateStock");
                    return Json(new { success = false, message = $"❌ LABEL TIDAK VALID! Label harus mengandung kode VIN: \"{VinHelper.Normalize(record.Tag)}\"." });
                }

                record.Remark = "Match";
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
                
                // Get updated stock for feedback (only count Match records)
                var updatedStockCount = await _context.PullingRecords
                    .CountAsync(r => r.ItemId == item.ItemId && r.Remark == "Match" && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label && p.Remark == "Match"));

                return Json(new { success = true, message = $"Data {item.ItemName} berhasil disimpan!", newStock = updatedStockCount, remark = record.Remark });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllItemsForAdjust()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "Admin")
                return Json(new { success = false });

            var items = await _context.Items
                .OrderBy(i => i.ItemName)
                .Select(i => new {
                    i.ItemId,
                    i.ItemCode,
                    i.ItemName,
                    i.VIN,
                    i.Plant,
                    i.Category,
                    i.Rack,
                    i.NoRack
                })
                .ToListAsync();

            return Json(new { success = true, items });
        }

        [HttpPost]
        public async Task<IActionResult> ManualAdjust([FromBody] ManualAdjustRequest request)
        {
            // Admin only
            var role = HttpContext.Session.GetString("Role");
            if (role != "Admin")
                return Json(new { success = false, message = "Hanya Admin yang dapat melakukan Manual Adjust." });

            // Validate inputs
            if (request.ItemId <= 0)
                return Json(new { success = false, message = "Item tidak valid." });
            if (request.Qty == 0)
                return Json(new { success = false, message = "Qty tidak boleh 0." });

            // Default note jika kosong
            if (string.IsNullOrWhiteSpace(request.Note))
                request.Note = "Manual Stock Adjustment";

            var item = await _context.Items.FindAsync(request.ItemId);
            if (item == null)
                return Json(new { success = false, message = "Item tidak ditemukan." });

            // Update Item master jika Rack berubah
            bool rackChanged = false;
            if (!string.IsNullOrWhiteSpace(request.Rack) && request.Rack != item.Rack)
            {
                item.Rack = request.Rack.Trim().ToUpper();
                rackChanged = true;
            }
            if (request.NoRack.HasValue && request.NoRack.Value != item.NoRack)
            {
                item.NoRack = request.NoRack.Value;
                rackChanged = true;
            }
            if (rackChanged)
            {
                _context.Items.Update(item);
                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "UpdateRack",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"Lokasi RAK diubah ke: {item.Rack}.{item.NoRack}",
                    performedBy: HttpContext.Session.GetString("FullName") ?? "Admin"
                );
            }

            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var now = DateTime.Now;

            if (request.Qty > 0)
            {
                // ADD: insert PullingRecords — all records in one batch share the same label
                // so they appear as a single grouped row in the dashboard (grouped by label)
                var tag = item.VIN ?? item.ItemCode;
                var batchLabel = (item.VIN ?? "") + "LB"; // Label = VIN + LB (e.g. NA1560LB)

                for (int i = 0; i < request.Qty; i++)
                {
                    var pulling = new PullingRecord
                    {
                        ItemId = item.ItemId,
                        Plant = item.Plant ?? "Unknown",
                        Rack = item.Rack ?? "-",
                        Column = item.NoRack ?? 0,
                        Tag = tag,
                        Label = batchLabel,
                        IsManualAdjust = true,
                        AdjustNote = request.Note.Trim(),
                        CreatedBy = createdBy,
                        CreatedDate = now
                    };
                    _context.PullingRecords.Add(pulling);
                }

                await _context.SaveChangesAsync();

                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "Adjust",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"TAMBAH +{request.Qty} pcs. Catatan: {request.Note}",
                    performedBy: createdBy
                );
            }
            else
            {
                // REDUCE: consume existing PullingRecords via PreparationRecords
                int reduceQty = Math.Abs(request.Qty);

                // Find available pulling records (not yet consumed)
                var available = await _context.PullingRecords
                    .Where(r => r.ItemId == item.ItemId &&
                                !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label))
                    .OrderBy(r => r.CreatedDate)
                    .Take(reduceQty)
                    .ToListAsync();

                if (available.Count < reduceQty)
                    return Json(new { success = false, message = $"Stok tidak cukup. Tersedia: {available.Count} pcs." });

                foreach (var pr in available)
                {
                    var prep = new PreparationRecord
                    {
                        Plant = "MADJUST",
                        Tag = pr.Tag,
                        Label = pr.Label,
                        Kanban = request.Note.Trim(),
                        ScheduleId = null,
                        CreatedBy = createdBy,
                        CreatedDate = now
                    };
                    _context.PreparationRecords.Add(prep);
                }

                await _context.SaveChangesAsync();

                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "Adjust",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"KURANGI -{reduceQty} pcs. Catatan: {request.Note}",
                    performedBy: createdBy
                );
            }

            // Broadcast SignalR
            await _hubContext.Clients.All.SendAsync("updateStock");

            // Return updated stock
            var newStock = await _context.PullingRecords
                .CountAsync(r => r.ItemId == item.ItemId &&
                                 !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

            string direction = request.Qty > 0 ? $"+{request.Qty}" : $"{request.Qty}";
            return Json(new { success = true, message = $"Manual Adjust berhasil: {direction} pcs untuk {item.ItemName}.", newStock });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LogNG([FromBody] PullingNGLogRequest req)
        {
            try
            {
                var createdBy = HttpContext.Session.GetString("FullName") 
                             ?? HttpContext.Session.GetString("Username") 
                             ?? "Operator";
                var rec = new DeliveryControl.Models.ScanNGLog
                {
                    Module = "Pulling",
                    Tag = req?.Tag?.Trim() ?? "",
                    Label = req?.Label?.Trim() ?? "",
                    Kanban = "",
                    Reason = req?.Reason?.Trim() ?? "Unknown Error",
                    CreatedBy = createdBy,
                    CreatedDate = DateTime.Now
                };
                _context.ScanNGLogs.Add(rec);
                await _context.SaveChangesAsync();

                // Broadcast ke dashboard Log Scan NG
                await _hubContext.Clients.All.SendAsync("updateStock");

                return Json(new { ok = true });
            }
            catch { return Json(new { ok = false }); }
        }
    }

    public class ManualAdjustRequest
    {
        public int ItemId { get; set; }
        public int Qty { get; set; }
        public string Note { get; set; } = string.Empty;
        public string? Rack { get; set; }
        public int? NoRack { get; set; }
    }

    public class PullingNGLogRequest
    {
        public string Module { get; set; } = "Pulling";
        public string? Tag { get; set; }
        public string? Label { get; set; }
        public string? Kanban { get; set; }
        public string? Reason { get; set; }
    }
}
