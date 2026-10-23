using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    public int DocumentId { get; set; }

    /// <summary>Set for a user share.</summary>
    public int? SharedWithUserId { get; set; }

    /// <summary>Department name; set for a team share.</summary>
    [MaxLength(100)]
    public string? SharedWithTeam { get; set; }

    /// <summary>Always the document owner.</summary>
    public int SharedByUserId { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User? SharedWithUser { get; set; }

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;
}
