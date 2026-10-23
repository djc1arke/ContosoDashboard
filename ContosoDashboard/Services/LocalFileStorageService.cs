namespace ContosoDashboard.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["FileStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = "AppData/uploads";
        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Test/alternate constructor with an explicit absolute root.</summary>
    public LocalFileStorageService(string rootPath)
    {
        _root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private string Resolve(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
            throw new InvalidOperationException("Invalid storage path.");

        var full = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        return full;
    }

    public async Task<string> UploadAsync(Stream content, string storagePath, CancellationToken ct = default)
    {
        var full = Resolve(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // FileMode.CreateNew guarantees an existing key is never overwritten.
        await using var target = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(target, 81920, ct);
        return storagePath;
    }

    public Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Resolve(storagePath);
        if (!File.Exists(full)) throw new FileNotFoundException("Stored file not found.");
        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Resolve(storagePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public Task<string?> GetUrlAsync(string storagePath, TimeSpan? validFor = null, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
