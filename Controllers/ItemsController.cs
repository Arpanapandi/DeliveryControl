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
                .Include(i => i.PoolingRecords)
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
                .Include(i => i.PoolingRecords)
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
                _context.PoolingRecords.RemoveRange(_context.PoolingRecords);
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

            var items = new List<Item>();
            var errorMessages = new List<string>();
            int successCount = 0;
            int errorCount = 0;
            var errorSamples = new List<string>();

            var headerMap = new Dictionary<string, int>();
            try
            {
                // --- REFACTORED IMPLEMENTATION (v2.6) --- 
                // Clean separation of concerns for robust import logic
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
                    {
                        // --- v5.8 EXACT MIRROR: Zero Manipulation ---
                        var worksheet = workbook.Worksheet(1);
                        var headerRow = worksheet.FirstRowUsed();
                        int bestScore = -1;
                        var targetKeywords = new[] { "VIN", "LOKASI", "RACK", "CUST", "PLANT", "QPC", "MIN", "ROP", "MAX", "PROD" };

                        // Search first 20 rows for the row with the HIGHEST match score
                        for (int r = 1; r <= 20; r++) {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 25; c++) {
                                var val = GetSafeString(testRow.Cell(c)).ToUpper();
                                foreach (var k in targetKeywords) if (val.Contains(k)) currentScore++;
                            }
                            if (currentScore > bestScore && currentScore >= 3) {
                                bestScore = currentScore;
                                headerRow = testRow;
                            }
                        }

                        // Exact Header Mapping (v5.8): Based on user's Excel screenshot
                        var cleanedHeaders = new Dictionary<string, int>();
                        for (int col = 1; col <= worksheet.LastColumnUsed().ColumnNumber(); col++) {
                            var rawVal = GetSafeString(headerRow.Cell(col));
                            var cleaned = System.Text.RegularExpressions.Regex.Replace(rawVal, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
                            if (!string.IsNullOrEmpty(cleaned) && !cleanedHeaders.ContainsKey(cleaned)) cleanedHeaders.Add(cleaned, col);
                        }

                        // Pre-calculate Column Indices (v4.7 Absolute Sync)
                        // Uses an EXACT-match-first strategy to avoid collisions
                        var aliasMap = new Dictionary<string, string[]> {
                            { "LOKASI", new[] { "LOKASIRACK", "LOKASI", "TEMPAT" } },
                            { "PLANT",  new[] { "PRODPLANT", "PLANT", "PLANTCODE" } },
                            { "RAK",    new[] { "RAK", "SHELF" } },
                            { "NORAK",  new[] { "NORAK", "NO" } },
                            { "CUST",   new[] { "CUST", "CUSTOMER" } },
                            { "STATUS", new[] { "STATUS", "ACTIVE" } },
                            { "PROD",   new[] { "PROD", "PROD.", "CATEGORY" } }, 
                            { "VIN",    new[] { "VIN", "ITEMCODE", "PARTNO" } },
                            { "QPC",    new[] { "QPC", "QTYLOT", "LOT" } },
                            { "MIN",    new[] { "MIN", "MIN1D", "RACKMIN" } },
                            { "ROP",    new[] { "ROP", "ROP2D" } },
                            { "MAX",    new[] { "MAX", "MAX3D", "RACKMAX" } }
                        };

                        // Specific Indexer: Favors Exact Match from Aliases after Aggressive Cleaning
                        int FindExact(params string[] aliases) {
                            foreach (var a in aliases) {
                                var cleanA = System.Text.RegularExpressions.Regex.Replace(a, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
                                if (cleanedHeaders.ContainsKey(cleanA)) return cleanedHeaders[cleanA];
                            }
                            return -1;
                        }
                        
                        var colMap = new {
                            Lokasi   = FindExact("LOKASI RACK", "LOKASIRACK"),
                            Plant    = FindExact("PROD PLANT", "PROD. PLANT", "PRODPLANT"),
                            Rak      = FindExact("RAK"),
                            NoRak    = FindExact("NO RAK", "NORAK"),
                            Cust     = FindExact("CUST"),
                            Status   = FindExact("STATUS"),
                            Prod     = FindExact("PROD", "PROD."), 
                            Vin      = FindExact("VIN"),
                            Qpc      = FindExact("QPC"),
                            Min      = FindExact("MIN 1D", "MIN1D", "MIN"),
                            Rop      = FindExact("ROP 2D", "ROP2D", "ROP"),
                            Max      = FindExact("MAX 3D", "MAX3D", "MAX")
                        };

                        if (colMap.Lokasi == -1 || colMap.Vin == -1) {
                            var list = string.Join(", ", cleanedHeaders.Keys.Take(10));
                            TempData["ErrorMessage"] = $"Header tidak ditemukan. Dibutuhkan 'LOKASI RACK' & 'VIN'.<br/>Terdeteksi: {list}";
                            return RedirectToAction(nameof(Index));
                        }

                        // Process Range (v5.2 LEGACY MODE: Resilient toward all Excel structures)
                        var rows = worksheet.RowsUsed().Skip(headerRow.RowNumber());

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            try
                            {
                                // 1. Direct Extraction via pre-calculated indices
                                string GetS(int c) => c != -1 ? GetSafeString(row.Cell(c)) : "";
                                string GetN(int c) => c != -1 ? GetSafeNumberString(row.Cell(c)) : "0";

                                var dto = new ItemDto {
                                    ItemCode  = GetS(colMap.Lokasi),
                                    Plant     = GetS(colMap.Plant),
                                    Rack      = GetS(colMap.Rak),
                                    NoRackStr = GetS(colMap.NoRak),
                                    Customer  = GetS(colMap.Cust),
                                    StatusStr = GetS(colMap.Status),
                                    Category  = GetS(colMap.Prod),
                                    VIN       = GetS(colMap.Vin),
                                    QpcStr    = GetN(colMap.Qpc),
                                    MinStr    = GetN(colMap.Min),
                                    RopStr    = GetN(colMap.Rop),
                                    MaxStr    = GetN(colMap.Max)
                                };

                                if (string.IsNullOrWhiteSpace(dto.VIN) && string.IsNullOrWhiteSpace(dto.ItemCode)) continue;
                                
                                // SAFETY: ItemName (Lokasi Rack) is REQUIRED in DB. Fallback to VIN if empty.
                                if (string.IsNullOrWhiteSpace(dto.ItemCode)) dto.ItemCode = dto.VIN;

                                // 2. Robust Uniqueness (v4.5): 7-Column Identity
                                int? noRakInt = ParseInt(dto.NoRackStr);
                                
                                var existingItem = _context.Items.Local
                                    .FirstOrDefault(i => i.VIN == dto.VIN && 
                                                        i.ItemName == dto.ItemCode && 
                                                        i.Rack == dto.Rack && 
                                                        i.NoRack == noRakInt &&
                                                        i.Plant == dto.Plant &&
                                                        i.Customer == dto.Customer &&
                                                        i.Category == dto.Category);

                                if (existingItem == null)
                                {
                                    existingItem = await _context.Items
                                        .FirstOrDefaultAsync(i => i.VIN == dto.VIN && 
                                                                i.ItemName == dto.ItemCode && 
                                                                i.Rack == dto.Rack && 
                                                                i.NoRack == noRakInt &&
                                                                i.Plant == dto.Plant &&
                                                                i.Customer == dto.Customer &&
                                                                i.Category == dto.Category);
                                }

                                 // 3. Update or Create
                                 if (existingItem != null)
                                 {
                                     UpdateItem(existingItem, dto);
                                 }
                                else
                                {
                                    var newItem = CreateItem(dto);
                                    // Use a stable ItemCode logic: ItemCode is internal GUID
                                    _context.Items.Add(newItem);
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) {
                                    var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                    errorSamples.Add($"Baris {row.RowNumber()}: {innerMsg}");
                                }
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();
                
                if (successCount > 0) 
                {
                    TempData["SuccessMessage"] = $"✅ 100% SINKRONISASI (v5.9)! {successCount} baris berhasil diimport dengan konversi type-safe.";
                }
                if (errorCount > 0) {
                    var errorDetails = errorSamples.Count > 0 ? "<br/><br/>Contoh error:<br/>" + string.Join("<br/>", errorSamples) : "";
                    TempData["ErrorMessage"] = $"❌ {errorCount} baris gagal diimport.{errorDetails}";
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " | " + ex.InnerException.Message : "";
                TempData["ErrorMessage"] = "Terjadi kesalahan fatal: " + ex.Message + innerMsg;
            }

            return RedirectToAction(nameof(Index));
        }

        // --- Helper Methods (v2.7 Type-Safe) ---

        private ItemDto ExtractItemDataByHeader(ClosedXML.Excel.IXLRow row, Dictionary<string, int> map)
        {
            var dto = new ItemDto();
            
            // Helper function using Cleaned Header mapping
            // 4.1 Precision Keyword Search: Detect "MIN", "ROP", "MAX" even if in different row or with junk text
            string GetVal(string header) => map.ContainsKey(header) ? GetSafeString(row.Cell(map[header])) : "";
            string GetNum(string header) => map.ContainsKey(header) ? GetSafeNumberString(row.Cell(map[header])) : "0";

            // Fallback for numeric columns: Match by substring to handle multi-line Alt-Enter headers
            string GetNumFuzzy(string keyword) {
                // Try exact match first
                if (map.ContainsKey(keyword)) return GetSafeNumberString(row.Cell(map[keyword]));
                // Try keyword match
                var key = map.Keys.FirstOrDefault(k => k.Contains(keyword));
                return key != null ? GetSafeNumberString(row.Cell(map[key])) : "0";
            }

            dto.ItemCode  = GetVal("LOKASIRACK");
            dto.Plant     = GetVal("PRODPLANT");
            dto.Rack      = GetVal("RAK");
            dto.NoRackStr = GetVal("NORAK");
            dto.Customer  = GetVal("CUST");
            dto.StatusStr = GetVal("STATUS");
            dto.Category  = GetVal("PROD");
            dto.VIN       = GetVal("VIN");
            
            dto.QpcStr    = GetNumFuzzy("QPC");
            dto.MinStr    = GetNumFuzzy("MIN");
            dto.RopStr    = GetNumFuzzy("ROP");
            dto.MaxStr    = GetNumFuzzy("MAX");

            return dto;
        }

        // --- v5.9 TYPE-SAFE PARSER: Zero IConvertible Errors ---
        private string GetSafeNumberString(ClosedXML.Excel.IXLCell cell)
        {
            if (cell == null || cell.IsEmpty() || cell.Value.IsBlank || cell.Value.IsError) return "0";
            
            try
            {
                // 1. Try direct double conversion (handles formulas & numbers)
                if (cell.TryGetValue(out double dVal)) {
                    if (double.IsNaN(dVal) || double.IsInfinity(dVal)) return "0";
                    return Math.Round(dVal).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // 2. Fallback: Safe string extraction
                string raw = cell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(raw) || raw == "0" || raw == "-") return "0";

                // Clean all symbols except digits, dots, commas, minus
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"[^0-9.,\-]", "");
                if (string.IsNullOrWhiteSpace(raw)) return "0";
                
                // Pure digits check
                if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pure)) {
                    return Math.Round(pure).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Handle Mixed Separators (Indo: 1.050,50 or Intl: 1,050.50)
                if (raw.Contains(".") && raw.Contains(",")) {
                    if (raw.LastIndexOf('.') > raw.LastIndexOf(',')) raw = raw.Replace(",", "");
                    else raw = raw.Replace(".", "").Replace(",", ".");
                }
                // Handle Single Separator
                else if (raw.Contains(".") || raw.Contains(",")) {
                    char sep = raw.Contains(".") ? '.' : ',';
                    string[] parts = raw.Split(sep);
                    if (parts.Length == 2 && parts.Last().Length == 3) raw = raw.Replace(sep.ToString(), "");
                    else raw = raw.Replace(sep.ToString(), ".");
                }

                if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) {
                    return Math.Round(parsed).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return "0";
            }
            catch { 
                return "0"; 
            }
        }

        private string GetSafeString(ClosedXML.Excel.IXLCell cell)
        {
            try
            {
                if (cell == null || cell.Value.IsBlank) return "";
                
                // v3.8 Pure Raw Extraction: Preserve everything exactly as formatted in Excel
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
