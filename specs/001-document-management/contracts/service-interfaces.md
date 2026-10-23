# Contract: Service Interfaces

**Feature**: `001-document-management` | Namespace `ContosoDashboard.Services` (types in `ContosoDashboard.Models` where noted)

All interfaces are registered **scoped** in `Program.cs` (infrastructure interfaces: scoped/singleton as noted). Razor components call these interfaces only — never `ApplicationDbContext`. Every data-touching method takes the requesting user id and returns `null` / `false` / empty / a failure result instead of revealing the existence of inaccessible data. Signatures are the design contract; exact parameter-object shapes may be refined during implementation without changing semantics.

## Infrastructure (Principle II)

```csharp
public interface IFileStorageService          // default: LocalFileStorageService (singleton-safe, stateless)
{
    Task<string> UploadAsync(Stream content, string storagePath, CancellationToken ct = default); // returns the stored key
    Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default);               // throws FileNotFoundException when missing
    Task DeleteAsync(string storagePath, CancellationToken ct = default);                         // idempotent
    Task<string?> GetUrlAsync(string storagePath, TimeSpan? validFor = null, CancellationToken ct = default); // local: null
}

public interface IMalwareScanService          // default: StubMalwareScanService (EICAR detection only)
{
    Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default);
}
public sealed record MalwareScanResult(bool IsClean, string? ThreatName);
```

Rules: `LocalFileStorageService` resolves `storagePath` under `FileStorage:RootPath` and throws if the resolved full path is outside the root; callers never pass user-supplied names. `UploadAsync` must not overwrite an existing key.

## `IDocumentService`

```csharp
public interface IDocumentService
{
    // Upload & change
    Task<UploadResult> UploadAsync(UploadRequest request, int requestingUserId, CancellationToken ct = default);          // one file; batch = caller loops (per-file outcome, FR-007)
    Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId);              // uploader, Admin, project mgr, same-dept Team Lead
    Task<UploadResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId, CancellationToken ct = default); // uploader only
    Task<OperationResult> DeleteAsync(int documentId, int requestingUserId);                                                  // uploader, Admin, project mgr, same-dept Team Lead (caller confirms in UI first)

    // Query (all results pre-filtered by the visibility predicate)
    Task<PagedResult<DocumentListItem>> GetMyDocumentsAsync(DocumentQuery query, int requestingUserId);
    Task<PagedResult<DocumentListItem>> SearchAsync(string term, DocumentQuery query, int requestingUserId);
    Task<PagedResult<DocumentListItem>> GetProjectDocumentsAsync(int projectId, DocumentQuery query, int requestingUserId);   // empty if user not member/manager/Admin
    Task<PagedResult<DocumentListItem>> GetSharedWithMeAsync(DocumentQuery query, int requestingUserId);
    Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId);                                            // null if missing or not viewable; includes capability flags (CanEdit, CanDelete, CanReplace, CanShare)
    Task<List<DocumentListItem>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);                                // uploader's own, newest first (FR-038)
    Task<int> GetDocumentCountAsync(int requestingUserId);                                                                    // uploader's own (FR-039)

    // Delivery (used by the two HTTP endpoints)
    Task<DocumentStream?> OpenReadAsync(int documentId, int requestingUserId, DocumentAccessKind kind, CancellationToken ct = default); // records Download/Preview activity; null if missing/denied/not previewable

    // Task integration
    Task<List<DocumentListItem>> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<OperationResult> AttachToTaskAsync(int documentId, int taskId, int requestingUserId);   // FR-037 rules; fails with clear message on project mismatch
    Task<OperationResult> DetachFromTaskAsync(int documentId, int taskId, int requestingUserId); // attacher, uploader, task's project mgr, or Admin
    Task<List<DocumentListItem>> GetAttachableDocumentsAsync(int taskId, int requestingUserId);  // accessible docs with no project or same project
}
```

