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
                .Include(i => i.PoolingRecords)
                .FirstOrDefaultAsync(i => i.ItemId == id);

            if (item != null)
            {
                // Cek relasi dengan DeliveryItem
                if (item.DeliveryItems.Any())
                {
                    TempData["ErrorMessage"] = $"Tidak dapat menghapus item! Masih ada {item.DeliveryItems.Count} delivery item yang terkait.";
                    return RedirectToAction(nameof(Index));
                }

                // Cek relasi dengan PoolingRecords
                if (item.PoolingRecords.Any())
                {
                    TempData["ErrorMessage"] = $"Tidak dapat menghapus item! Masih ada {item.PoolingRecords.Count} riwayat transaksi pooling. Anda bisa menonaktifkan status 'Aktif' alih-alih menghapus.";
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item != null)
            {
                item.IsActive = !item.IsActive;
                item.UpdatedDate = DateTime.Now;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Item {item.ItemCode} berhasil {(item.IsActive ? "diaktifkan" : "dinonaktifkan")}!";
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
                .Include(i => i.PoolingRecords)
                .Where(i => ids.Contains(i.ItemId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var item in itemsToDelete)
            {
                if (item.DeliveryItems.Any())
                {
                    errorMessages.Add($"{item.ItemCode}: Ada delivery item");
                    errorCount++;
                    continue;
                }

                if (item.PoolingRecords.Any())
                {
                    errorMessages.Add($"{item.ItemCode}: Ada riwayat pooling");
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

                // Header matches the UI table
                worksheet.Cell(1, 1).Value = "KODE (TAG)";
                worksheet.Cell(1, 2).Value = "NAMA";
                worksheet.Cell(1, 3).Value = "PLANT";
                worksheet.Cell(1, 4).Value = "LOCATION (RAK.NO)";
                worksheet.Cell(1, 5).Value = "QTY/LOT";
                worksheet.Cell(1, 6).Value = "CAPACITY (MAX)";
                worksheet.Cell(1, 7).Value = "STATUS";

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#3b82f6");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Contoh data (baris 2)
                worksheet.Cell(2, 1).Value = "ITM001";
                worksheet.Cell(2, 2).Value = "Steel Plate 10mm";
                worksheet.Cell(2, 3).Value = "Molded";
                worksheet.Cell(2, 4).Value = "A.1";
                worksheet.Cell(2, 5).Value = 100;
                worksheet.Cell(2, 6).Value = 500;
                worksheet.Cell(2, 7).Value = "Aktif";

                // Contoh data 2 (baris 3)
                worksheet.Cell(3, 1).Value = "ITM002";
                worksheet.Cell(3, 2).Value = "Bolt M12";
                worksheet.Cell(3, 3).Value = "Hose";
                worksheet.Cell(3, 4).Value = "B.15";
                worksheet.Cell(3, 5).Value = 500;
                worksheet.Cell(3, 6).Value = 1000;
                worksheet.Cell(3, 7).Value = "Aktif";

                // Catatan
                int noteRow = 5;
                worksheet.Cell(noteRow++, 1).Value = "PANDUAN PENGISIAN:";
                worksheet.Cell(noteRow++, 1).Value = "1. KODE (TAG): Wajib diisi & Unique (Contoh: ITM001)";
                worksheet.Cell(noteRow++, 1).Value = "2. NAMA: Wajib diisi (Contoh: Bolt M12)";
                worksheet.Cell(noteRow++, 1).Value = "3. PLANT: Molded, Hose, atau RVI";
                worksheet.Cell(noteRow++, 1).Value = "4. LOCATION (RAK.NO): Format Huruf.Angka (Contoh: A.1)";
                worksheet.Cell(noteRow++, 1).Value = "5. QTY/LOT: Jumlah item per lot (Angka)";
                worksheet.Cell(noteRow++, 1).Value = "6. CAPACITY: Kapasitas maksimal rak (Angka)";
                worksheet.Cell(noteRow++, 1).Value = "7. STATUS: Aktif atau Tidak Aktif";

                headerRange.RangeUsed().Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                
                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Item.xlsx");
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

                        foreach (var row in rows)
                        {
                            try
                            {
                                // Excel Format matches UI:
                                // 1: KODE (TAG)
                                // 2: NAMA
                                // 3: PLANT
                                // 4: LOCATION (RAK.NO)
                                // 5: QTY/LOT
                                // 6: CAPACITY
                                // 7: STATUS
                                
                                var itemCode = row.Cell(1).GetString().Trim();
                                var itemName = row.Cell(2).GetString().Trim();
                                var plant = row.Cell(3).GetString().Trim();
                                var location = row.Cell(4).GetString().Trim();
                                var qtyLotStr = row.Cell(5).GetString().Trim();
                                var maxCapStr = row.Cell(6).GetString().Trim();
                                var statusStr = row.Cell(7).GetString().Trim();

                                // Skip empty rows or note markers
                                if (string.IsNullOrWhiteSpace(itemCode) || 
                                    itemCode.StartsWith("PANDUAN", StringComparison.OrdinalIgnoreCase) ||
                                    itemCode.Length <= 1 && char.IsDigit(itemCode[0])) // Skip row numbers in notes
                                {
                                    continue;
                                }

                                // Validasi required fields
                                if (string.IsNullOrWhiteSpace(itemName))
                                {
                                    errorMessages.Add($"Baris {row.RowNumber()}: Nama Item wajib diisi");
                                    errorCount++;
                                    continue;
                                }

                                // Handle Location (RAK.NO) -> Split by . or -
                                string? rack = null;
                                int? noRak = null;
                                if (!string.IsNullOrEmpty(location))
                                {
                                    var parts = location.Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 1) rack = parts[0].ToUpper();
                                    if (parts.Length >= 2 && int.TryParse(parts[1], out int nr)) noRak = nr;
                                }

                                // Handle numeric fields
                                int? qtyLot = null;
                                if (int.TryParse(qtyLotStr, out int ql)) qtyLot = ql;

                                int? maxCap = null;
                                if (int.TryParse(maxCapStr, out int mac)) maxCap = mac;

                                // Status logic
                                bool isActive = !statusStr.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase);

                                // Item Logic: Update if exists, otherwise create new
                                var existingItem = await _context.Items.FirstOrDefaultAsync(i => i.ItemCode == itemCode);
                                if (existingItem != null)
                                {
                                    existingItem.ItemName = itemName;
                                    existingItem.Plant = string.IsNullOrWhiteSpace(plant) ? null : plant;
                                    existingItem.Rack = rack;
                                    existingItem.NoRack = noRak;
                                    existingItem.QtyLot = qtyLot;
                                    existingItem.RackMax = maxCap;
                                    existingItem.RackMin = (maxCap.HasValue ? maxCap / 10 : 0); // Logic: Min is 10% of Max
                                    existingItem.IsActive = isActive;
                                    existingItem.UpdatedDate = DateTime.Now;
                                    _context.Items.Update(existingItem);
                                }
                                else
                                {
                                    var item = new Item
                                    {
                                        ItemCode = itemCode,
                                        ItemName = itemName,
                                        Plant = string.IsNullOrWhiteSpace(plant) ? null : plant,
                                        Rack = rack,
                                        NoRack = noRak,
                                        QtyLot = qtyLot,
                                        RackMax = maxCap,
                                        RackMin = (maxCap.HasValue ? maxCap / 10 : 0),
                                        Category = "-",
                                        IsActive = isActive,
                                        CreatedDate = DateTime.Now
                                    };
                                    _context.Items.Add(item);
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

                await _context.SaveChangesAsync();
                
                if (successCount > 0)
                {
                    TempData["SuccessMessage"] = $"✅ Berhasil memproses {successCount} item!";
                }
                
                if (errorCount > 0)
                {
                    var errorDetail = string.Join("<br/>", errorMessages.Take(5));
                    TempData["ErrorMessage"] = $"{errorCount} baris gagal diimport: <br/>{errorDetail}";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Terjadi kesalahan saat memproses file: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
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
