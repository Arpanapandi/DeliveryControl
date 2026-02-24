using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using System.Collections.Generic;

namespace DeliveryControl.Diagnostics
{
    public class GroupingCheck
    {
        private readonly ApplicationDbContext _context;
        public GroupingCheck(ApplicationDbContext context) { _context = context; }

        public async Task Run()
        {
            var today = DateTime.Today;
            // Kita coba cek data hari ini atau besok
            var enterDockDate = today.AddDays(1); // Dari screenshot sepertinya "BESOK"

            Console.WriteLine($"Checking schedules for: {enterDockDate:yyyy-MM-dd}");

            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date == enterDockDate.Date && s.Status != "Cancelled")
                .ToListAsync();

            Console.WriteLine($"Total schedules found: {allSchedules.Count}");

            var rawGroups = allSchedules
                .Select(s => new {
                    s.ScheduleId,
                    s.ScheduleNumber,
                    s.Area,
                    s.Cycle,
                    s.Route,
                    CustomerDock = s.Customer?.Docking,
                    EffectiveArea = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    ManifestBase = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(),
                    CycleGroup = (s.Cycle ?? "").Trim().ToUpper(),
                    RouteGroup = (s.Route ?? "").Trim().ToUpper()
                })
                .ToList();

            var groups = rawGroups
                .GroupBy(x => new {
                    x.ManifestBase,
                    x.CycleGroup,
                    x.RouteGroup,
                    x.EffectiveArea
                })
                .ToList();

            Console.WriteLine($"Number of groups: {groups.Count}");

            foreach (var g in groups)
            {
                Console.WriteLine($"\nGroup: Manifest={g.Key.ManifestBase}, Cycle={g.Key.CycleGroup}, Route={g.Key.RouteGroup}, Area={g.Key.EffectiveArea}");
                Console.WriteLine($"Count: {g.Count()}");
                foreach (var item in g)
                {
                    Console.WriteLine($"  - ID: {item.ScheduleId}, Num: {item.ScheduleNumber}, RawArea: '{item.Area}', CustDock: '{item.CustomerDock}'");
                }
            }
        }
    }
}
