using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class PoolingRecord
    {
        [Key]
        public int PoolingId { get; set; }

        [Required]
        [StringLength(20)]
        public string Plant { get; set; } = string.Empty; // Molded, Hose, RVI

        [Required]
        [StringLength(5)]
        public string Rack { get; set; } = string.Empty; // A - K

        [Required]
        public int Column { get; set; } // 1 - 20

        [Required(ErrorMessage = "Tag wajib diisi")]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required(ErrorMessage = "Label wajib diisi")]
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }
        
        public int? ItemId { get; set; }
        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; } = 1;
    }
}
