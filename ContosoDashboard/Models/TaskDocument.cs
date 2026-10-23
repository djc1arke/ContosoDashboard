using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class TaskDocument
{
    public int TaskId { get; set; }

    public int DocumentId { get; set; }

    public int AttachedByUserId { get; set; }

    public DateTime AttachedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey("TaskId")]
    public virtual TaskItem Task { get; set; } = null!;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("AttachedByUserId")]
    public virtual User AttachedByUser { get; set; } = null!;
}
