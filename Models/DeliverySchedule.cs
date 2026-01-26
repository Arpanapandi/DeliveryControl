using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk Schedule Delivery
    /// </summary>
    public class DeliverySchedule
    {
        [Key]
        public int ScheduleId { get; set; }

        [Required(ErrorMessage = "Nomor schedule wajib diisi")]
        [StringLength(50)]
        [Display(Name = "Schedule Number")]
        public string ScheduleNumber { get; set; } = string.Empty;

        // CUST - Customer
        [Required(ErrorMessage = "Customer wajib dipilih")]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        // ROUTE
        [StringLength(100)]
        [Display(Name = "Route")]
        public string? Route { get; set; }

        // CYCLE
        [StringLength(50)]
        [Display(Name = "Cycle")]
        public string? Cycle { get; set; }

        // ENTER DOCK - Waktu Masuk Dock
        [Display(Name = "Enter Dock Time")]
        public DateTime? EnterDockTime { get; set; }

        // PICKUP - Waktu Pickup
        [Display(Name = "Pickup Time")]
        public DateTime? PickupTime { get; set; }

        // ETD - Estimated Time of Departure
        [Display(Name = "ETD")]
        public DateTime? ETD { get; set; }

        // Range - Range waktu atau jarak
        [StringLength(100)]
        [Display(Name = "Range")]
        public string? Range { get; set; }

        // SKID - Jumlah SKID/Pallet
        [Display(Name = "SKID")]
        public int? SKID { get; set; }

        // AREA - Area tujuan atau area pengiriman
        [StringLength(100)]
        [Display(Name = "Area")]
        public string? Area { get; set; }

        // Fields tambahan untuk tracking
        [Required(ErrorMessage = "Tanggal schedule wajib diisi")]
        [Display(Name = "Scheduled Date")]
        public DateTime ScheduledDate { get; set; }

        [Display(Name = "Actual Enter Dock Time")]
        public DateTime? ActualEnterDockTime { get; set; }

        [Display(Name = "Actual Start Time")]
        public DateTime? ActualStartTime { get; set; }

        [Display(Name = "Actual End Time")]
        public DateTime? ActualEndTime { get; set; }

        [StringLength(20)]
        [Display(Name = "Preparation Status")]
        public string? PreparationStatus { get; set; } = "Scheduled";

        [StringLength(20)]
        [Display(Name = "Driver Status")]
        public string? DriverStatus { get; set; } = "Scheduled";

        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Scheduled"; // Scheduled, In Progress, Completed, Cancelled, Delayed

        [StringLength(100)]
        [Display(Name = "Vehicle Number")]
        public string? VehicleNumber { get; set; }

        [StringLength(100)]
        [Display(Name = "Driver Name")]
        public string? DriverName { get; set; }

        [StringLength(50)]
        [Display(Name = "Driver Phone")]
        public string? DriverPhone { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public string? CreatedBy { get; set; }

        public DateTime? UpdatedDate { get; set; }

        public string? UpdatedBy { get; set; }

        // Calculated property untuk status keterlambatan
        [NotMapped]
        public string DelayStatus
        {
            get
            {
                if (ActualStartTime.HasValue && ETD.HasValue)
                {
                    if (ActualStartTime.Value > ETD.Value)
                        return "Late";
                }
                return "On Time";
            }
        }

        [NotMapped]
        public TimeSpan? DelayDuration
        {
            get
            {
                if (ActualStartTime.HasValue && ETD.HasValue)
                {
                    var delay = ActualStartTime.Value - ETD.Value;
                    return delay.TotalMinutes > 0 ? delay : TimeSpan.Zero;
                }
                return null;
            }
        }

        // Navigation properties
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; } = null!;

        public virtual ICollection<DeliveryItem> DeliveryItems { get; set; } = new List<DeliveryItem>();
    }
}

