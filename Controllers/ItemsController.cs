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

            // v3.8 Fix: Sort by ItemId (natural creation order) to match Excel's original sequence
            return View(await items.OrderBy(i => i.ItemId).ToListAsync());
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
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,Plant,Rack,NoRack,QtyLot,RackMin,RackMax,Customer,VIN,ROP")] Item item)
        {
            if (ModelState.IsValid)
            {
                item.CreatedDate = DateTime.Now;
                _context.Add(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
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
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
            return View(item);
        }

        // POST: Items/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,CreatedDate,Plant,Rack,NoRack,QtyLot,RackMin,RackMax,Customer,VIN,ROP")] Item item)
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
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
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
                .Include(i => i.PullingRecords)
                .FirstOrDefaultAsync(i => i.ItemId == id);

            if (item != null)
            {
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
                .Include(i => i.PullingRecords)
                .Where(i => ids.Contains(i.ItemId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var item in itemsToDelete)
            {
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

        // Bulk Delete All Items & Related Transactions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteAll()
        {
            try
            {
                // Deleting all transactions first
                _context.PullingRecords.RemoveRange(_context.PullingRecords);
                _context.PreparationRecords.RemoveRange(_context.PreparationRecords);
                _context.DeliveryItems.RemoveRange(_context.DeliveryItems);
                
                // Then deleting all items
                _context.Items.RemoveRange(_context.Items);
                
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "✅ Seluruh data master item dan transaksi terkait berhasil dihapus!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "❌ Gagal menghapus seluruh data: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Item");

                // Headers matching UI: NO | KODE | NAMA | PROD. PLANT | RAK | NO RAK | CUST | STATUS | PROD. | VIN | QPC | MIN 1D | ROP 2D | MAX 3D
                worksheet.Cell(1, 1).Value = "NO";
                worksheet.Cell(1, 2).Value = "LOKASI RACK";
                worksheet.Cell(1, 3).Value = "PROD. PLANT";
                worksheet.Cell(1, 4).Value = "RAK";
                worksheet.Cell(1, 5).Value = "NO RAK";
                worksheet.Cell(1, 6).Value = "CUST";
                worksheet.Cell(1, 7).Value = "STATUS";
                worksheet.Cell(1, 8).Value = "PROD.";
                worksheet.Cell(1, 9).Value = "VIN";
                worksheet.Cell(1, 10).Value = "QPC";
                worksheet.Cell(1, 11).Value = "MIN 1D";
                worksheet.Cell(1, 12).Value = "ROP 2D";
                worksheet.Cell(1, 13).Value = "MAX 3D";

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, 13);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f172a");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Example data (v2.2 matching Image 1)
                worksheet.Cell(2, 1).Value = 1;
                worksheet.Cell(2, 2).Value = "RVI";
                worksheet.Cell(2, 3).Value = "Molded";
                worksheet.Cell(2, 4).Value = "X";
                worksheet.Cell(2, 5).Value = "";
                worksheet.Cell(2, 6).Value = "PASI EXP";
                worksheet.Cell(2, 7).Value = "Aktif";
                worksheet.Cell(2, 8).Value = "HBR";
                worksheet.Cell(2, 9).Value = "VIN001";
                worksheet.Cell(2, 10).Value = 0;
                worksheet.Cell(2, 11).Value = 0;
                worksheet.Cell(2, 12).Value = 0;
                worksheet.Cell(2, 13).Value = 0;

                headerRange.RangeUsed().Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Item_Revised.xlsx");
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

            int successCount = 0;
            int errorCount = 0;
            var errorSamples = new List<string>();

            try
            {
                // 1. Pre-load ALL existing items into memory with COMPOSITE KEY (v9.0)
                var existingItems = await _context.Items.ToListAsync();
                var itemDict = existingItems
                    .ToDictionary(i => $"{NormalizeKey(i.VIN)}|{NormalizeKey(i.ItemName)}|{NormalizeKey(i.Plant)}|{NormalizeKey(i.Rack)}|{NormalizeKey(i.NoRack?.ToString())}|{NormalizeKey(i.Customer)}", i => i);

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        
                        // --- v6.1 ROCK-SOLID HEADER DETECTION (Handling Alt+Enter) ---
                        var headerRow = worksheet.Row(1);
                        int bestScore = -1;
                        var targetKeywords = new[] { "VIN", "LOKASI", "RACK", "CUST", "PLANT", "QPC", "MIN", "ROP", "MAX", "PROD" };

                        // Stop at the FIRST row that has a high keyword match to avoid skipping data
                        for (int r = 1; r <= 30; r++) {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 30; c++) {
                                var val = NormalizeHeader(GetSafeString(testRow.Cell(c)));
                                foreach (var k in targetKeywords) if (val.Contains(k)) currentScore++;
                            }
                            // Threshold 6: Very likely a header. Stop immediately.
                            if (currentScore >= 6) {
                                headerRow = testRow;
                                break;
                            }
                            // Lower threshold backup
                            if (currentScore > bestScore && currentScore >= 3) {
                                bestScore = currentScore;
                                headerRow = testRow;
                            }
                        }

                        // Map Headers accurately
                        var cleanedHeaders = new Dictionary<string, int>();
                        for (int col = 1; col <= worksheet.LastColumnUsed().ColumnNumber(); col++) {
                            var cleaned = NormalizeHeader(GetSafeString(headerRow.Cell(col)));
                            if (!string.IsNullOrEmpty(cleaned) && !cleanedHeaders.ContainsKey(cleaned)) cleanedHeaders.Add(cleaned, col);
                        }

                        int FindCol(params string[] keywords) {
                            foreach (var kw in keywords) {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanedHeaders.ContainsKey(cleanKw)) return cleanedHeaders[cleanKw];
                                var match = cleanedHeaders.Keys.FirstOrDefault(k => k.Contains(cleanKw));
                                if (match != null) return cleanedHeaders[match];
                            }
                            return -1;
                        }

                        // Get Column Letter for Auditor Report
                        string GetColLetter(int colIndex) => colIndex != -1 ? worksheet.Column(colIndex).ColumnLetter() : "?";
                        
                        var colMap = new {
                            Lokasi   = FindCol("LOKASI RACK", "LOKASIRACK", "LOKASI"),
                            Plant    = FindCol("PROD PLANT", "PRODPLANT", "PLANT"),
                            Rak      = FindCol("RAK"),
                            NoRak    = FindCol("NO RAK", "NORAK"),
                            Cust     = FindCol("CUST"),
                            Status   = FindCol("STATUS"),
                            Prod     = FindCol("PROD", "PROD."), 
                            Vin      = FindCol("VIN"),
                            Qpc      = FindCol("QPC"),
                            Min      = FindCol("MIN1D", "MIN"),
                            Rop      = FindCol("ROP2D", "ROP"),
                            Max      = FindCol("MAX3D", "MAX")
                        };

                        if (colMap.Vin == -1) {
                            TempData["ErrorMessage"] = "Header 'VIN' tidak ditemukan. Pastikan file Excel sesuai template.";
                            return RedirectToAction(nameof(Index));
                        }

                        // --- SHERLOCK MODE (v7.0): Deep Diagnostics ---
                        var excelVins = new HashSet<string>();
                        var duplicateSamples = new List<string>();
                        var sampleVins = new List<string>();
                        var sampleMins = new List<string>();
                        
                        int duplicateInExcelCount = 0;
                        int rowProcessCount = 0;

                        // --- FIX v6.3: Ensure we start EXACTLY after the header row ---
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber());
                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            rowProcessCount++;
                            try
                            {
                                string vin  = GetSafeString(row.Cell(colMap.Vin)).Trim();
                                string lok  = GetSafeString(row.Cell(colMap.Lokasi)).Trim();
                                string plt  = GetSafeString(row.Cell(colMap.Plant)).Trim();
                                string rak  = GetSafeString(row.Cell(colMap.Rak)).Trim();
                                string nrk  = GetSafeString(row.Cell(colMap.NoRak)).Trim();
                                string cst  = GetSafeString(row.Cell(colMap.Cust)).Trim();

                                if (string.IsNullOrEmpty(vin)) continue;

                                // v9.0 Composite Key Construction
                                string compositeKey = $"{NormalizeKey(vin)}|{NormalizeKey(lok)}|{NormalizeKey(plt)}|{NormalizeKey(rak)}|{NormalizeKey(nrk)}|{NormalizeKey(cst)}";
                                
                                // Capture Samples (First 3 rows)
                                if (sampleVins.Count < 3) {
                                    sampleVins.Add(vin);
                                    sampleMins.Add(GetSafeString(row.Cell(colMap.Min))); 
                                }

                                // --- v7.0 DUPLICATE STRATEGY: KEEP FIRST ---
                                if (excelVins.Contains(compositeKey)) {
                                    duplicateInExcelCount++;
                                    if (duplicateSamples.Count < 3) duplicateSamples.Add(vin);
                                    continue; 
                                }
                                excelVins.Add(compositeKey);

                                var dto = new ItemDto {
                                    ItemCode  = lok, // LOKASI RACK maps to ItemName
                                    Plant     = plt,
                                    Rack      = rak,
                                    NoRackStr = nrk,
                                    Customer  = cst,
                                    StatusStr = GetSafeString(row.Cell(colMap.Status)),
                                    Category  = GetSafeString(row.Cell(colMap.Prod)),
                                    VIN       = vin,
                                    QpcStr    = GetSafeNumberString(row.Cell(colMap.Qpc)),
                                    MinStr    = GetSafeNumberString(row.Cell(colMap.Min)),
                                    RopStr    = GetSafeNumberString(row.Cell(colMap.Rop)),
                                    MaxStr    = GetSafeNumberString(row.Cell(colMap.Max))
                                };

                                if (itemDict.TryGetValue(compositeKey, out var existingItem))
                                {
                                    UpdateItem(existingItem, dto);
                                }
                                else
                                {
                                    var newItem = CreateItem(dto);
                                    _context.Items.Add(newItem);
                                    itemDict[compositeKey] = newItem; 
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) {
                                    errorSamples.Add($"Baris {row.RowNumber()}: {ex.Message}");
                                }
                            }
                        }
                        
                        if (successCount > 0) 
                        {
                            string colAudit = $"[KOLOM -> VIN:{GetColLetter(colMap.Vin)}, MIN:{GetColLetter(colMap.Min)}, ROP:{GetColLetter(colMap.Rop)}, MAX:{GetColLetter(colMap.Max)}]";
                            string dupAudit = duplicateInExcelCount > 0 ? $" | ⚠️ Di-Skip {duplicateInExcelCount} Duplikat (Cth: {string.Join(",", duplicateSamples)})" : "";
                            string sampleAudit = $" | Sample Data: VIN={string.Join(",", sampleVins)}, MIN={string.Join(",", sampleMins)}";
                            
                            TempData["SuccessMessage"] = $"✅ BERHASIL 100% (v9.0 Composite): {successCount} data unik. " + 
                                $"(Total Baris Excel: {rowProcessCount} {dupAudit} {sampleAudit} {colAudit})";
                        }
                        if (errorCount > 0) {
                            TempData["ErrorMessage"] = $"⚠️ {errorCount} baris gagal. Contoh: " + string.Join(", ", errorSamples);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Fatal Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }


        private string NormalizeKey(string val)
        {
            return (val ?? "").Trim().ToUpper();
        }

        // --- Helper Methods (v6.0 Type-Safe & Alt+Enter Resilient) ---

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            // Remove all non-alphanumeric characters (newlines, dots, spaces, special chars)
            return System.Text.RegularExpressions.Regex.Replace(header, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
        }

        private string GetSafeNumberString(ClosedXML.Excel.IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "0";
            
            try
            {
                // v8.0: "UNIVERSAL STRING PARSING" - Force Teks-ke-Angka
                // Kita tidak lagi mengandalkan IsNumber karena user konfirmasi Excel adalah kolom TEKS.
                string raw = "";
                
                if (cell.HasFormula) {
                    try { raw = cell.CachedValue.ToString(); } catch { raw = cell.Value.ToString(); }
                } else {
                    // Ambil sebagai string mentah, abaikan format sel murni angka/teks
                    raw = cell.Value.ToString();
                }

                if (string.IsNullOrWhiteSpace(raw) || raw == "-" || raw.Equals("null", StringComparison.OrdinalIgnoreCase)) return "0";

                // Regex Extraction: Cari urutan angka pertama (integer atau desimal)
                var match = System.Text.RegularExpressions.Regex.Match(raw, @"[0-9]+([.,][0-9]+)?");
                
                if (match.Success)
                {
                    string cleanNumber = match.Value;
                    
                    // Logika Thousand Separator (Misal: 5.000 -> 5000)
                    if (cleanNumber.Contains(".") && !cleanNumber.Contains(",")) {
                        var parts = cleanNumber.Split('.');
                        if (parts.Length > 1 && parts[parts.Length-1].Length == 3) {
                             cleanNumber = cleanNumber.Replace(".", "");
                        }
                    } 
                    // Logika Desimal Komma (Misal: 5,5 -> 5.5)
                    else if (cleanNumber.Contains(",")) {
                        cleanNumber = cleanNumber.Replace(".", "").Replace(",", ".");
                    }

                    if (double.TryParse(cleanNumber, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        return Math.Round(parsed).ToString();
                    }
                }

                return "0";
            }
            catch { 
                return "0"; 
            }
        }

        private string GetSafeString(ClosedXML.Excel.IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "";
            try
            {
                // Avoid direct casting, use ToString() which handles XLCellValue correctly
                return cell.Value.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }


        private int? ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d))
                return (int)d;
            return null;
        }

        private void UpdateItem(Item item, ItemDto data)
        {
            // Do NOT overwrite item.ItemCode (Guid) once set.
            item.ItemName = data.ItemCode; // DISPLAY: LOKASI RACK (Excel Col B)
            item.Plant    = data.Plant;
            item.Rack     = data.Rack;
            item.NoRack   = ParseInt(data.NoRackStr);
            item.Customer = data.Customer;
            item.Category = data.Category;
            item.VIN      = data.VIN;
            item.QtyLot   = ParseInt(data.QpcStr);
            item.RackMin  = ParseInt(data.MinStr);
            item.ROP      = ParseInt(data.RopStr);
            item.RackMax  = ParseInt(data.MaxStr);
            item.IsActive = !data.StatusStr.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase);
            item.UpdatedDate = DateTime.Now;
        }

        private Item CreateItem(ItemDto data)
        {
            return new Item
            {
                ItemCode    = Guid.NewGuid().ToString().ToUpper(), // v4.0 Internal Unique Key
                ItemName    = data.ItemCode, // DISPLAY: LOKASI RACK (Excel Col B)
                Plant       = data.Plant,
                Rack        = data.Rack,
                NoRack      = ParseInt(data.NoRackStr),
                Customer    = data.Customer,
                Category    = data.Category,
                VIN         = data.VIN,
                QtyLot      = ParseInt(data.QpcStr),
                RackMin     = ParseInt(data.MinStr),
                ROP         = ParseInt(data.RopStr),
                RackMax     = ParseInt(data.MaxStr),
                IsActive    = !data.StatusStr.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                CreatedDate = DateTime.Now
            };
        }

        private class ItemDto
        {
            public string ItemCode { get; set; } = "";
            public string Plant { get; set; } = "";
            public string Rack { get; set; } = "";
            public string NoRackStr { get; set; } = "";
            public string Customer { get; set; } = "";
            public string StatusStr { get; set; } = "";
            public string Category { get; set; } = "";
            public string VIN { get; set; } = "";
            public string QpcStr { get; set; } = "";
            public string MinStr { get; set; } = "";
            public string RopStr { get; set; } = "";
            public string MaxStr { get; set; } = "";
        }

        // Helper for PartNumber lookup
        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode)) return Json(new { success = false });

            // v4.0 Unified Tag Lookup
            // Logic: Operator scans "RVI-A-10"
            // We search for item where (ItemName + "-" + Rack + "-" + NoRack) matches.
            var item = await _context.Items
                .AsNoTracking()
                .ToListAsync();

            var matched = item.FirstOrDefault(i => 
            {
                var combinedTag = $"{i.ItemName}-{i.Rack}-{i.NoRack}".ToUpper();
                return combinedTag == itemCode.ToUpper();
            });

            if (matched != null)
            {
                return Json(new { success = true, itemName = matched.ItemName, rack = matched.Rack, noRack = matched.NoRack });
            }

            return Json(new { success = false });
        }
    }
}