Supporting types: `UploadRequest { FileUpload File; DocumentMetadata Metadata }`, `FileUpload { Stream Content; string FileName; long Length; IProgress<int>? Progress }`, `DocumentMetadata { Title, Description?, Category, ProjectId?, Tags }`, `DocumentQuery { Category?, ProjectId?, DateFrom?, DateTo?, SortBy (Title|UploadDate|Category|FileSize), Descending, Page, PageSize }`, `PagedResult<T> { Items, TotalCount }`, `OperationResult { Succeeded, ErrorMessage? }`, `UploadResult : OperationResult { DocumentId? }`, `DocumentStream { Stream, ContentType, FileName, Length }`, `DocumentAccessKind { Download, Preview }`.

Required behavior: UploadAsync validates → scans → generates GUID path → stores → saves row + tags + `Upload` activity in one `SaveChanges` → on any failure removes the stored file and persists nothing; after commit it notifies project members (excluding uploader) on a best-effort basis (FR-034). Failure messages are user-presentable (size limit, accepted types list, mismatch, malware, project not permitted).

## `IDocumentSharingService`

```csharp
public interface IDocumentSharingService
{
    Task<OperationResult> ShareWithUsersAsync(int documentId, IReadOnlyCollection<int> userIds, int requestingUserId);   // owner only; notifies recipients; logs Share
    Task<OperationResult> ShareWithTeamAsync(int documentId, string department, int requestingUserId);                    // owner only; notifies current team members except owner; logs Share
    Task<OperationResult> RemoveShareAsync(int shareId, int requestingUserId);                                            // owner only; immediate; logs ShareRemoved
    Task<List<DocumentShareInfo>> GetSharesAsync(int documentId, int requestingUserId);                                   // owner only, else empty
}
```

## `IDocumentActivityService` (internal use by services + reports)

```csharp
public interface IDocumentActivityService
{
    Task RecordAsync(DocumentActionType action, int documentId, string title, string fileType, int ownerUserId, int actorUserId, string? target = null);
}
```

Append-only; no update/delete members exist.

## `IDocumentReportService` (Administrator only — role verified in service, returns empty/`null`/throws `UnauthorizedAccessException` for others per implementation choice, documented in tasks)

```csharp
public interface IDocumentReportService
{
    Task<List<UploadsByTypeRow>> GetUploadsByTypeAsync(DateTime? from, DateTime? to, int requestingUserId);
    Task<List<TopUploaderRow>> GetTopUploadersAsync(DateTime? from, DateTime? to, int top, int requestingUserId);
    Task<AccessPatternReport> GetAccessPatternsAsync(DateTime from, DateTime to, int requestingUserId);   // by document, by user, by day; Download and Preview counted separately
    Task<PagedResult<DocumentActivityRow>> GetActivityAsync(DateTime? from, DateTime? to, int page, int pageSize, int requestingUserId);
}
```

## Changes to existing interfaces

| Interface | Change |
|---|---|
| `INotificationService` | None (new `NotificationType` values `DocumentShared`, `DocumentAdded` appended to the enum). |
| `IDashboardService` / `DashboardSummary` | Add `DocumentCount` to the summary (counts uploader's documents). |
| `IUserService` | Add `Task<List<string>> GetDepartmentsAsync()` and `Task<List<User>> GetUsersForSharingAsync(int requestingUserId)` (id + display name + department only). |
| `ITaskService` | None; `GetTaskByIdAsync(taskId, userId)` is reused for task access checks. |

## DI registration (`Program.cs`)

```csharp
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentSharingService, DocumentSharingService>();
builder.Services.AddScoped<IDocumentActivityService, DocumentActivityService>();
builder.Services.AddScoped<IDocumentReportService, DocumentReportService>();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IMalwareScanService, StubMalwareScanService>();
```

Configuration (`appsettings.json`): `"FileStorage": { "RootPath": "AppData/uploads" }`.
