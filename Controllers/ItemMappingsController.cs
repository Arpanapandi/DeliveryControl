using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using ClosedXML.Excel;
using System.IO;

namespace DeliveryControl.Controllers
{
    public class ItemMappingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ItemMappingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: ItemMappings
        public async Task<IActionResult> Index(string searchString)
        {
            var query = _context.Items.AsNoTracking().AsQueryable();

            // Only show items that are actually mappings (have a Part No defined)
            query = query.Where(i => !string.IsNullOrEmpty(i.CustomerPartNumber));

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(i => 
                    (i.Customer ?? "").Contains(searchString) || 
                    (i.CustomerPartNumber ?? "").Contains(searchString) || 
                    (i.VIN ?? "").Contains(searchString));
            }

            var mappings = await query
                .OrderBy(i => i.Customer)
                .ThenBy(i => i.CustomerPartNumber)
                .ToListAsync();

            ViewData["CurrentFilter"] = searchString;
            return View(mappings);
        }

        // GET: ItemMappings/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: ItemMappings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Customer,CustomerPartNumber,VIN")] Item item)
        {
            if (ModelState.IsValid)
            {
                // To avoid "Data Hantu" (Duplicate VINs), check if VIN already exists
                var existing = await _context.Items.FirstOrDefaultAsync(i => i.VIN == item.VIN);
                
                if (existing != null)
                {
                    existing.Customer = item.Customer;
                    existing.CustomerPartNumber = item.CustomerPartNumber;
                    existing.UpdatedDate = DateTime.Now;
                    _context.Update(existing);
                    TempData["SuccessMessage"] = "Mapping berhasil diperbarui pada item yang sudah ada!";
                }
                else
                {
                    // Generate internal ItemCode (Guid) to avoid null errors with existing schema
                    item.ItemCode = Guid.NewGuid().ToString().ToUpper();
                    item.ItemName = "MAPPING-ONLY"; 
                    item.CreatedDate = DateTime.Now;
                    item.IsActive = true;
                    _context.Add(item);
                    TempData["SuccessMessage"] = "Mapping baru berhasil ditambahkan!";
                }
                
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(item);
        }

        // GET: ItemMappings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var item = await _context.Items.FindAsync(id);
            if (item == null) return NotFound();

            return View(item);
        }

        // POST: ItemMappings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ItemId,Customer,CustomerPartNumber,VIN")] Item item)
        {
            if (id != item.ItemId) return NotFound();

            // Remove validation for fields that are NOT in the partial form
            ModelState.Remove("ItemCode");
            ModelState.Remove("ItemName");

            if (ModelState.IsValid)
            {
                try
                {
                    // Fetch existing item to perform partial update
                    var existingItem = await _context.Items.FindAsync(id);
                    if (existingItem == null) return NotFound();

                    // Update ONLY mapping fields
                    existingItem.Customer = item.Customer;
                    existingItem.CustomerPartNumber = item.CustomerPartNumber;
                    // VIN is readonly in UI, but we can still update it if posted, 
                    // or keep it same. Here we keep it same to be safe.
                    // existingItem.VIN = item.VIN; 
                    
                    existingItem.UpdatedDate = DateTime.Now;

                    _context.Update(existingItem);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Mapping berhasil diupdate tanpa merusak data Master lainnya!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ItemExists(item.ItemId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(item);
        }

        // GET: ItemMappings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var item = await _context.Items.FirstOrDefaultAsync(m => m.ItemId == id);
            if (item == null) return NotFound();

            return View(item);
        }

        // POST: ItemMappings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item != null)
            {
                _context.Items.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Mapping berhasil dihapus!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ItemExists(int id)
        {
            return _context.Items.Any(e => e.ItemId == id);
        }

        // Download Template Mapping
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Mapping");

                var headers = new[] { "KODE CUSTOMER", "PART NO (EKSTERNAL)", "VIN (INTERNAL)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Sample data
                worksheet.Cell(2, 1).Value = "CUSTOMER_A";
                worksheet.Cell(2, 2).Value = "EXT-PART-001";
                worksheet.Cell(2, 3).Value = "VIN-INT-001";

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Item_Mapping.xlsx");
                }
            }
        }

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return new string(header.ToUpper().Where(c => char.IsLetterOrDigit(c)).ToArray());
        }

        // Import Excel Mapping
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid!";
                return RedirectToAction(nameof(Index));
            }

            int successCount = 0;
            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var firstRow = worksheet.Row(1);
                        
                        // Dynamic Header Detection
                        var headers = new Dictionary<string, int>();
                        for (int c = 1; c <= worksheet.ColumnsUsed().Count(); c++)
                        {
                            var h = NormalizeHeader(firstRow.Cell(c).GetString());
                            if (!string.IsNullOrEmpty(h) && !headers.ContainsKey(h)) headers[h] = c;
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords)
                            {
                                var normalizedKw = NormalizeHeader(kw);
                                if (headers.ContainsKey(normalizedKw)) return headers[normalizedKw];
                            }
                            return -1;
                        }

                        int colCust = FindCol("KODE CUSTOMER", "CUSTOMER", "CUST");
                        int colPart = FindCol("PART NO EKSTERNAL", "PART NO EXTERNAL", "PART NO", "EXT");
                        int colVin  = FindCol("VIN INTERNAL", "VIN", "INTERNAL");

                        if (colPart == -1 || colVin == -1)
                        {
                            TempData["ErrorMessage"] = "Format header tidak dikenali! Pastikan ada kolom PART NO (EKSTERNAL) dan VIN (INTERNAL).";
                            return RedirectToAction(nameof(Index));
                        }

                        var rows = worksheet.RowsUsed().Skip(1);
                        
                        // Load all existing items into a dictionary to prevent duplicates and handle updates
                        var allItems = await _context.Items.ToListAsync();
                        var itemDict = allItems.GroupBy(i => i.VIN?.Trim().ToUpper())
                                              .Where(g => !string.IsNullOrEmpty(g.Key))
                                              .ToDictionary(g => g.Key!, g => g.First());

                        foreach (var row in rows)
                        {
                            string cust = colCust != -1 ? row.Cell(colCust).GetString().Trim() : "";
                            string partNo = row.Cell(colPart).GetString().Trim();
                            string vin = row.Cell(colVin).GetString().Trim();

                            if (string.IsNullOrEmpty(vin)) continue;

                            string vinKey = vin.ToUpper();

                            if (itemDict.TryGetValue(vinKey, out var existing))
                            {
                                // Update existing item
                                existing.Customer = cust;
                                existing.CustomerPartNumber = partNo;
                                existing.UpdatedDate = DateTime.Now;
                                _context.Update(existing);
                            }
                            else
                            {
                                // Create new item mapping
                                var newItem = new Item
                                {
                                    ItemCode = Guid.NewGuid().ToString().ToUpper(),
                                    ItemName = "MAPPING-ONLY", // Required by DB schema
                                    Customer = cust,
                                    CustomerPartNumber = partNo,
                                    VIN = vin,
                                    IsActive = true,
                                    CreatedDate = DateTime.Now
                                };
                                _context.Items.Add(newItem);
                                itemDict[vinKey] = newItem; // Track within this loop
                            }
                            successCount++;
                        }
                        await _context.SaveChangesAsync();
                    }
                }
                TempData["SuccessMessage"] = $"Berhasil memproses {successCount} mapping!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // Bulk Delete All Mappings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteAll()
        {
            try
            {
                // To prevent "Ghost Data", we must clear related transactions first if we are deleting items
                _context.PreparationRecords.RemoveRange(_context.PreparationRecords);
                _context.DeliveryItems.RemoveRange(_context.DeliveryItems);
                _context.Items.RemoveRange(_context.Items);
                
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Berhasil menghapus seluruh data mapping dan transaksi terkait!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
