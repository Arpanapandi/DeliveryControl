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

                // Header (urutan sesuai tabel: CUST | ROUTE | CYCLE | DOCKING | PICKUP | ETD | SKID | AREA)
                // Tanpa CUST karena auto-generate, tanpa Range karena auto-calculate
                worksheet.Cell(1, 1).Value = "Nama Customer";
                worksheet.Cell(1, 2).Value = "Route";
                worksheet.Cell(1, 3).Value = "Cycle";
                worksheet.Cell(1, 4).Value = "Docking";
                worksheet.Cell(1, 5).Value = "Pickup";
                worksheet.Cell(1, 6).Value = "ETD";
                worksheet.Cell(1, 7).Value = "SKID";
                worksheet.Cell(1, 8).Value = "Area";

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, 8);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Contoh data (baris 2)
                worksheet.Cell(2, 1).Value = "PT. ABC Indonesia";
                worksheet.Cell(2, 2).Value = "Route A";
                worksheet.Cell(2, 3).Value = "Cycle 1";
                worksheet.Cell(2, 4).Value = "08:00";
                worksheet.Cell(2, 5).Value = "10:00";
                worksheet.Cell(2, 6).Value = "12:00";
                worksheet.Cell(2, 7).Value = "10 SKID";
                worksheet.Cell(2, 8).Value = "Jakarta";

                // Catatan
                worksheet.Cell(4, 1).Value = "Catatan:";
                worksheet.Cell(5, 1).Value = "- Kode Customer akan di-generate otomatis (CUST001, CUST002, dst)";
                worksheet.Cell(6, 1).Value = "- Range akan di-calculate otomatis dari ETD - Pickup";
                worksheet.Cell(7, 1).Value = "- Nama Customer wajib diisi";
                worksheet.Cell(8, 1).Value = "- Semua field lainnya boleh kosong";
                worksheet.Cell(9, 1).Value = "- Format waktu untuk Docking, Pickup, ETD: HH:MM (contoh: 08:00, 10:30)";

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Customer.xlsx");
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
                                // Template urutan: Nama Customer | Route | Cycle | Docking | Pickup | ETD | SKID | Area
                                // Range akan di-calculate otomatis
                                // Column 1: CustomerName
                                // Column 2: Route  
                                // Column 3: Cycle
                                // Column 4: Docking
                                // Column 5: Pickup
                                // Column 6: ETD
                                // Column 7: SKID
                                // Column 8: Area
                                var customerName = row.Cell(1).GetString().Trim();
                                var route = row.Cell(2).GetString().Trim();
                                var cycle = row.Cell(3).GetString().Trim();
                                var docking = row.Cell(4).GetString().Trim();
                                var pickup = row.Cell(5).GetString().Trim();
                                var etd = row.Cell(6).GetString().Trim();
                                var skidStr = row.Cell(7).GetString().Trim();
                                var area = row.Cell(8).GetString().Trim();

                                // Skip baris kosong atau baris catatan
                                if (string.IsNullOrWhiteSpace(customerName) || 
                                    customerName.StartsWith("Catatan", StringComparison.OrdinalIgnoreCase) ||
                                    customerName.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // Validasi required fields
                                if (customerName.Length < 3)
                                {
                                    errorMessages.Add($"Baris {row.RowNumber()}: Nama customer terlalu pendek (min 3 karakter)");
                                    errorCount++;
                                    continue;
                                }

                                // Generate CustomerCode dengan increment
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
                    TempData["SuccessMessage"] = $"✅ Berhasil import {successCount} customer dengan kode otomatis!";
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
                TempData["ErrorMessage"] = $"❌ Error saat import Excel: {ex.Message}<br/>Stack: {ex.StackTrace}";
                return RedirectToAction(nameof(Create));
            }
        }
    }
}

