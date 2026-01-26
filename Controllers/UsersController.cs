using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Data;
using BCrypt.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    [AuthorizeAdmin]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ApplicationDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Helper method untuk check authentication dan authorization
        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("UserId") != null;
        }

        private bool IsAdmin()
        {
            return HttpContext.Session.GetString("Role") == "Admin";
        }

        // GET: Users
        public async Task<IActionResult> Index()
        {
            if (!IsAuthenticated())
            {
                return RedirectToAction("Login", "Account");
            }

            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk halaman ini";
                return RedirectToAction("Index", "Home");
            }

            var users = await _context.Users
                .OrderBy(u => u.Username)
                .ToListAsync();
            
            return View(users);
        }

        // GET: Users/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(m => m.UserId == id);
            
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // GET: Users/Create
        public IActionResult Create()
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.Roles = new SelectList(new[] { "Admin", "User" });
            return View();
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Username,Password,FullName,Email,Role,IsActive,ConfirmPassword")] User user)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            // Check username sudah ada
            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
            {
                ModelState.AddModelError("Username", "Username sudah digunakan");
            }

            if (ModelState.IsValid)
            {
                // Hash password
                user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                user.CreatedDate = DateTime.Now;
                
                _context.Add(user);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "User berhasil ditambahkan";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Roles = new SelectList(new[] { "Admin", "User" }, user.Role);
            return View(user);
        }

        // GET: Users/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Jangan tampilkan password yang sudah di-hash
            user.Password = "";
            ViewBag.Roles = new SelectList(new[] { "Admin", "User" }, user.Role);
            return View(user);
        }

        // POST: Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("UserId,Username,Password,FullName,Email,Role,IsActive,ConfirmPassword")] User user)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id != user.UserId)
            {
                return NotFound();
            }

            // Check username sudah ada (kecuali user yang sedang diedit)
            if (await _context.Users.AnyAsync(u => u.Username == user.Username && u.UserId != id))
            {
                ModelState.AddModelError("Username", "Username sudah digunakan");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.Users.FindAsync(id);
                    if (existingUser == null)
                    {
                        return NotFound();
                    }

                    // Update fields
                    existingUser.Username = user.Username;
                    existingUser.FullName = user.FullName;
                    existingUser.Email = user.Email;
                    existingUser.Role = user.Role;
                    existingUser.IsActive = user.IsActive;
                    existingUser.UpdatedDate = DateTime.Now;

                    // Update password hanya jika diisi
                    if (!string.IsNullOrWhiteSpace(user.Password))
                    {
                        existingUser.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                    }

                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "User berhasil diupdate";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.UserId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            ViewBag.Roles = new SelectList(new[] { "Admin", "User" }, user.Role);
            return View(user);
        }

        // GET: Users/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(m => m.UserId == id);
            
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: Users/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                // Jangan hapus user yang sedang login
                var currentUserId = HttpContext.Session.GetString("UserId");
                if (currentUserId == user.UserId.ToString())
                {
                    TempData["ErrorMessage"] = "Tidak dapat menghapus user yang sedang login";
                    return RedirectToAction(nameof(Index));
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "User berhasil dihapus";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.UserId == id);
        }
    }
}

