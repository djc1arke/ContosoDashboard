namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    /// <summary>Stores the stream under the given provider-neutral key and returns the key. Never overwrites.</summary>
    Task<string> UploadAsync(Stream content, string storagePath, CancellationToken ct = default);

    /// <summary>Opens the stored file. Throws <see cref="FileNotFoundException"/> when missing.</summary>
    Task<Stream> DownloadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Idempotent delete.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>Returns a direct URL when the provider supports it; null for local storage.</summary>
    Task<string?> GetUrlAsync(string storagePath, TimeSpan? validFor = null, CancellationToken ct = default);
}
