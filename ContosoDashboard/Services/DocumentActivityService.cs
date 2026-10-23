using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

/// <summary>Append-only activity log. No update or delete members exist.</summary>
public class DocumentActivityService : IDocumentActivityService
{
    private readonly ApplicationDbContext _context;

    public DocumentActivityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task RecordAsync(DocumentActionType action, int documentId, string title, string fileType,
        int ownerUserId, int actorUserId, string? target = null)
    {
        _context.DocumentActivities.Add(CreateEntry(action, documentId, title, fileType, ownerUserId, actorUserId, target));
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds an entry without saving, so callers can persist it in the same SaveChanges as their own change.</summary>
    public static DocumentActivity CreateEntry(DocumentActionType action, int documentId, string title, string fileType,
        int ownerUserId, int actorUserId, string? target = null) => new()
    {
        ActionType = action,
        DocumentId = documentId,
        DocumentTitle = title.Length > 255 ? title[..255] : title,
        FileType = fileType,
        DocumentOwnerUserId = ownerUserId,
        ActorUserId = actorUserId,
        TargetDescription = target,
        Timestamp = DateTime.UtcNow
    };
}
