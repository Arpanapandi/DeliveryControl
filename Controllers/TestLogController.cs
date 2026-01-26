using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller temporary untuk test Activity Log
    /// </summary>
    public class TestLogController : Controller
    {
        private readonly ActivityLogService _logService;

        public TestLogController(ActivityLogService logService)
        {
            _logService = logService;
        }

        // GET: TestLog/CreateTestLog
        public async Task<IActionResult> CreateTestLog()
        {
            try
            {
                await _logService.LogActivity(
                    "Test",
                    "TestAction",
                    "TEST-001",
                    1,
                    "This is a test log to verify Activity Log is working",
                    performedBy: "TestUser"
                );

                return Content("Test log created successfully! Check ActivityLogs table or /ActivityLogs/Index");
            }
            catch (Exception ex)
            {
                return Content($"Error creating test log: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
            }
        }
    }
}

