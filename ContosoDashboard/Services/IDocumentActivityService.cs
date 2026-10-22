using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentActivityService
{
    Task RecordAsync(DocumentActionType action, int documentId, string title, string fileType,
        int ownerUserId, int actorUserId, string? target = null);
}
