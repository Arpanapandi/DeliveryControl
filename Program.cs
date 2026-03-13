using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Hubs;
using DeliveryControl.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options => 
{
    options.Filters.Add<DeliveryControl.Filters.AuthorizeAttribute>();
});

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add Session untuk authentication
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30); // Sesi bertahan 30 hari idle
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".DeliveryControl.Session";
    options.Cookie.MaxAge = TimeSpan.FromDays(30); // Membuat cookie persistent (tahan banting setelah browser ditutup)
});

// Add DbContext
// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (connectionString.Contains("Server=") || connectionString.Contains("Database="))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Add HttpContextAccessor (untuk ActivityLogService)
builder.Services.AddHttpContextAccessor();

// Add ActivityLogService
builder.Services.AddScoped<ActivityLogService>();

// Add PreparationSyncService
builder.Services.AddScoped<PreparationSyncService>();

// Add SmartImportService
builder.Services.AddScoped<SmartImportService>();

// Add StockSnapshotService (background service cutoff jam 08:00)
builder.Services.AddSingleton<StockSnapshotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StockSnapshotService>());

var app = builder.Build();

// Initialize database dengan seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        // Pastikan database sudah di-migrate
        try
        {
            // context.Database.Migrate();
            // logger.LogInformation("Database migration completed successfully.");
        }
        catch (Exception migrateEx)
        {
            logger.LogWarning(migrateEx, "Migration warning (database might already be up to date).");
        }

        // Idempotent schema updates for SQL Server
        try 
        { 
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PullingRecords' AND COLUMN_NAME = 'Remark')
                BEGIN
                    ALTER TABLE [PullingRecords] ADD [Remark] NVARCHAR(20) NOT NULL DEFAULT 'Match';
                END
            "); 
            logger.LogInformation("Ensured Remark column on PullingRecords."); 
        } 
        catch (Exception ex) { logger.LogError("PullingRecords Remark Error: {Msg}", ex.Message); }

        try 
        { 
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PreparationRecords' AND COLUMN_NAME = 'Remark')
                BEGIN
                    ALTER TABLE [PreparationRecords] ADD [Remark] NVARCHAR(20) NOT NULL DEFAULT 'Match';
                END
            "); 
            logger.LogInformation("Ensured Remark column on PreparationRecords."); 
        } 
        catch (Exception ex) { logger.LogError("PreparationRecords Remark Error: {Msg}", ex.Message); }

        try
        {
            context.Database.ExecuteSqlRaw(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ScanNGLogs')
                BEGIN
                    CREATE TABLE [ScanNGLogs] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [Module] NVARCHAR(100) NOT NULL DEFAULT '',
                        [Tag] NVARCHAR(100) NOT NULL DEFAULT '',
                        [Label] NVARCHAR(100) NOT NULL DEFAULT '',
                        [Kanban] NVARCHAR(100) NOT NULL DEFAULT '',
                        [Reason] NVARCHAR(MAX) NOT NULL DEFAULT '',
                        [CreatedBy] NVARCHAR(100) NOT NULL DEFAULT '',
                        [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
                    )
                END
            ");
            logger.LogInformation("Ensured ScanNGLogs table exists.");
        }
        catch (Exception ex) { logger.LogError("ScanNGLogs Error: {Msg}", ex.Message); }

        // Seed Users
        DeliveryControl.Data.DbInitializer.Initialize(context);
        logger.LogInformation("Database initialization completed.");

        // Manual seeding trigger via CLI argument: --seed-stock
        if (args.Contains("--seed-stock"))
        {
            logger.LogInformation("Flag --seed-stock detected. Seeding historical data...");
            DeliveryControl.Data.DbInitializer.SeedHistoricalStockSnapshots(context);
            logger.LogInformation("Historical data seeding completed.");
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
        // Jangan stop aplikasi jika seed data gagal
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // Temporarily enabled to debug 500 error in PRODUCTION
    app.UseDeveloperExceptionPage();
    // app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// Map SignalR Hub
app.MapHub<DeliveryHub>("/deliveryHub");
app.MapHub<StockHub>("/stockHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
