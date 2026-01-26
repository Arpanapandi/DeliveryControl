using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk Item yang akan di-deliver pada setiap schedule
    /// </summary>
    public class DeliveryItem
    {
        [Key]
        public int DeliveryItemId { get; set; }

        [Required]
        public int ScheduleId { get; set; }

        [Required]
        public int ItemId { get; set; }

        [Required(ErrorMessage = "Quantity wajib diisi")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ActualQuantity { get; set; }

        [StringLength(20)]
        public string? Unit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalWeight { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalVolume { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        // Calculated property
        [NotMapped]
        public decimal? VarianceQuantity
        {
            get
            {
                if (ActualQuantity.HasValue)
                    return ActualQuantity.Value - Quantity;
                return null;
            }
        }

        // Navigation properties
        [ForeignKey("ScheduleId")]
        public virtual DeliverySchedule DeliverySchedule { get; set; } = null!;

        [ForeignKey("ItemId")]
        public virtual Item Item { get; set; } = null!;
    }
}

