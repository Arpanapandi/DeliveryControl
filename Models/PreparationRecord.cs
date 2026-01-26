using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models
{
    public class PreparationRecord
    {
        [Key]
        public int PreparationId { get; set; }

        [Required]
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

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }
    }
}
