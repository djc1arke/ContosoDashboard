using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentTag
{
    [Key]
    public int DocumentTagId { get; set; }

    public int DocumentId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Tag { get; set; } = string.Empty;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;
}
