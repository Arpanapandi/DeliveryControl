using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class ItemMapping
    {
        [Key]
        public int MappingId { get; set; }

        [Required]
        [StringLength(50)]
        public string VIN { get; set; } // Foreign Key (Logical) to Items.VIN

        [Required]
        [StringLength(100)]
        public string Customer { get; set; }

        [Required]
        [StringLength(100)]
        public string CustomerPartNumber { get; set; }
        
        // Audit Trail
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? UpdatedDate { get; set; }
    }
}
