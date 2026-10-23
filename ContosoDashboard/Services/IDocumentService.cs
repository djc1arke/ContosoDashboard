namespace ContosoDashboard.Services;

public interface IDocumentService
{
    // Upload & change
    Task<UploadResult> UploadAsync(UploadRequest request, int requestingUserId, CancellationToken ct = default);
    Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId);
    Task<UploadResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int documentId, int requestingUserId);

    // Query (all results pre-filtered by the visibility predicate)
    Task<PagedResult<DocumentListItem>> GetMyDocumentsAsync(DocumentQuery query, int requestingUserId);
    Task<PagedResult<DocumentListItem>> SearchAsync(string term, DocumentQuery query, int requestingUserId);
    Task<PagedResult<DocumentListItem>> GetProjectDocumentsAsync(int projectId, DocumentQuery query, int requestingUserId);
    Task<PagedResult<DocumentListItem>> GetSharedWithMeAsync(DocumentQuery query, int requestingUserId);
    Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId);
    Task<List<DocumentListItem>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);
    Task<int> GetDocumentCountAsync(int requestingUserId);

    // Delivery (used by the two HTTP endpoints)
    Task<DocumentStream?> OpenReadAsync(int documentId, int requestingUserId, DocumentAccessKind kind, CancellationToken ct = default);

    // Task integration
    Task<List<DocumentListItem>> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<OperationResult> AttachToTaskAsync(int documentId, int taskId, int requestingUserId);
    Task<OperationResult> DetachFromTaskAsync(int documentId, int taskId, int requestingUserId);
    Task<List<DocumentListItem>> GetAttachableDocumentsAsync(int taskId, int requestingUserId);
}
