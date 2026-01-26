using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk master Item/Produk
    /// </summary>
    public class Item
    {
        [Key]
        public int ItemId { get; set; }

        [Required(ErrorMessage = "Kode item wajib diisi")]
        [StringLength(50)]
        public string ItemCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama item wajib diisi")]
        [StringLength(200)]
        public string ItemName { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(20)]
        public string? Unit { get; set; } // PCS, BOX, PALLET, dll

        [StringLength(100)]
        public string? Category { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Weight { get; set; } // dalam KG

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Volume { get; set; } // dalam M3

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        // Navigation properties
        public virtual ICollection<DeliveryItem> DeliveryItems { get; set; } = new List<DeliveryItem>();
    }
}

