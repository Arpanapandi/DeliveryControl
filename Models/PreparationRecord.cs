using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class PreparationRecord
    {
        [Key]
        public int PreparationId { get; set; }

        [StringLength(20)]
        public string Plant { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tag wajib diisi")]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required(ErrorMessage = "Label wajib diisi")]
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kanban wajib diisi")]
        [StringLength(100)]
        public string Kanban { get; set; } = string.Empty;

        public int? ScheduleId { get; set; }

        [ForeignKey("ScheduleId")]
        public virtual DeliverySchedule? DeliverySchedule { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Remark: "Match" = normal (affect stock), "Mismatch" = log only (no stock impact)
        /// </summary>
        [StringLength(20)]
        public string Remark { get; set; } = "Match";

        /// <summary>
        /// Flag sementara (tidak disimpan ke DB): bypass validasi stock habis di rak.
        /// Diisi dari request body saat toggle "Skip Stock Validation" aktif.
        /// </summary>
        [NotMapped]
        public bool SkipStockValidation { get; set; } = false;
    }
}
