using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public class DocumentService : IDocumentService
{
    private const int MaxTags = 10;
    private const int MaxTagLength = 30;
    private const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanService _scanner;
    private readonly INotificationService _notifications;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IMalwareScanService scanner,
        INotificationService notifications,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _storage = storage;
        _scanner = scanner;
        _notifications = notifications;
        _logger = logger;
    }

    // ------------------------------------------------------------------ Upload (US1)

    public async Task<UploadResult> UploadAsync(UploadRequest request, int requestingUserId, CancellationToken ct = default)
    {
        var requester = await DocumentAccessRules.LoadRequesterAsync(_context, requestingUserId);
        if (requester == null)
            return UploadResult.Failure("You are not allowed to upload documents.");

        var meta = request.Metadata;
        var title = (meta.Title ?? string.Empty).Trim();
        if (title.Length == 0) return UploadResult.Failure("A title is required.");
        if (title.Length > 255) return UploadResult.Failure("The title must be 255 characters or fewer.");
        if (!Enum.IsDefined(meta.Category)) return UploadResult.Failure("A category is required.");

        var description = string.IsNullOrWhiteSpace(meta.Description) ? null : meta.Description.Trim();
        if (description is { Length: > 2000 })
            return UploadResult.Failure("The description must be 2000 characters or fewer.");

        var tagResult = NormalizeTags(meta.Tags);
        if (tagResult.Error != null) return UploadResult.Failure(tagResult.Error);

        // Validation needs a seekable stream; buffer if necessary (bounded by the size limit).
        var source = request.File.Content;
        MemoryStream? buffered = null;
        try
        {
            Stream content = source;
            if (!source.CanSeek)
            {
                buffered = new MemoryStream();
                // Read at most limit+1 bytes so an oversize stream is rejected by size validation.
                var buf = new byte[81920];
                long total = 0;
                int n;
                while (total <= DocumentFileRules.MaxFileSizeBytes &&
                       (n = await source.ReadAsync(buf, ct)) > 0)
                {
                    await buffered.WriteAsync(buf.AsMemory(0, n), ct);
                    total += n;
                }
                buffered.Position = 0;
                content = buffered;
            }

            var length = content.Length;
            var validation = DocumentFileRules.Validate(request.File.FileName, length, content);
            if (!validation.IsValid) return UploadResult.Failure(validation.ErrorMessage!);

            var scan = await _scanner.ScanAsync(content, request.File.FileName, ct);
            if (!scan.IsClean)
                return UploadResult.Failure("The file was rejected because it may contain malware.");

            if (meta.ProjectId != null &&
                !await DocumentAccessRules.CanAssociateProjectAsync(_context, requester, meta.ProjectId.Value))
                return UploadResult.Failure("You can only add documents to projects you belong to.");

            var ext = validation.Extension!;
            var now = DateTime.UtcNow;
            var storagePath = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";

            content.Position = 0;
            request.File.Progress?.Report(10);
            try
            {
                await _storage.UploadAsync(content, storagePath, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Document storage upload failed.");
                await TryDeleteStoredFileAsync(storagePath);
                return UploadResult.Failure("The file could not be stored. Please try again.");
            }
            request.File.Progress?.Report(80);

            var document = new Document
            {
                Title = title,
                Description = description,
                Category = meta.Category,
                OriginalFileName = DocumentFileRules.SanitizeFileName(request.File.FileName),
                FileType = ext.TrimStart('.'),
                ContentType = validation.ContentType!,
                FileSizeBytes = length,
                StoragePath = storagePath,
                UploadDate = now,
                UploadedByUserId = requester.UserId,
                ProjectId = meta.ProjectId,
                Tags = tagResult.Tags.Select(t => new DocumentTag { Tag = t }).ToList()
            };

            try
            {
                _context.Documents.Add(document);
                // Save once to obtain the identity key, then log the activity in the same transaction.
                await using var tx = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null
                    ? await _context.Database.BeginTransactionAsync(ct)
                    : null;
                await _context.SaveChangesAsync(ct);
                _context.DocumentActivities.Add(DocumentActivityService.CreateEntry(
                    DocumentActionType.Upload, document.DocumentId, document.Title, document.FileType,
                    requester.UserId, requester.UserId));
                await _context.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving document metadata failed; removing stored file.");
                _context.ChangeTracker.Clear();
                await TryDeleteStoredFileAsync(storagePath);
                return UploadResult.Failure("The document could not be saved. Please try again.");
            }

            request.File.Progress?.Report(100);
            await NotifyProjectMembersAsync(document, requester);
            return UploadResult.Ok(document.DocumentId);
        }
        finally
        {
            if (buffered != null) await buffered.DisposeAsync();
        }
    }

    private async Task TryDeleteStoredFileAsync(string storagePath)
    {
        try { await _storage.DeleteAsync(storagePath); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to remove stored file {Path} during cleanup.", storagePath); }
    }

    private async Task NotifyProjectMembersAsync(Document document, User uploader)
    {
        if (document.ProjectId == null) return;
        try
        {
            var project = await _context.Projects.AsNoTracking()
                .Where(p => p.ProjectId == document.ProjectId)
                .Select(p => new
                {
                    p.Name,
                    p.ProjectManagerId,
                    Members = p.ProjectMembers.Select(pm => pm.UserId).ToList()
                })
                .FirstOrDefaultAsync();
            if (project == null) return;

            var recipients = project.Members.Append(project.ProjectManagerId)
                .Where(id => id != uploader.UserId).Distinct().ToList();
            foreach (var userId in recipients)
            {
                await _notifications.CreateNotificationAsync(new Notification
                {
                    UserId = userId,
                    Title = "New project document",
                    Message = $"{uploader.DisplayName} added \"{document.Title}\" to {project.Name}.",
                    Type = NotificationType.DocumentAdded,
                    Priority = NotificationPriority.Informational
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort document notification failed.");
        }
    }

    private static (List<string> Tags, string? Error) NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? Enumerable.Empty<string>())
        {
            var tag = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength)
                return (result, $"Each tag must be {MaxTagLength} characters or fewer.");
            if (!result.Contains(tag)) result.Add(tag);
        }
        if (result.Count > MaxTags)
            return (result, $"A document can have at most {MaxTags} tags.");
        return (result, null);
    }

    // ------------------------------------------------------------------ Queries (US2)

    public async Task<PagedResult<DocumentListItem>> GetMyDocumentsAsync(DocumentQuery query, int requestingUserId)
    {
        var requester = await DocumentAccessRules.LoadRequesterAsync(_context, requestingUserId);
        if (requester == null) return new PagedResult<DocumentListItem>();

        var docs = _context.Documents.AsNoTracking().Where(d => d.UploadedByUserId == requestingUserId);
        return await ExecuteQueryAsync(docs, query);
    }

    public async Task<PagedResult<DocumentListItem>> SearchAsync(string term, DocumentQuery query, int requestingUserId)
    {
        var requester = await DocumentAccessRules.LoadRequesterAsync(_context, requestingUserId);
        if (requester == null) return new PagedResult<DocumentListItem>();

        var docs = DocumentAccessRules.Visible(_context.Documents.AsNoTracking(), requester);

        term = (term ?? string.Empty).Trim();
        if (term.Length > 0)
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            docs = docs.Where(d =>
                EF.Functions.Like(d.Title, pattern, "\\")
                || (d.Description != null && EF.Functions.Like(d.Description, pattern, "\\"))
                || d.Tags.Any(t => EF.Functions.Like(t.Tag, pattern, "\\"))
                || EF.Functions.Like(d.UploadedByUser.DisplayName, pattern, "\\")
                || (d.Project != null && EF.Functions.Like(d.Project.Name, pattern, "\\")));
        }
        return await ExecuteQueryAsync(docs, query);
    }

    private static async Task<PagedResult<DocumentListItem>> ExecuteQueryAsync(IQueryable<Document> docs, DocumentQuery query)
    {
        if (query.Category != null) docs = docs.Where(d => d.Category == query.Category);
        if (query.ProjectId != null) docs = docs.Where(d => d.ProjectId == query.ProjectId);
        if (query.DateFrom != null)
        {
            var from = query.DateFrom.Value.Date;
            docs = docs.Where(d => d.UploadDate >= from);
        }
        if (query.DateTo != null)
        {
            var toExclusive = query.DateTo.Value.Date.AddDays(1);
            docs = docs.Where(d => d.UploadDate < toExclusive);
        }

        var total = await docs.CountAsync();

        docs = (query.SortBy, query.Descending) switch
        {
            (DocumentSortBy.Title, false) => docs.OrderBy(d => d.Title).ThenBy(d => d.DocumentId),
            (DocumentSortBy.Title, true) => docs.OrderByDescending(d => d.Title).ThenBy(d => d.DocumentId),
            (DocumentSortBy.Category, false) => docs.OrderBy(d => d.Category).ThenBy(d => d.DocumentId),
            (DocumentSortBy.Category, true) => docs.OrderByDescending(d => d.Category).ThenBy(d => d.DocumentId),
            (DocumentSortBy.FileSize, false) => docs.OrderBy(d => d.FileSizeBytes).ThenBy(d => d.DocumentId),
            (DocumentSortBy.FileSize, true) => docs.OrderByDescending(d => d.FileSizeBytes).ThenBy(d => d.DocumentId),
            (_, false) => docs.OrderBy(d => d.UploadDate).ThenBy(d => d.DocumentId),
            _ => docs.OrderByDescending(d => d.UploadDate).ThenBy(d => d.DocumentId),
        };

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);

        var items = await docs
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentListItem
            {
                DocumentId = d.DocumentId,
                Title = d.Title,
                Description = d.Description,
                Category = d.Category,
                OriginalFileName = d.OriginalFileName,
                FileType = d.FileType,
                FileSizeBytes = d.FileSizeBytes,
                UploadDate = d.UploadDate,
                UploadedByUserId = d.UploadedByUserId,
                UploadedByName = d.UploadedByUser.DisplayName,
                ProjectId = d.ProjectId,
                ProjectName = d.Project != null ? d.Project.Name : null,
                Tags = d.Tags.Select(t => t.Tag).ToList()
            })
            .ToListAsync();

        return new PagedResult<DocumentListItem> { Items = items, TotalCount = total };
    }

    // ------------------------------------------------------------------ Delivery (US3)

    public async Task<DocumentDetails?> GetDocumentAsync(int documentId, int requestingUserId)
    {
        var requester = await DocumentAccessRules.LoadRequesterAsync(_context, requestingUserId);
        if (requester == null) return null;

        var d = await DocumentAccessRules.Visible(_context.Documents.AsNoTracking(), requester)
            .Include(x => x.UploadedByUser)
            .Include(x => x.Project)
            .Include(x => x.Tags)
            .FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (d == null) return null;

        return new DocumentDetails
        {
            DocumentId = d.DocumentId,
            Title = d.Title,
            Description = d.Description,
            Category = d.Category,
            OriginalFileName = d.OriginalFileName,
            FileType = d.FileType,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            UploadDate = d.UploadDate,
            UploadedByUserId = d.UploadedByUserId,
            UploadedByName = d.UploadedByUser.DisplayName,
            ProjectId = d.ProjectId,
            ProjectName = d.Project?.Name,
            Tags = d.Tags.Select(t => t.Tag).OrderBy(t => t).ToList(),
            CanEdit = DocumentAccessRules.CanEditOrDelete(requester, d),
            CanDelete = DocumentAccessRules.CanEditOrDelete(requester, d),
            CanReplace = DocumentAccessRules.CanReplace(requester, d),
            CanShare = DocumentAccessRules.CanShare(requester, d)
        };
    }

    public async Task<DocumentStream?> OpenReadAsync(int documentId, int requestingUserId, DocumentAccessKind kind, CancellationToken ct = default)
    {
        var requester = await DocumentAccessRules.LoadRequesterAsync(_context, requestingUserId);
        if (requester == null) return null;

        var d = await DocumentAccessRules.Visible(_context.Documents.AsNoTracking(), requester)
            .FirstOrDefaultAsync(x => x.DocumentId == documentId, ct);
        if (d == null) return null;

        if (kind == DocumentAccessKind.Preview && !DocumentFileRules.IsPreviewable(d.FileType))
            return null;

        Stream stream;
        try
        {
            stream = await _storage.DownloadAsync(d.StoragePath, ct);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Stored file missing for document {DocumentId}.", d.DocumentId);
            return null;
        }

        try
        {
            _context.DocumentActivities.Add(DocumentActivityService.CreateEntry(
                kind == DocumentAccessKind.Preview ? DocumentActionType.Preview : DocumentActionType.Download,
                d.DocumentId, d.Title, d.FileType, d.UploadedByUserId, requester.UserId));
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        return new DocumentStream
        {
            Stream = stream,
            ContentType = d.ContentType,
            FileName = d.OriginalFileName,
            Length = d.FileSizeBytes
        };
    }

    // ------------------------------------------------------------------ Later stories

    public Task<OperationResult> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId)
        => throw new NotImplementedException();
    public Task<UploadResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<OperationResult> DeleteAsync(int documentId, int requestingUserId)
        => throw new NotImplementedException();
    public Task<PagedResult<DocumentListItem>> GetProjectDocumentsAsync(int projectId, DocumentQuery query, int requestingUserId)
        => throw new NotImplementedException();
    public Task<PagedResult<DocumentListItem>> GetSharedWithMeAsync(DocumentQuery query, int requestingUserId)
        => throw new NotImplementedException();
    public Task<List<DocumentListItem>> GetRecentDocumentsAsync(int requestingUserId, int count = 5)
        => throw new NotImplementedException();
    public Task<int> GetDocumentCountAsync(int requestingUserId)
        => throw new NotImplementedException();
    public Task<List<DocumentListItem>> GetTaskDocumentsAsync(int taskId, int requestingUserId)
        => throw new NotImplementedException();
    public Task<OperationResult> AttachToTaskAsync(int documentId, int taskId, int requestingUserId)
        => throw new NotImplementedException();
    public Task<OperationResult> DetachFromTaskAsync(int documentId, int taskId, int requestingUserId)
        => throw new NotImplementedException();
    public Task<List<DocumentListItem>> GetAttachableDocumentsAsync(int taskId, int requestingUserId)
        => throw new NotImplementedException();
}
