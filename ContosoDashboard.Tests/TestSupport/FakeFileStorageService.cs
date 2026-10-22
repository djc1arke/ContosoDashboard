using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.TestSupport;

public sealed class FakeFileStorageService : IFileStorageService
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public bool FailOnUpload { get; set; }
    public bool FailOnDelete { get; set; }
    public List<string> DeletedKeys { get; } = new();

    public async Task<string> UploadAsync(Stream content, string storagePath, CancellationToken ct = default)
    {
        if (FailOnUpload) throw new IOException("Simulated storage failure.");
        if (Files.ContainsKey(storagePath)) throw new IOException("Key exists.");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Files[storagePath] = ms.ToArray();
        return storagePath;
    }

    public Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default)
    {
        if (!Files.TryGetValue(storagePath, out var bytes)) throw new FileNotFoundException();
        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        if (FailOnDelete) throw new IOException("Simulated delete failure.");
        Files.Remove(storagePath);
        DeletedKeys.Add(storagePath);
        return Task.CompletedTask;
    }

    public Task<string?> GetUrlAsync(string storagePath, TimeSpan? validFor = null, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
