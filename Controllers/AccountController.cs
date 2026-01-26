using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Data;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;

        public AccountController(ApplicationDbContext context, ILogger<AccountController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Account/Login
        public IActionResult Login()
        {
            // Jika sudah login, redirect ke home
            if (HttpContext.Session.GetString("UserId") != null)
            {
                var username = HttpContext.Session.GetString("Username") ?? string.Empty;

                if (string.Equals(username, "driver", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("Index", "Driver");
                }

                if (string.Equals(username, "prepare", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(username, "preparation", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("Index", "Preparation");
                }

                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username && u.IsActive);

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
            {
                ModelState.AddModelError("", "Username atau password salah");
                return View(model);
            }

            // Set session
            HttpContext.Session.SetString("UserId", user.UserId.ToString());
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role);

            _logger.LogInformation($"User {user.Username} logged in successfully");

            // Redirect berdasarkan username / role
            if (string.Equals(user.Username, "driver", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Driver");
            }

            if (string.Equals(user.Username, "prepare", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Username, "preparation", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Preparation");
            }

            return RedirectToAction("Index", "Home");
        }

        // GET: Account/Logout
        public IActionResult Logout()
        {
            var username = HttpContext.Session.GetString("Username");
            HttpContext.Session.Clear();
            _logger.LogInformation($"User {username} logged out");
            return RedirectToAction("Login");
        }
    }

    // ViewModel untuk Login
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username wajib diisi")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password wajib diisi")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}

