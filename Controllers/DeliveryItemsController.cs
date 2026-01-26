using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;

namespace DeliveryControl.Controllers
{
    public class DeliveryItemsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DeliveryItemsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: DeliveryItems/Index/5 (by ScheduleId)
        public async Task<IActionResult> Index(int? scheduleId)
        {
            if (scheduleId == null)
            {
                return RedirectToAction("Index", "DeliverySchedules");
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
            {
                return NotFound();
            }

            ViewBag.Schedule = schedule;

            var items = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.Item)
                .Where(di => di.ScheduleId == scheduleId)
                .ToListAsync();

            return View(items);
        }

        // GET: DeliveryItems/Create
        public async Task<IActionResult> Create(int? scheduleId)
        {
            if (scheduleId == null)
            {
                return RedirectToAction("Index", "DeliverySchedules");
            }

            var schedule = await _context.DeliverySchedules.FindAsync(scheduleId);
            if (schedule == null)
            {
                return NotFound();
            }

            ViewBag.Schedule = schedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName");
            
            var model = new DeliveryItem
            {
                ScheduleId = scheduleId.Value
            };

            return View(model);
        }

        // POST: DeliveryItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("DeliveryItemId,ScheduleId,ItemId,Quantity,ActualQuantity,Unit,TotalWeight,TotalVolume,Notes,IsCompleted")] DeliveryItem deliveryItem)
        {
            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedule");
            ModelState.Remove("Item");

            if (ModelState.IsValid)
            {
                deliveryItem.CreatedDate = DateTime.Now;
                _context.Add(deliveryItem);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item delivery berhasil ditambahkan!";
                return RedirectToAction(nameof(Index), new { scheduleId = deliveryItem.ScheduleId });
            }
            
            var schedule = await _context.DeliverySchedules.FindAsync(deliveryItem.ScheduleId);
            ViewBag.Schedule = schedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // GET: DeliveryItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var deliveryItem = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.DeliverySchedule)
                .FirstOrDefaultAsync(di => di.DeliveryItemId == id);

            if (deliveryItem == null)
            {
                return NotFound();
            }

            ViewBag.Schedule = deliveryItem.DeliverySchedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // POST: DeliveryItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("DeliveryItemId,ScheduleId,ItemId,Quantity,ActualQuantity,Unit,TotalWeight,TotalVolume,Notes,IsCompleted,CreatedDate")] DeliveryItem deliveryItem)
        {
            if (id != deliveryItem.DeliveryItemId)
            {
                return NotFound();
            }

            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedule");
            ModelState.Remove("Item");

            if (ModelState.IsValid)
            {
                try
                {
                    deliveryItem.UpdatedDate = DateTime.Now;
                    _context.Update(deliveryItem);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Item delivery berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DeliveryItemExists(deliveryItem.DeliveryItemId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index), new { scheduleId = deliveryItem.ScheduleId });
            }

            var schedule = await _context.DeliverySchedules.FindAsync(deliveryItem.ScheduleId);
            ViewBag.Schedule = schedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // GET: DeliveryItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var deliveryItem = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.DeliverySchedule)
                .Include(di => di.Item)
                .FirstOrDefaultAsync(m => m.DeliveryItemId == id);

            if (deliveryItem == null)
            {
                return NotFound();
            }

            return View(deliveryItem);
        }

        // POST: DeliveryItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deliveryItem = await _context.DeliveryItems.FindAsync(id);
            int scheduleId = 0;
            
            if (deliveryItem != null)
            {
                scheduleId = deliveryItem.ScheduleId;
                _context.DeliveryItems.Remove(deliveryItem);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item delivery berhasil dihapus!";
            }

            return RedirectToAction(nameof(Index), new { scheduleId });
        }

        private bool DeliveryItemExists(int id)
        {
            return _context.DeliveryItems.Any(e => e.DeliveryItemId == id);
        }
    }
}

