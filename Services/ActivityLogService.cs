using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.AspNetCore.Http;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Service untuk mencatat aktivitas di sistem
    /// </summary>
    public class ActivityLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ActivityLogService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Log aktivitas ke database
        /// </summary>
        public async Task LogActivity(
            string module,
            string action,
            string? entityName = null,
            int? entityId = null,
            string? description = null,
            string? oldData = null,
            string? newData = null,
            string? performedBy = null)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString();
                var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();
                var user = performedBy ?? httpContext?.User?.Identity?.Name ?? "System";

                var log = new ActivityLog
                {
                    Module = module,
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    Description = description,
                    OldData = oldData,
                    NewData = newData,
                    Timestamp = DateTime.Now,
                    PerformedBy = user,
                    IpAddress = ipAddress,
                    UserAgent = userAgent
                };

                _context.ActivityLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log error tapi jangan sampai mengganggu proses utama
                Console.WriteLine($"Error logging activity: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // Untuk debugging, bisa uncomment baris berikut:
                // throw;
            }
        }

        /// <summary>
        /// Log Create activity
        /// </summary>
        public async Task LogCreate(string module, string entityName, int entityId, string description, string? performedBy = null)
        {
            await LogActivity(module, "Create", entityName, entityId, description, performedBy: performedBy);
        }

        /// <summary>
        /// Log Update activity
        /// </summary>
        public async Task LogUpdate(string module, string entityName, int entityId, string description, string? oldData = null, string? newData = null, string? performedBy = null)
        {
            await LogActivity(module, "Update", entityName, entityId, description, oldData, newData, performedBy);
        }

        /// <summary>
        /// Log Delete activity
        /// </summary>
        public async Task LogDelete(string module, string entityName, int entityId, string description, string? performedBy = null)
        {
            await LogActivity(module, "Delete", entityName, entityId, description, performedBy: performedBy);
        }

        /// <summary>
        /// Log Confirm activity (untuk Driver/Preparation)
        /// </summary>
        public async Task LogConfirm(string module, string entityName, int entityId, string description, string? performedBy = null)
        {
            await LogActivity(module, "Confirm", entityName, entityId, description, performedBy: performedBy);
        }
    }
}

