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
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

// Add HttpContextAccessor (untuk ActivityLogService)
builder.Services.AddHttpContextAccessor();

// Add ActivityLogService
builder.Services.AddScoped<ActivityLogService>();

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
            context.Database.Migrate();
            logger.LogInformation("Database migration completed successfully.");
        }
        catch (Exception migrateEx)
        {
            logger.LogWarning(migrateEx, "Migration warning (database might already be up to date).");
        }
        
        // Seed Users
        DeliveryControl.Data.DbInitializer.Initialize(context);
        logger.LogInformation("Database initialization completed.");
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
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
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
