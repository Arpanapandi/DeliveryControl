using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;

namespace DeliveryControl.Data
{
    /// <summary>
    /// Database Context untuk Delivery Control System
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets untuk semua entities
        public DbSet<Customer> Customers { get; set; }
        public DbSet<DeliverySchedule> DeliverySchedules { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<DeliveryItem> DeliveryItems { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<PoolingRecord> PoolingRecords { get; set; }
        public DbSet<PreparationRecord> PreparationRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Customer
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasIndex(e => e.CustomerCode).IsUnique();
                entity.Property(e => e.CustomerCode).IsRequired();
                entity.Property(e => e.CustomerName).IsRequired();
            });

            // Configure Item
            modelBuilder.Entity<Item>(entity =>
            {
                entity.HasIndex(e => e.ItemCode).IsUnique();
                entity.Property(e => e.ItemCode).IsRequired();
                entity.Property(e => e.ItemName).IsRequired();
            });

            // Configure DeliverySchedule
            modelBuilder.Entity<DeliverySchedule>(entity =>
            {
                entity.HasIndex(e => e.ScheduleNumber).IsUnique();
                
                entity.HasOne(ds => ds.Customer)
                    .WithMany(c => c.DeliverySchedules)
                    .HasForeignKey(ds => ds.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Index untuk pencarian berdasarkan tanggal dan status
                entity.HasIndex(e => e.ScheduledDate);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.CustomerId, e.ScheduledDate });
            });

            // Configure DeliveryItem
            modelBuilder.Entity<DeliveryItem>(entity =>
            {
                entity.HasOne(di => di.DeliverySchedule)
                    .WithMany(ds => ds.DeliveryItems)
                    .HasForeignKey(di => di.ScheduleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(di => di.Item)
                    .WithMany(i => i.DeliveryItems)
                    .HasForeignKey(di => di.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.ScheduleId, e.ItemId });
            });

            // Configure User
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.Username).IsRequired();
                entity.Property(e => e.Password).IsRequired();
                entity.Property(e => e.FullName).IsRequired();
                entity.Property(e => e.Role).IsRequired();
            });

            // Seed Data (Optional - untuk development)
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed Customers
            modelBuilder.Entity<Customer>().HasData(
                new Customer
                {
                    CustomerId = 1,
                    CustomerCode = "CUST001",
                    CustomerName = "PT ABC Manufacturing",
                    Route = "Route A",
                    SKID = "10 SKID",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Customer
                {
                    CustomerId = 2,
                    CustomerCode = "CUST002",
                    CustomerName = "PT XYZ Industries",
                    Route = "Route B",
                    SKID = "15 SKID",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }
            );

            // Seed Items
            modelBuilder.Entity<Item>().HasData(
                new Item
                {
                    ItemId = 1,
                    ItemCode = "ITM001",
                    ItemName = "Raw Material A",
                    Description = "Raw material untuk produksi",
                    Unit = "KG",
                    Category = "Raw Material",
                    Weight = 1.0m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Item
                {
                    ItemId = 2,
                    ItemCode = "ITM002",
                    ItemName = "Finished Product B",
                    Description = "Produk jadi siap kirim",
                    Unit = "PCS",
                    Category = "Finished Goods",
                    Weight = 2.5m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Item
                {
                    ItemId = 3,
                    ItemCode = "ITM003",
                    ItemName = "Packaging Material",
                    Description = "Material packaging",
                    Unit = "BOX",
                    Category = "Packaging",
                    Weight = 0.5m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }
            );

            // Configure SystemSetting
            modelBuilder.Entity<SystemSetting>(entity =>
            {
                entity.HasIndex(e => e.Key).IsUnique();
                entity.Property(e => e.Key).IsRequired();
                entity.Property(e => e.Value).IsRequired();
            });

            // Configure ActivityLog
            modelBuilder.Entity<ActivityLog>(entity =>
            {
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Module);
                entity.HasIndex(e => new { e.Module, e.Action });
                entity.Property(e => e.Module).IsRequired();
                entity.Property(e => e.Action).IsRequired();
                entity.Property(e => e.PerformedBy).IsRequired();
            });

            // Note: User seed data akan dibuat via migration atau manual
            // Default users:
            // - Username: admin, Password: admin123, Role: Admin
            // - Username: user, Password: user123, Role: User
        }
    }
}

