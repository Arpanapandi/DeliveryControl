using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class FgMapping
    {
        [Key]
        public int FgMappingId { get; set; }

        [Required(ErrorMessage = "Item wajib dipilih")]
        public int ItemId { get; set; }

        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }

        [Required(ErrorMessage = "Plant wajib dipilih")]
        [StringLength(20)]
        public string Plant { get; set; } = string.Empty; // Molded, Hose, RVI

        [Required(ErrorMessage = "Rak wajib dipilih")]
        [StringLength(5)]
        public string Rack { get; set; } = string.Empty; // A - I

        [Required(ErrorMessage = "Nomor Rak wajib diisi")]
        public int NoRack { get; set; } // 1 - 33

        [Required(ErrorMessage = "Qty Lot wajib diisi")]
        public int QtyLot { get; set; }

        [Required(ErrorMessage = "Min Stock wajib diisi")]
        public int MinStock { get; set; }

        [Required(ErrorMessage = "Max Stock wajib diisi")]
        public int MaxStock { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
