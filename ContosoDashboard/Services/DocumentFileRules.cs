using System.IO.Compression;
using System.Text;

namespace ContosoDashboard.Services;

public sealed record FileValidationResult(bool IsValid, string? ErrorMessage, string? Extension, string? ContentType)
{
    public static FileValidationResult Fail(string message) => new(false, message, null, null);
}

/// <summary>Allow-list, size limits, magic-byte checks, content types and file-name sanitising (FR-003, FR-009, FR-014).</summary>
public static class DocumentFileRules
{
    public const long MaxFileSizeBytes = 26_214_400; // 25 MB
    public const string AcceptedTypesText = "PDF, Word, Excel, PowerPoint, text, JPEG, PNG";
    public const string SizeLimitText = "25 MB";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    private static readonly HashSet<string> PreviewableTypes = new(StringComparer.OrdinalIgnoreCase) { "pdf", "jpg", "jpeg", "png" };

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Ole2 = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };

    public static bool IsPreviewable(string fileType) => PreviewableTypes.Contains(fileType.TrimStart('.'));

    /// <summary>Canonical extension (with dot, lowercase) if allowed, otherwise null.</summary>
    public static string? GetCanonicalExtension(string fileName)
    {
        var ext = Path.GetExtension(SanitizeFileName(fileName)).ToLowerInvariant();
        return ContentTypes.ContainsKey(ext) ? ext : null;
    }

    public static string? GetContentType(string fileTypeOrExtension)
    {
        var ext = fileTypeOrExtension.StartsWith('.') ? fileTypeOrExtension : "." + fileTypeOrExtension;
        return ContentTypes.TryGetValue(ext, out var ct) ? ct : null;
    }

    /// <summary>Validates name, size and content signature. The stream must be seekable; position is restored.</summary>
    public static FileValidationResult Validate(string fileName, long length, Stream content)
    {
        var ext = GetCanonicalExtension(fileName ?? string.Empty);
        if (ext == null)
            return FileValidationResult.Fail($"This file type is not supported. Accepted types: {AcceptedTypesText}.");

        if (length <= 0)
            return FileValidationResult.Fail("The file is empty.");
        if (length > MaxFileSizeBytes)
            return FileValidationResult.Fail($"The file is larger than the {SizeLimitText} limit.");

        if (!content.CanSeek)
            return FileValidationResult.Fail("The file could not be read.");

        var original = content.Position;
        try
        {
            content.Position = 0;
            if (!SignatureMatches(ext, content, length))
                return FileValidationResult.Fail("The file content does not match its file type.");
        }
        finally
        {
            content.Position = original;
        }

        return new FileValidationResult(true, null, ext, ContentTypes[ext]);
    }

    private static bool SignatureMatches(string ext, Stream content, long length)
    {
        var headLength = (int)Math.Min(length, 8192);
        var head = new byte[headLength];
        var read = 0;
        int n;
        while (read < headLength && (n = content.Read(head, read, headLength - read)) > 0) read += n;
        if (read == 0) return false;
        var span = head.AsSpan(0, read);

        switch (ext)
        {
            case ".pdf": return span.StartsWith(Pdf);
            case ".jpg":
            case ".jpeg": return span.StartsWith(Jpeg);
            case ".png": return span.StartsWith(Png);
            case ".doc":
            case ".xls":
            case ".ppt": return span.StartsWith(Ole2);
            case ".docx": return span.StartsWith(Zip) && ZipHasRoot(content, "word/");
            case ".xlsx": return span.StartsWith(Zip) && ZipHasRoot(content, "xl/");
            case ".pptx": return span.StartsWith(Zip) && ZipHasRoot(content, "ppt/");
            case ".txt": return IsText(span, length > headLength);
            default: return false;
        }
    }

    private static bool ZipHasRoot(Stream content, string root)
    {
        try
        {
            content.Position = 0;
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            return zip.Entries.Any(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsText(ReadOnlySpan<byte> sample, bool truncated)
    {
        // UTF-16 BOM
        if (sample.Length >= 2 && ((sample[0] == 0xFF && sample[1] == 0xFE) || (sample[0] == 0xFE && sample[1] == 0xFF)))
            return true;

        if (sample.IndexOf((byte)0) >= 0) return false;

        try
        {
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            var chars = new char[sample.Length + 1];
            decoder.GetChars(sample, chars, flush: !truncated);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Sanitises a client-provided file name for display and Content-Disposition use.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "file";

        // Drop any directory components regardless of separator style.
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c) || c is '"' or '<' or '>' or ':' or '|' or '?' or '*' or ';' or '%') continue;
            sb.Append(c);
        }

        var cleaned = sb.ToString().Trim().Trim('.');
        // Re-attach extension dot if trimming removed leading dots only
        if (cleaned.Length == 0) return "file";

        if (cleaned.Length > 255)
        {
            var ext = Path.GetExtension(cleaned);
            if (ext.Length > 10) ext = ext[..10];
            cleaned = cleaned[..(255 - ext.Length)] + ext;
        }
        return cleaned;
    }
}
