using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;

namespace DeliveryControl.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string searchString)
        {
            ViewData["CurrentFilter"] = searchString;

            var customers = _context.Customers
                .Include(c => c.DeliverySchedules)
                .AsNoTracking();

            if (!String.IsNullOrEmpty(searchString))
            {
                customers = customers.Where(c => c.CustomerCode.Contains(searchString)
                                       || c.CustomerName.Contains(searchString));
            }

            return View(await customers.OrderBy(c => c.CustomerCode).ToListAsync());
        }

        // GET: Customers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id);

            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // GET: Customers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId,CustomerName,Route,Cycle,Docking,Pickup,ETD,SKID,Area,IsActive")] Customer customer)
        {
            // Remove CustomerCode from ModelState since it's auto-generated
            ModelState.Remove("CustomerCode");
            ModelState.Remove("Range"); // Range is auto-calculated
            
            if (ModelState.IsValid)
            {
                // Auto-generate CustomerCode
                customer.CustomerCode = await GenerateCustomerCodeAsync();
                customer.CreatedDate = DateTime.Now;
                
                // Auto-calculate Range dari ETD - Pickup
                customer.CalculateRange();
                
                _context.Add(customer);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Customer berhasil ditambahkan dengan kode {customer.CustomerCode}!";
                return RedirectToAction(nameof(Index));
            }
            return View(customer);
        }
        
        // Helper method to generate CustomerCode
        private async Task<string> GenerateCustomerCodeAsync()
        {
            var lastCustomer = await _context.Customers
                .OrderByDescending(c => c.CustomerId)
                .FirstOrDefaultAsync();
            
            if (lastCustomer == null)
            {
                return "CUST001";
            }
            
            // Extract number from last code (e.g., CUST001 -> 001)
            var lastCode = lastCustomer.CustomerCode;
            if (lastCode.StartsWith("CUST") && lastCode.Length > 4)
            {
                var numberPart = lastCode.Substring(4);
                if (int.TryParse(numberPart, out int lastNumber))
                {
                    var newNumber = lastNumber + 1;
                    return $"CUST{newNumber:D3}"; // Format: CUST001, CUST002, etc.
                }
            }
            
            // Fallback: count total + 1
            var totalCount = await _context.Customers.CountAsync();
            return $"CUST{(totalCount + 1):D3}";
        }

        // GET: Customers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == id);
            
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        // POST: Customers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CustomerId,CustomerCode,CustomerName,Route,Cycle,Docking,Pickup,ETD,SKID,Area,IsActive,CreatedDate")] Customer customer)
        {
            if (id != customer.CustomerId)
            {
                return NotFound();
            }

            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedules");
            ModelState.Remove("Range"); // Range is auto-calculated

            if (ModelState.IsValid)
            {
                try
                {
                    customer.UpdatedDate = DateTime.Now;
                    
                    // Auto-calculate Range dari ETD - Pickup
                    customer.CalculateRange();
                    
                    _context.Update(customer);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Customer berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CustomerExists(customer.CustomerId))
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
            
            // If called from modal, return to Index
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return RedirectToAction(nameof(Index));
            }
            
            return View(customer);
        }

        // GET: Customers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // Tidak perlu Include disini, cukup load data customer saja untuk performa
            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id);
            
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // POST: Customers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, bool forceDelete = false)
        {
            var customer = await _context.Customers
                .Include(c => c.DeliverySchedules)
                    .ThenInclude(ds => ds.DeliveryItems)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer != null)
            {
                // Cek apakah ada relasi dengan DeliverySchedule
                if (customer.DeliverySchedules.Any())
                {
                    if (!forceDelete)
                    {
                        TempData["ErrorMessage"] = $"Tidak dapat menghapus customer! Masih ada {customer.DeliverySchedules.Count} schedule yang terkait. Hapus schedule terlebih dahulu.";
                        TempData["ForceDeleteId"] = id; // Store ID untuk force delete option
                        return RedirectToAction(nameof(Index));
                    }
                    
                    // Force delete: Hapus semua relasi terlebih dahulu
                    try
                    {
                        // Hapus semua DeliveryItems yang terkait dengan DeliverySchedules
                        foreach (var schedule in customer.DeliverySchedules)
                        {
                            if (schedule.DeliveryItems.Any())
                            {
                                _context.DeliveryItems.RemoveRange(schedule.DeliveryItems);
                            }
                        }
                        
                        // Hapus semua DeliverySchedules
                        _context.DeliverySchedules.RemoveRange(customer.DeliverySchedules);
                        
                        // Hapus Customer
                        _context.Customers.Remove(customer);
                        await _context.SaveChangesAsync();
                        
                        TempData["SuccessMessage"] = $"Customer dan {customer.DeliverySchedules.Count} schedule terkait berhasil dihapus!";
                    }
                    catch (DbUpdateException ex)
                    {
                        TempData["ErrorMessage"] = $"Error saat menghapus customer: {ex.InnerException?.Message ?? ex.Message}";
                    }
                }
                else
                {
                    try
                    {
                        _context.Customers.Remove(customer);
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = "Customer berhasil dihapus!";
                    }
                    catch (DbUpdateException ex)
                    {
                        TempData["ErrorMessage"] = $"Error saat menghapus customer: {ex.InnerException?.Message ?? ex.Message}";
                    }
                }
            }

            return RedirectToAction(nameof(Index));
        }
        
        // POST: Force Delete Customer dengan semua relasi
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceDelete(int id)
        {
            try
            {
                var customer = await _context.Customers
                    .Include(c => c.DeliverySchedules)
                        .ThenInclude(ds => ds.DeliveryItems)
                    .FirstOrDefaultAsync(c => c.CustomerId == id);

                if (customer == null)
                {
                    TempData["ErrorMessage"] = "Customer tidak ditemukan!";
                    return RedirectToAction(nameof(Index));
                }

                int scheduleCount = customer.DeliverySchedules.Count;
                int itemCount = customer.DeliverySchedules.Sum(s => s.DeliveryItems.Count);

                // Hapus semua DeliveryItems terlebih dahulu
                foreach (var schedule in customer.DeliverySchedules)
                {
                    if (schedule.DeliveryItems.Any())
                    {
                        _context.DeliveryItems.RemoveRange(schedule.DeliveryItems);
                    }
                }

                // Hapus semua DeliverySchedules
                if (customer.DeliverySchedules.Any())
                {
                    _context.DeliverySchedules.RemoveRange(customer.DeliverySchedules);
                }

                // Hapus Customer
                _context.Customers.Remove(customer);
                
                // Save changes
                await _context.SaveChangesAsync();

                if (scheduleCount > 0 || itemCount > 0)
                {
                    TempData["SuccessMessage"] = $"✅ Berhasil menghapus Customer '{customer.CustomerCode}' beserta {scheduleCount} schedule dan {itemCount} delivery items!";
                }
                else
                {
                    TempData["SuccessMessage"] = $"✅ Customer '{customer.CustomerCode}' berhasil dihapus!";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat force delete: {ex.InnerException?.Message ?? ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CustomerExists(int id)
        {
            return _context.Customers.Any(e => e.CustomerId == id);
        }

        // Bulk Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada customer yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',').Select(int.Parse).ToList();
            var customersToDelete = await _context.Customers
                .Include(c => c.DeliverySchedules)
                .Where(c => ids.Contains(c.CustomerId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var customer in customersToDelete)
            {
                // Cek relasi

                if (customer.DeliverySchedules.Any())
                {
                    errorMessages.Add($"{customer.CustomerCode}: Masih ada {customer.DeliverySchedules.Count} schedule");
                    errorCount++;
                    continue;
                }

                try
                {
                    _context.Customers.Remove(customer);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{customer.CustomerCode}: {ex.Message}");
                    errorCount++;
                }
            }

            if (successCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {successCount} customer!";
            }

            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = $"⚠️ {errorCount} customer gagal dihapus: {string.Join(", ", errorMessages.Take(5))}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Customer");

                // Header matches the application table
                var headers = new[] { 
                    "KODE CUSTOMER", "NAMA CUSTOMER", "ROUTE", "CYCLE", 
                    "DOCKING", "PICKUP", "ETD", "RANGE", "SKID", "AREA" 
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f172a"); // Dark theme match
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                // Example data (Row 2) - Use REAL DATA from Database
                var sampleCustomer = _context.Customers.FirstOrDefault();
                if (sampleCustomer != null)
                {
                    worksheet.Cell(2, 1).Value = sampleCustomer.CustomerCode;
                    worksheet.Cell(2, 2).Value = sampleCustomer.CustomerName;
                    worksheet.Cell(2, 3).Value = sampleCustomer.Route;
                    worksheet.Cell(2, 4).Value = sampleCustomer.Cycle;
                    worksheet.Cell(2, 5).Value = sampleCustomer.Docking;
                    worksheet.Cell(2, 6).Value = sampleCustomer.Pickup;
                    worksheet.Cell(2, 7).Value = sampleCustomer.ETD;
                    // Range might be null or computed
                    worksheet.Cell(2, 8).Value = sampleCustomer.Range ?? ""; 
                    worksheet.Cell(2, 9).Value = sampleCustomer.SKID;
                    worksheet.Cell(2, 10).Value = sampleCustomer.Area;
                }
                else
                {
                    // Fallback
                    worksheet.Cell(2, 1).Value = "CUST001"; 
                    worksheet.Cell(2, 2).Value = "PT. ORIGINAL EQUIPMENT";
                    worksheet.Cell(2, 3).Value = "RC25";
                    worksheet.Cell(2, 4).Value = "C1";
                    worksheet.Cell(2, 5).Value = "21:00";
                    worksheet.Cell(2, 6).Value = "04:00";
                    worksheet.Cell(2, 7).Value = "04:30";
                    worksheet.Cell(2, 8).Value = "30 Menit";
                    worksheet.Cell(2, 9).Value = "4 - 8"; 
                    worksheet.Cell(2, 10).Value = "A1-2"; 
                }

                // Instructions
                worksheet.Cell(4, 2).Value = "CATATAN PENGISIAN UNTUK IMPORT BARU:"; 
                worksheet.Cell(5, 2).Value = "• Gunakan template ini untuk MENAMBAH data baru.";
                worksheet.Cell(6, 2).Value = "• KODE CUSTOMER: Boleh dikosongkan (Sistem akan generate otomatis)";
                worksheet.Cell(7, 2).Value = "• NAMA CUSTOMER: Wajib diisi";

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Input_Customer.xlsx");
                }
            }
        }

        // Export All Data (For Backup/Reporting)
        public async Task<IActionResult> ExportExcel()
        {
            var customers = await _context.Customers.OrderBy(c => c.CustomerCode).AsNoTracking().ToListAsync();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Data Customer");

                // Headers
                var headers = new[] { 
                    "KODE CUSTOMER", "NAMA CUSTOMER", "ROUTE", "CYCLE", 
                    "DOCKING", "PICKUP", "ETD", "RANGE", "SKID", "AREA" 
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Header Style
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#15803d"); // Green for Export
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Data Rows
                int row = 2;
                foreach (var item in customers)
                {
                    worksheet.Cell(row, 1).Value = item.CustomerCode;
                    worksheet.Cell(row, 2).Value = item.CustomerName;
                    worksheet.Cell(row, 3).Value = item.Route;
                    worksheet.Cell(row, 4).Value = item.Cycle;
                    worksheet.Cell(row, 5).Value = item.Docking;
                    worksheet.Cell(row, 6).Value = item.Pickup;
                    worksheet.Cell(row, 7).Value = item.ETD;
                    worksheet.Cell(row, 8).Value = item.Range;
                    worksheet.Cell(row, 9).Value = item.SKID;
                    worksheet.Cell(row, 10).Value = item.Area;
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Data_Customer_{DateTime.Now:yyyyMMdd}.xlsx");
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
                return RedirectToAction(nameof(Create));
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToAction(nameof(Create));
            }

            var customers = new List<Customer>();
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
                        
                        // Get starting customer code ONCE before loop
                        var lastCustomer = await _context.Customers
                            .OrderByDescending(c => c.CustomerId)
                            .FirstOrDefaultAsync();
                        
                        int startNumber = 1;
                        if (lastCustomer != null && lastCustomer.CustomerCode.StartsWith("CUST"))
                        {
                            var numberPart = lastCustomer.CustomerCode.Substring(4);
                            if (int.TryParse(numberPart, out int lastNumber))
                            {
                                startNumber = lastNumber + 1;
                            }
                        }

                        int currentNumber = startNumber;

                        foreach (var row in rows)
                        {
                            try
                            {
                                // Updated Mapping (Matches User Request):
                                // 1: KODE (Ignored/System Gen)
                                // 2: NAMA CUSTOMER
                                // 3: ROUTE
                                // 4: CYCLE
                                // 5: DOCKING
                                // 6: PICKUP
                                // 7: ETD
                                // 8: RANGE (Ignored/Auto Calc)
                                // 9: SKID
                                // 10: AREA

                                var customerName = row.Cell(2).GetString().Trim();
                                var route = row.Cell(3).GetString().Trim();
                                var cycle = row.Cell(4).GetString().Trim();
                                var docking = row.Cell(5).GetString().Trim();
                                var pickup = row.Cell(6).GetString().Trim();
                                var etd = row.Cell(7).GetString().Trim();
                                // Col 8 is Range
                                var skidStr = row.Cell(9).GetString().Trim();
                                var area = row.Cell(10).GetString().Trim();

                                // Skip invalid rows
                                if (string.IsNullOrWhiteSpace(customerName) || 
                                    customerName.StartsWith("Catatan", StringComparison.OrdinalIgnoreCase) ||
                                    customerName.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                if (customerName.Length < 3)
                                {
                                    errorMessages.Add($"Baris {row.RowNumber()}: Nama customer '{customerName}' terlalu pendek.");
                                    errorCount++;
                                    continue;
                                }

                                // Generate CustomerCode
                                var customerCode = $"CUST{currentNumber:D3}";
                                currentNumber++;

                                var customer = new Customer
                                {
                                    CustomerCode = customerCode,
                                    CustomerName = customerName,
                                    Route = string.IsNullOrWhiteSpace(route) ? null : route,
                                    Cycle = string.IsNullOrWhiteSpace(cycle) ? null : cycle,
                                    Docking = string.IsNullOrWhiteSpace(docking) ? null : docking,
                                    Pickup = string.IsNullOrWhiteSpace(pickup) ? null : pickup,
                                    ETD = string.IsNullOrWhiteSpace(etd) ? null : etd,
                                    SKID = string.IsNullOrWhiteSpace(skidStr) ? null : skidStr,
                                    Area = string.IsNullOrWhiteSpace(area) ? null : area,
                                    IsActive = true,
                                    CreatedDate = DateTime.Now
                                };

                                // Auto-calculate Range dari ETD - Pickup
                                customer.CalculateRange();

                                customers.Add(customer);
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

                if (customers.Any())
                {
                    _context.Customers.AddRange(customers);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"✅ Berhasil import {successCount} customer baru!";
                }
                else
                {
                     if (errorCount == 0 && successCount == 0)
                        TempData["ErrorMessage"] = "File kosong atau tidak ada data yang valid.";
                }

                if (errorCount > 0)
                {
                    var errorDetail = string.Join("<br/>", errorMessages.Take(10));
                    TempData["WarningMessage"] = $"{errorCount} data gagal diimport.<br/><small>{errorDetail}</small>";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat import Excel: {ex.Message}<br/>Stack: {ex.StackTrace}";
                return RedirectToAction(nameof(Create));
            }
        }
    }
}

