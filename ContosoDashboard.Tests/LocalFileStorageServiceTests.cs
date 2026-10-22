using ContosoDashboard.Services;

namespace ContosoDashboard.Tests;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "contoso-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _sut;

    public LocalFileStorageServiceTests() => _sut = new LocalFileStorageService(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Upload_download_round_trip()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var key = await _sut.UploadAsync(new MemoryStream(data), "2026/10/abc.pdf");
        Assert.Equal("2026/10/abc.pdf", key);

        await using var s = await _sut.DownloadAsync(key);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        Assert.Equal(data, ms.ToArray());
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("2026/../../escape.pdf")]
    [InlineData("/absolute.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("")]
    public async Task Path_traversal_and_absolute_paths_rejected(string key)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UploadAsync(new MemoryStream(new byte[1]), key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DownloadAsync(key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(key));
    }

    [Fact]
    public async Task Existing_key_is_never_overwritten()
    {
        await _sut.UploadAsync(new MemoryStream(new byte[] { 1 }), "k/a.pdf");
        await Assert.ThrowsAsync<IOException>(() => _sut.UploadAsync(new MemoryStream(new byte[] { 2 }), "k/a.pdf"));

        await using var s = await _sut.DownloadAsync("k/a.pdf");
        Assert.Equal(1, s.ReadByte());
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        await _sut.UploadAsync(new MemoryStream(new byte[] { 1 }), "k/a.pdf");
        await _sut.DeleteAsync("k/a.pdf");
        await _sut.DeleteAsync("k/a.pdf");
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.DownloadAsync("k/a.pdf"));
    }

    [Fact]
    public async Task Missing_file_throws_FileNotFound()
        => await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.DownloadAsync("nope/none.pdf"));

    [Fact]
    public async Task GetUrl_returns_null_locally()
        => Assert.Null(await _sut.GetUrlAsync("k/a.pdf"));
}
