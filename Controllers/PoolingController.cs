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
