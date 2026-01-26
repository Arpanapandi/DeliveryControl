using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            // Pastikan database sudah di-migrate
            try
            {
                context.Database.Migrate();
            }
            catch
            {
                // Jika migration gagal, mungkin database belum ada atau sudah ada
                // Lanjutkan ke seed data
            }

            // Seed Users jika belum ada
            if (!context.Users.Any())
            {
                var users = new User[]
                {
                    new User
                    {
                        Username = "admin",
                        Password = BCrypt.Net.BCrypt.HashPassword("admin123"), // Password: admin123
                        FullName = "Administrator",
                        Email = "admin@deliverycontrol.com",
                        Role = "Admin",
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    },
                    new User
                    {
                        Username = "user",
                        Password = BCrypt.Net.BCrypt.HashPassword("user123"), // Password: user123
                        FullName = "User Test",
                        Email = "user@deliverycontrol.com",
                        Role = "User",
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    }
                };

                context.Users.AddRange(users);
                context.SaveChanges();
            }
        }
    }
}

