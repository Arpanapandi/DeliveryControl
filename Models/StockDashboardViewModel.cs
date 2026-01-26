using System.Collections.Generic;

namespace DeliveryControl.Models
{
    public class StockDashboardViewModel
    {
        public string PlantName { get; set; } = string.Empty;
        public List<PoolingRecord> RecentPooling { get; set; } = new List<PoolingRecord>();
        public List<PreparationRecord> RecentPreparation { get; set; } = new List<PreparationRecord>();
        
        public int TotalPoolingToday { get; set; }
        public int TotalPreparationToday { get; set; }
        
        public int NetStock => TotalPoolingToday - TotalPreparationToday; // Simplified for today
    }
}
