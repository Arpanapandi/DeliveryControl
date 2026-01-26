using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Pengaturan aplikasi sederhana (misalnya jam cutoff AutoScheduler).
    /// </summary>
    [AuthorizeAdmin]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var setting = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "AutoSchedulerCutoffTime");

            var model = new AutoSchedulerSettingViewModel
            {
                CutoffTime = setting?.Value ?? "18:00"
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AutoSchedulerSettingViewModel model)
        {
            if (!TimeSpan.TryParse(model.CutoffTime, out _))
            {
                ModelState.AddModelError(nameof(model.CutoffTime), "Format waktu harus HH:mm, contoh 18:00");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoSchedulerCutoffTime");

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    Key = "AutoSchedulerCutoffTime",
                    Value = model.CutoffTime,
                    Description = "Jam cutoff auto generate jadwal (format HH:mm)"
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = model.CutoffTime;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Jam cutoff AutoScheduler disimpan: {model.CutoffTime}";
            return RedirectToAction(nameof(Index));
        }
    }
}


