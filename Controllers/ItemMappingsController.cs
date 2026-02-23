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
        public async Task<IActionResult> Index(string searchString, int pageNumber = 1)
        {
            var query = _context.ItemMappings.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(i => 
                    (i.Customer ?? "").Contains(searchString) || 
                    (i.CustomerPartNumber ?? "").Contains(searchString) || 
                    (i.VIN ?? "").Contains(searchString));
            }

            var totalItems = await query.CountAsync();
            int pageSize = 20;

            var items = await query
                .OrderBy(i => i.Customer)
                .ThenBy(i => i.CustomerPartNumber)
                .ThenBy(i => i.MappingId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { { "searchString", searchString } };

            ViewData["CurrentFilter"] = searchString;
            return View(items);
        }        

        // GET: ItemMappings/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: ItemMappings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Customer,CustomerPartNumber,VIN")] ItemMapping itemMapping)
        {
            if (ModelState.IsValid)
            {
                // Uniqueness Check: (Customer + PartNo + VIN) must be unique
                var exists = await _context.ItemMappings.AnyAsync(m => 
                    m.Customer.ToUpper() == itemMapping.Customer.ToUpper() && 
                    m.CustomerPartNumber.ToUpper() == itemMapping.CustomerPartNumber.ToUpper() &&
                    m.VIN.ToUpper() == itemMapping.VIN.ToUpper());
                
                if (exists) {
                    ModelState.AddModelError("", "Mapping untuk DOCK, Part No, dan VIN ini sudah ada!");
                    return View(itemMapping);
                }

                itemMapping.CreatedDate = DateTime.Now;
                _context.Add(itemMapping);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Mapping baru berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            return View(itemMapping);
        }

        // GET: ItemMappings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var mapping = await _context.ItemMappings.FindAsync(id);
            if (mapping == null) return NotFound();

            return View(mapping);
        }

        // POST: ItemMappings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("MappingId,Customer,CustomerPartNumber,VIN")] ItemMapping itemMapping)
        {
            if (id != itemMapping.MappingId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.ItemMappings.FindAsync(id);
                    if (existing == null) return NotFound();

                    // Uniqueness Check: (Customer + PartNo + VIN) must be unique
                    var exists = await _context.ItemMappings.AnyAsync(m => 
                        m.MappingId != id &&
                        m.Customer.ToUpper() == itemMapping.Customer.ToUpper() && 
                        m.CustomerPartNumber.ToUpper() == itemMapping.CustomerPartNumber.ToUpper() &&
                        m.VIN.ToUpper() == itemMapping.VIN.ToUpper());
                    
                    if (exists) {
                        ModelState.AddModelError("", "Mapping untuk DOCK, Part No, dan VIN ini sudah ada!");
                        return View(itemMapping);
                    }

                    existing.Customer = itemMapping.Customer;
                    existing.CustomerPartNumber = itemMapping.CustomerPartNumber;
                    existing.VIN = itemMapping.VIN;
                    existing.UpdatedDate = DateTime.Now;

                    _context.Update(existing);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Mapping berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ItemMappingExists(itemMapping.MappingId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(itemMapping);
        }

        // GET: ItemMappings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var mapping = await _context.ItemMappings.FirstOrDefaultAsync(m => m.MappingId == id);
            if (mapping == null) return NotFound();

            return View(mapping);
        }

        // POST: ItemMappings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var mapping = await _context.ItemMappings.FindAsync(id);
            if (mapping != null)
            {
                _context.ItemMappings.Remove(mapping);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Mapping berhasil dihapus!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ItemMappingExists(int id)
        {
            return _context.ItemMappings.Any(e => e.MappingId == id);
        }

        // Download Template Mapping
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Mapping");

                var headers = new[] { "DOCK", "PART NO (EKSTERNAL)", "VIN (INTERNAL)" };
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

        private string NormalizeMappingHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return System.Text.RegularExpressions.Regex.Replace(header, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
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

            int createdCount = 0;
            int updatedCount = 0;

            try
            {
                // 1. Load Existing Mappings (Performance: Memory Lookup)
                var existingMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var rows = worksheet.RowsUsed().Skip(1); // Skip Header

                        // Column Detection
                        var headerRow = worksheet.Row(1);
                        var headers = new Dictionary<string, int>();
                        for (int i = 1; i <= 20; i++) 
                        {
                            var cellVal = headerRow.Cell(i).GetValue<string>().Trim().ToUpper();
                            if (!string.IsNullOrEmpty(cellVal)) headers[cellVal] = i;
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords) {
                                foreach (var h in headers) {
                                    if (h.Key == kw) return h.Value;
                                }
                            }
                            foreach (var kw in keywords) {
                                foreach (var h in headers) {
                                    if (h.Key.Contains(kw)) return h.Value;
                                }
                            }
                            return -1;
                        }

                        int colCust = FindCol("DOCK", "NAMA CUSTOMER", "KODE CUSTOMER", "CUSTOMER", "CUST");
                        int colPart = FindCol("PART NO EKSTERNAL", "PART NO EXTERNAL", "PART NO", "EXT");
                        int colVin  = FindCol("VIN INTERNAL", "VIN", "INTERNAL");
                        
                        if (colCust == -1 || colPart == -1 || colVin == -1)
                        {
                            TempData["ErrorMessage"] = "Kolom Wajib (Customer, Part No, VIN) tidak ditemukan!";
                            return RedirectToAction(nameof(Index));
                        }

                        // Lookup for Fast Update - Unique Key: VIN | PART_NO | DOCK
                        var mappingLookup = existingMappings
                            .GroupBy(m => $"{NormalizeMappingHeader(m.VIN ?? "")}|{NormalizeMappingHeader(m.CustomerPartNumber ?? "")}|{NormalizeMappingHeader(m.Customer ?? "")}")
                            .ToDictionary(g => g.Key, g => g.First());

                        int duplicateCount = 0;
                        int totalProcessed = 0;
                        string lastVin = "";
                        string lastCust = "";
                        var duplicateSamples = new List<string>();

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            totalProcessed++;

                            string cust = row.Cell(colCust).GetString().Trim();
                            string partNo = row.Cell(colPart).GetString().Trim();
                            string vin = row.Cell(colVin).GetString().Trim();

                            if (string.IsNullOrEmpty(vin) && !string.IsNullOrEmpty(lastVin)) vin = lastVin; else lastVin = vin;
                            if (string.IsNullOrEmpty(cust) && !string.IsNullOrEmpty(lastCust)) cust = lastCust; else lastCust = cust;

                            if (string.IsNullOrEmpty(partNo)) continue;

                            string key = $"{NormalizeMappingHeader(vin)}|{NormalizeMappingHeader(partNo)}|{NormalizeMappingHeader(cust)}";
                            
                            if (mappingLookup.TryGetValue(key, out var existingMapping))
                            {
                                existingMapping.VIN = vin ?? "";
                                existingMapping.UpdatedDate = DateTime.Now;
                                
                                if (existingMapping.MappingId > 0)
                                {
                                    var entry = _context.Entry(existingMapping);
                                    if (entry.State == EntityState.Detached)
                                    {
                                        _context.Update(existingMapping);
                                        updatedCount++;
                                    }
                                }
                                else
                                {
                                    duplicateCount++;
                                    if (duplicateSamples.Count < 5)
                                    {
                                        duplicateSamples.Add($"Row {row.RowNumber()}: {vin} | {partNo} | {cust}");
                                    }
                                }
                            }
                            else
                            {
                                var newMapping = new ItemMapping
                                {
                                    Customer = cust ?? "",
                                    CustomerPartNumber = partNo ?? "",
                                    VIN = vin ?? "",
                                    CreatedDate = DateTime.Now
                                };
                                _context.ItemMappings.Add(newMapping);
                                mappingLookup[key] = newMapping;
                                createdCount++;
                            }
                        }
                        await _context.SaveChangesAsync();
                        string msg = $"Impor Selesai! Total: {totalProcessed} baris. Baru: {createdCount}, Update: {updatedCount}, Duplikat diabaikan: {duplicateCount}.";
                        if (duplicateSamples.Any())
                        {
                            msg += " Contoh duplikat: " + string.Join("; ", duplicateSamples);
                        }
                        TempData["SuccessMessage"] = msg;
                    }
                }
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
                _context.ItemMappings.RemoveRange(_context.ItemMappings);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Berhasil menghapus seluruh data Master Item Mapping! Data Master Item (Stok) AMAN.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
