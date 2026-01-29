using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;

namespace DeliveryControl.Controllers
{
    public class ItemsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ItemsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Items
        public async Task<IActionResult> Index(string searchString, string category)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentCategory"] = category;

            var categories = await _context.Items
                .AsNoTracking()
                .Where(i => !string.IsNullOrEmpty(i.Category))
                .Select(i => i.Category)
                .Distinct()
                .ToListAsync();
            ViewData["Categories"] = categories;

            var items = _context.Items.AsNoTracking();

            if (!String.IsNullOrEmpty(searchString))
            {
                items = items.Where(i => i.ItemCode.Contains(searchString)
                                   || i.ItemName.Contains(searchString));
            }

            if (!String.IsNullOrEmpty(category))
            {
                items = items.Where(i => i.Category == category);
            }

            return View(await items.OrderBy(i => i.ItemCode).ToListAsync());
        }

        // GET: Items/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ItemId == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        public IActionResult Create()
        {
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,Plant,Rack,NoRack,QtyLot,RackMin,RackMax")] Item item)
        {
            if (ModelState.IsValid)
            {
                item.CreatedDate = DateTime.Now;
                _context.Add(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View(item);
        }

        // GET: Items/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemId == id);
            
            if (item == null)
            {
                return NotFound();
            }
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View(item);
        }

        // POST: Items/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,CreatedDate,Plant,Rack,NoRack,QtyLot,RackMin,RackMax")] Item item)
        {
            if (id != item.ItemId)
            {
                return NotFound();
            }

            // Remove navigation properties from validation
            ModelState.Remove("DeliveryItems");

            if (ModelState.IsValid)
            {
                try
                {
                    item.UpdatedDate = DateTime.Now;
                    _context.Update(item);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Item berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ItemExists(item.ItemId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Plants = new List<string> { "Molded", "Hose", "RVI" };
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = "ABCDEFGHI".Select(c => c.ToString()).ToList();
            return View(item);
        }


        // GET: Items/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ItemId == id);
            
            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        // POST: Items/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.Items
                .Include(i => i.DeliveryItems)
                .FirstOrDefaultAsync(i => i.ItemId == id);

            if (item != null)
            {
                // Cek apakah ada relasi dengan DeliveryItem
                if (item.DeliveryItems.Any())
                {
                    TempData["ErrorMessage"] = $"Tidak dapat menghapus item! Masih ada {item.DeliveryItems.Count} delivery item yang terkait. Hapus delivery item terlebih dahulu.";
                    return RedirectToAction(nameof(Index));
                }

                try
                {
                    _context.Items.Remove(item);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Item berhasil dihapus!";
                }
                catch (DbUpdateException ex)
                {
                    TempData["ErrorMessage"] = $"Error saat menghapus item: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ItemExists(int id)
        {
            return _context.Items.Any(e => e.ItemId == id);
        }

        // Bulk Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada item yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',').Select(int.Parse).ToList();
            var itemsToDelete = await _context.Items
                .Include(i => i.DeliveryItems)
                .Where(i => ids.Contains(i.ItemId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var item in itemsToDelete)
            {
                if (item.DeliveryItems.Any())
                {
                    errorMessages.Add($"{item.ItemCode}: Masih ada {item.DeliveryItems.Count} delivery item");
                    errorCount++;
                    continue;
                }

                try
                {
                    _context.Items.Remove(item);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{item.ItemCode}: {ex.Message}");
                    errorCount++;
                }
            }

            if (successCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {successCount} item!";
            }

            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = $"⚠️ {errorCount} item gagal dihapus: {string.Join(", ", errorMessages.Take(5))}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Item");

                // Header
                worksheet.Cell(1, 1).Value = "Kode Item (Tag)";
                worksheet.Cell(1, 2).Value = "Nama Item";
                worksheet.Cell(1, 3).Value = "Deskripsi";
                worksheet.Cell(1, 4).Value = "Unit";
                worksheet.Cell(1, 5).Value = "Plant";
                worksheet.Cell(1, 6).Value = "Rak";
                worksheet.Cell(1, 7).Value = "No Rak";
                worksheet.Cell(1, 8).Value = "Qty per Lot";
                worksheet.Cell(1, 9).Value = "QTY Min";
                worksheet.Cell(1, 10).Value = "QTY Max";
                worksheet.Cell(1, 11).Value = "Berat (KG)";
                worksheet.Cell(1, 12).Value = "Volume (M3)";

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, 12);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Contoh data (baris 2)
                worksheet.Cell(2, 1).Value = "ITM001";
                worksheet.Cell(2, 2).Value = "Steel Plate 10mm";
                worksheet.Cell(2, 3).Value = "Plat besi ukuran 10mm";
                worksheet.Cell(2, 4).Value = "PCS";
                worksheet.Cell(2, 5).Value = "Molded";
                worksheet.Cell(2, 6).Value = "A";
                worksheet.Cell(2, 7).Value = 1;
                worksheet.Cell(2, 8).Value = 100;
                worksheet.Cell(2, 9).Value = 10;
                worksheet.Cell(2, 10).Value = 200;
                worksheet.Cell(2, 11).Value = 25.5;
                worksheet.Cell(2, 12).Value = 0.05;

                // Contoh data 2 (baris 3)
                worksheet.Cell(3, 1).Value = "ITM002";
                worksheet.Cell(3, 2).Value = "Bolt M12";
                worksheet.Cell(3, 3).Value = "Baut ukuran M12";
                worksheet.Cell(3, 4).Value = "BOX";
                worksheet.Cell(3, 5).Value = "Hose";
                worksheet.Cell(3, 6).Value = "B";
                worksheet.Cell(3, 7).Value = 15;
                worksheet.Cell(3, 8).Value = 500;
                worksheet.Cell(3, 9).Value = 50;
                worksheet.Cell(3, 10).Value = 1000;
                worksheet.Cell(3, 11).Value = 5.0;
                worksheet.Cell(3, 12).Value = 0.01;

                // Catatan
                worksheet.Cell(5, 1).Value = "Catatan:";
                worksheet.Cell(6, 1).Value = "- Kode Item wajib diisi dan harus unique";
                worksheet.Cell(7, 1).Value = "- Nama Item wajib diisi";
                worksheet.Cell(8, 1).Value = "- Deskripsi, Unit, Kategori, Berat, dan Volume boleh kosong";
                worksheet.Cell(9, 1).Value = "- Berat dalam satuan kilogram (KG)";
                worksheet.Cell(10, 1).Value = "- Volume dalam satuan meter kubik (M3)";
                worksheet.Cell(11, 1).Value = "- Gunakan format angka desimal dengan titik (.) bukan koma";

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Item.xlsx");
                }
            }
        }

        // Import Excel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid! Silakan pilih file Excel terlebih dahulu.";
                return RedirectToAction(nameof(Index));
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToAction(nameof(Index));
            }

            var items = new List<Item>();
            var errorMessages = new List<string>();
            int successCount = 0;
            int errorCount = 0;

            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var rows = worksheet.RowsUsed().Skip(1); // Skip header

                        // Get existing item codes to check for duplicates
                        var existingItemCodes = await _context.Items
                            .Select(i => i.ItemCode.ToUpper())
                            .ToListAsync();

                        foreach (var row in rows)
                        {
                            try
                            {
                                // Column 1: Item Code (Tag)
                                // Column 2: Item Name
                                // Column 3: Description
                                // Column 4: Unit
                                // Column 5: Plant
                                // Column 6: Rack
                                // Column 7: No Rak
                                // Column 8: Qty per Lot
                                // Column 9: QTY Min
                                // Column 10: QTY Max
                                // Column 11: Berat
                                // Column 12: Volume
                                var itemCode = row.Cell(1).GetString().Trim();
                                var itemName = row.Cell(2).GetString().Trim();
                                var description = row.Cell(3).GetString().Trim();
                                var unit = row.Cell(4).GetString().Trim();
                                var plant = row.Cell(5).GetString().Trim();
                                var rack = row.Cell(6).GetString().Trim();
                                var noRakStr = row.Cell(7).GetString().Trim();
                                var qtyLotStr = row.Cell(8).GetString().Trim();
                                var minCapStr = row.Cell(9).GetString().Trim();
                                var maxCapStr = row.Cell(10).GetString().Trim();
                                var weightStr = row.Cell(11).GetString().Trim();
                                var volumeStr = row.Cell(12).GetString().Trim();

                                // Skip baris kosong atau baris catatan
                                if (string.IsNullOrWhiteSpace(itemCode) || 
                                    itemCode.StartsWith("Catatan", StringComparison.OrdinalIgnoreCase) ||
                                    itemCode.StartsWith("-", StringComparison.OrdinalIgnoreCase) ||
                                    itemCode == "Kode Item")
                                {
                                    continue;
                                }

                                // Validasi required fields
                                if (string.IsNullOrWhiteSpace(itemName) || itemName.Length < 2)
                                {
                                    errorMessages.Add($"Baris {row.RowNumber()}: Nama Item wajib diisi (min 2 karakter)");
                                    errorCount++;
                                    continue;
                                }

                                // Parse weight
                                decimal? weight = null;
                                if (!string.IsNullOrWhiteSpace(weightStr))
                                {
                                    if (decimal.TryParse(weightStr.Replace(",", "."), System.Globalization.NumberStyles.Any, 
                                        System.Globalization.CultureInfo.InvariantCulture, out decimal weightValue))
                                    {
                                        weight = weightValue;
                                    }
                                    else
                                    {
                                        errorMessages.Add($"Baris {row.RowNumber()}: Format berat tidak valid");
                                        errorCount++;
                                        continue;
                                    }
                                }

                                // Parse volume
                                decimal? volume = null;
                                if (!string.IsNullOrWhiteSpace(volumeStr))
                                {
                                    if (decimal.TryParse(volumeStr.Replace(",", "."), System.Globalization.NumberStyles.Any, 
                                        System.Globalization.CultureInfo.InvariantCulture, out decimal volumeValue))
                                    {
                                        volume = volumeValue;
                                    }
                                    else
                                    {
                                        errorMessages.Add($"Baris {row.RowNumber()}: Format volume tidak valid");
                                        errorCount++;
                                        continue;
                                    }
                                }

                                // Handle numeric fields
                                int? noRak = null;
                                if (int.TryParse(noRakStr, out int nr)) noRak = nr;

                                int? qtyLot = null;
                                if (int.TryParse(qtyLotStr, out int ql)) qtyLot = ql;

                                int? minCap = null;
                                if (int.TryParse(minCapStr, out int mic)) minCap = mic;

                                int? maxCap = null;
                                if (int.TryParse(maxCapStr, out int mac)) maxCap = mac;

                                // Item Logic: Update if exists, otherwise create new
                                var existingItem = await _context.Items.FirstOrDefaultAsync(i => i.ItemCode == itemCode);
                                if (existingItem != null)
                                {
                                    existingItem.ItemName = itemName;
                                    existingItem.Description = string.IsNullOrWhiteSpace(description) ? null : description;
                                    existingItem.Unit = string.IsNullOrWhiteSpace(unit) ? null : unit;
                                    existingItem.Plant = string.IsNullOrWhiteSpace(plant) ? null : plant;
                                    existingItem.Rack = string.IsNullOrWhiteSpace(rack) ? null : rack;
                                    existingItem.NoRack = noRak;
                                    existingItem.QtyLot = qtyLot;
                                    existingItem.RackMin = minCap;
                                    existingItem.RackMax = maxCap;
                                    existingItem.Weight = weight;
                                    existingItem.Volume = volume;
                                    existingItem.UpdatedDate = DateTime.Now;
                                    _context.Items.Update(existingItem);
                                }
                                else if (!items.Any(i => i.ItemCode == itemCode)) // Avoid duplicates in same batch
                                {
                                    var item = new Item
                                    {
                                        ItemCode = itemCode,
                                        ItemName = itemName,
                                        Description = string.IsNullOrWhiteSpace(description) ? null : description,
                                        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit,
                                        Category = "-", // Hide but keep as placeholder
                                        Plant = string.IsNullOrWhiteSpace(plant) ? null : plant,
                                        Rack = string.IsNullOrWhiteSpace(rack) ? null : rack,
                                        NoRack = noRak,
                                        QtyLot = qtyLot,
                                        RackMin = minCap,
                                        RackMax = maxCap,
                                        Weight = weight,
                                        Volume = volume,
                                        IsActive = true,
                                        CreatedDate = DateTime.Now
                                    };
                                    items.Add(item);
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorMessages.Add($"Baris {row.RowNumber()}: {ex.Message}");
                                errorCount++;
                            }
                        }
                    }
                }

                if (items.Any())
                {
                    _context.Items.AddRange(items);
                }
                
                await _context.SaveChangesAsync();
                
                if (successCount > 0)
                {
                    TempData["SuccessMessage"] = $"✅ Berhasil memproses {successCount} item!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Tidak ada data valid untuk diimport. Pastikan file Excel sudah diisi dengan benar.";
                }

                if (errorCount > 0)
                {
                    var errorDetail = string.Join("<br/>", errorMessages.Take(10));
                    TempData["ErrorMessage"] = TempData["ErrorMessage"] != null 
                        ? TempData["ErrorMessage"] + $"<br/><br/>{errorCount} baris gagal: <br/>{errorDetail}"
                        : $"{errorCount} baris gagal diimport: <br/>{errorDetail}";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat import Excel: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // Helper for PartNumber lookup
        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode)) return Json(new { success = false });

            // Try find by ItemCode or Label
            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemCode == itemCode);

            if (item != null)
            {
                return Json(new { success = true, itemName = item.ItemName });
            }

            return Json(new { success = false });
        }
    }
}
