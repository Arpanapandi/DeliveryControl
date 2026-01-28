using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DeliveryControl.Controllers
{
    public class FgMappingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FgMappingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: FgMappings
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.FgMappings.Include(f => f.Item);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: FgMappings/Create
        public IActionResult Create()
        {
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View();
        }

        // POST: FgMappings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FgMapping fgMapping, string PartNumber)
        {
            if (string.IsNullOrEmpty(PartNumber))
            {
                ModelState.AddModelError("PartNumber", "Part Number wajib diisi");
            }
            else
            {
                var item = await _context.Items.FirstOrDefaultAsync(i => i.ItemCode == PartNumber);
                if (item == null)
                {
                    ModelState.AddModelError("PartNumber", "Part Number tidak ditemukan di master item");
                }
                else
                {
                    fgMapping.ItemId = item.ItemId;
                }
            }

            if (ModelState.IsValid)
            {
                // Check duplicate check
                if (await _context.FgMappings.AnyAsync(f => f.ItemId == fgMapping.ItemId))
                {
                    ModelState.AddModelError("", "Mapping untuk Part Number ini sudah ada");
                }
                else
                {
                    fgMapping.CreatedDate = DateTime.Now;
                    fgMapping.CreatedBy = HttpContext.Session.GetString("FullName") ?? "System";
                    
                    // Sync to Item
                    var item = await _context.Items.FindAsync(fgMapping.ItemId);
                    if (item != null)
                    {
                        item.MinStock = fgMapping.MinStock;
                        item.MaxStock = fgMapping.MaxStock;
                    }

                    _context.Add(fgMapping);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
            }
            
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            ViewBag.PartNumber = PartNumber;
            return View(fgMapping);
        }

        // GET: FgMappings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var fgMapping = await _context.FgMappings.Include(f => f.Item).FirstOrDefaultAsync(m => m.FgMappingId == id);
            if (fgMapping == null) return NotFound();

            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            ViewBag.PartNumber = fgMapping.Item?.ItemCode;
            return View(fgMapping);
        }

        // POST: FgMappings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FgMapping fgMapping)
        {
            if (id != fgMapping.FgMappingId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    fgMapping.UpdatedDate = DateTime.Now;
                    fgMapping.UpdatedBy = HttpContext.Session.GetString("FullName") ?? "System";
                    
                    // Maintain creation metadata
                    var existing = await _context.FgMappings.AsNoTracking().FirstOrDefaultAsync(m => m.FgMappingId == id);
                    if (existing != null)
                    {
                        fgMapping.CreatedDate = existing.CreatedDate;
                        fgMapping.CreatedBy = existing.CreatedBy;
                        fgMapping.ItemId = existing.ItemId; // Item cannot be changed after creation

                        // Sync to Item
                        var item = await _context.Items.FindAsync(fgMapping.ItemId);
                        if (item != null)
                        {
                            item.MinStock = fgMapping.MinStock;
                            item.MaxStock = fgMapping.MaxStock;
                        }
                    }

                    _context.Update(fgMapping);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!FgMappingExists(fgMapping.FgMappingId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View(fgMapping);
        }

        // GET: FgMappings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var fgMapping = await _context.FgMappings
                .Include(f => f.Item)
                .FirstOrDefaultAsync(m => m.FgMappingId == id);
            if (fgMapping == null) return NotFound();

            return View(fgMapping);
        }

        // POST: FgMappings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var fgMapping = await _context.FgMappings.FindAsync(id);
            if (fgMapping != null)
            {
                _context.FgMappings.Remove(fgMapping);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool FgMappingExists(int id)
        {
            return _context.FgMappings.Any(e => e.FgMappingId == id);
        }
        
        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string partNumber)
        {
            var item = await _context.Items.FirstOrDefaultAsync(i => i.ItemCode == partNumber);
            if (item != null)
            {
                return Json(new { success = true, itemName = item.ItemName });
            }
            return Json(new { success = false });
        }
    }
}
