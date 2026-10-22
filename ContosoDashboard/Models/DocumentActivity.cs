using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public enum DocumentActionType
{
    Upload,
    Download,
    Preview,
    Delete,
    Share,
    ShareRemoved
}

/// <summary>Append-only audit record. DocumentId intentionally has no FK so records outlive the document.</summary>
public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string DocumentTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string FileType { get; set; } = string.Empty;

    public DocumentActionType ActionType { get; set; }

    public int ActorUserId { get; set; }

    public int DocumentOwnerUserId { get; set; }

    [MaxLength(255)]
    public string? TargetDescription { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ForeignKey("ActorUserId")]
    public virtual User ActorUser { get; set; } = null!;
}
