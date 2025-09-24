using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Azunt.DefaultAttachmentManagement
{
    [Table("DefaultAttachments")]
    public class DefaultAttachment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        // BIT NULL DEFAULT(1)
        public bool? Active { get; set; }

        // DB 기본값을 활용하려면 nullable 권장
        public DateTimeOffset? CreatedAt { get; set; }

        [MaxLength(255)]
        public string? CreatedBy { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Name { get; set; }

        // INT NULL DEFAULT(0)
        public int? ApplicantType { get; set; }

        [MaxLength(255)]
        public string? Type { get; set; }

        // BIT NULL DEFAULT(1)
        public bool? IsRequired { get; set; }
    }
}
