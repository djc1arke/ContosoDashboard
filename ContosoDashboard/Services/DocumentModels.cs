using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public class FileUpload
{
    public Stream Content { get; set; } = Stream.Null;
    public string FileName { get; set; } = string.Empty;
    public long Length { get; set; }
    public IProgress<int>? Progress { get; set; }
}

public class DocumentMetadata
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public int? ProjectId { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class UploadRequest
{
    public FileUpload File { get; set; } = new();
    public DocumentMetadata Metadata { get; set; } = new();
}

public enum DocumentSortBy
{
    Title,
    UploadDate,
    Category,
    FileSize
}

public class DocumentQuery
{
    public DocumentCategory? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public DocumentSortBy SortBy { get; set; } = DocumentSortBy.UploadDate;
    public bool Descending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

public class OperationResult
{
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }

    public static OperationResult Success() => new() { Succeeded = true };
    public static OperationResult Failure(string message) => new() { Succeeded = false, ErrorMessage = message };
}

public class UploadResult : OperationResult
{
    public int? DocumentId { get; set; }

    public static UploadResult Ok(int documentId) => new() { Succeeded = true, DocumentId = documentId };
    public static new UploadResult Failure(string message) => new() { Succeeded = false, ErrorMessage = message };
}

public sealed class DocumentStream : IAsyncDisposable, IDisposable
{
    public Stream Stream { get; init; } = Stream.Null;
    public string ContentType { get; init; } = "application/octet-stream";
    public string FileName { get; init; } = "file";
    public long Length { get; init; }

    public void Dispose() => Stream.Dispose();
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public enum DocumentAccessKind
{
    Download,
    Preview
}

public class DocumentListItem
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadDate { get; set; }
    public int UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsPreviewable => DocumentFileRules.IsPreviewable(FileType);
}

public class DocumentDetails : DocumentListItem
{
    public string ContentType { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanReplace { get; set; }
    public bool CanShare { get; set; }
}

public class DocumentShareInfo
{
    public int DocumentShareId { get; set; }
    public int DocumentId { get; set; }
    public int? SharedWithUserId { get; set; }
    public string? SharedWithUserName { get; set; }
    public string? SharedWithTeam { get; set; }
    public DateTime SharedDate { get; set; }
}
