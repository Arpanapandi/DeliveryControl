using System.Collections.Generic;

namespace DeliveryControl.Models
{
    public class StockDashboardViewModel
    {
        public string PlantName { get; set; } = string.Empty;
        public List<PullingRecord> RecentPulling { get; set; } = new List<PullingRecord>();
        public List<PreparationRecord> RecentPreparation { get; set; } = new List<PreparationRecord>();
        
        public List<StockItemDetail> StockDetails { get; set; } = new List<StockItemDetail>();
        
        public int ShortageCount { get; set; }
        public int NormalCount { get; set; }
        public int OverCount { get; set; }

        public int TotalPullingToday { get; set; }
        public int TotalPreparationToday { get; set; }
        public DateTime? SearchDate { get; set; }
        public string Period { get; set; } = "Day";
        
        public List<ScanNGLog> NGPulling { get; set; } = new List<ScanNGLog>();
        public List<ScanNGLog> NGPreparation { get; set; } = new List<ScanNGLog>();
        public int TotalNGPulling { get; set; }
        public int TotalNGPreparation { get; set; }
        
        public int NetStock => StockDetails.Count; // Updated to show current actual stock
    }

    public class StockItemDetail
    {
        public int No { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string RackInfo { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string VIN { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? QtyLot { get; set; }
        public int? Min { get; set; }
        public int? Rop { get; set; }
        public int? Max { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal LevelStock { get; set; }
        public string Operator { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Shortage, Normal, Over
        public DateTime LastActivityDate { get; set; }
        public bool IsManualAdjust { get; set; }
        public string? AdjustNote { get; set; }
    }
}
