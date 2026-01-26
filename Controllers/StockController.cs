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

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant)
        {
            var today = DateTime.Today;

            var viewModel = new StockDashboardViewModel
            {
                PlantName = plant,
                RecentPooling = await _context.PoolingRecords
                    .Where(r => r.Plant == plant)
                    .OrderByDescending(r => r.CreatedDate)
                    .Take(10)
                    .ToListAsync(),
                RecentPreparation = await _context.PreparationRecords
                    .Where(r => r.Plant == plant)
                    .OrderByDescending(r => r.CreatedDate)
                    .Take(10)
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
