using System.Collections.Generic;

namespace DeliveryControl.Models
{
    public class StockDashboardViewModel
    {
        public string PlantName { get; set; } = string.Empty;
        public List<PoolingRecord> RecentPooling { get; set; } = new List<PoolingRecord>();
        public List<PreparationRecord> RecentPreparation { get; set; } = new List<PreparationRecord>();
        
        public List<StockItemDetail> StockDetails { get; set; } = new List<StockItemDetail>();
        
        public int ShortageCount { get; set; }
        public int NormalCount { get; set; }
        public int OverCount { get; set; }

        public int TotalPoolingToday { get; set; }
        public int TotalPreparationToday { get; set; }
        
        public int NetStock => StockDetails.Count; // Updated to show current actual stock
    }

    public class StockItemDetail
    {
        public int No { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Shortage, Normal, Over
    }
}
