using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public class DocumentServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ApplicationDbContext _ctx;
    private readonly FakeFileStorageService _storage = new();
    private readonly FakeMalwareScanService _scanner = new();
    private readonly DocumentService _sut;

    public DocumentServiceTests()
    {
        _ctx = _db.CreateContext();
        _sut = _db.CreateDocumentService(_ctx, _storage, _scanner);
    }

    public void Dispose() { _ctx.Dispose(); _db.Dispose(); }

    private static UploadRequest Req(byte[] bytes, string fileName = "report.pdf", string title = "Quarterly report",
        DocumentCategory category = DocumentCategory.Reports, int? projectId = null, params string[] tags)
        => new()
        {
            File = new FileUpload { Content = new MemoryStream(bytes), FileName = fileName, Length = bytes.Length },
            Metadata = new DocumentMetadata { Title = title, Category = category, ProjectId = projectId, Tags = tags.ToList() }
        };

    [Fact]
    public async Task Upload_success_records_uploader_date_size_type_and_guid_path()
    {
        var bytes = TestData.Pdf(500);
        var before = DateTime.UtcNow.AddSeconds(-1);

        var r = await _sut.UploadAsync(Req(bytes, "My Report (final).pdf"), TestData.EngEmployeeId);

        Assert.True(r.Succeeded, r.ErrorMessage);
        var doc = await _ctx.Documents.AsNoTracking().SingleAsync(d => d.DocumentId == r.DocumentId);
        Assert.Equal(TestData.EngEmployeeId, doc.UploadedByUserId);
        Assert.Equal(bytes.Length, doc.FileSizeBytes);
        Assert.Equal("pdf", doc.FileType);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.True(doc.UploadDate >= before && doc.UploadDate <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("My Report (final).pdf", doc.OriginalFileName);
        Assert.DoesNotContain("Report", doc.StoragePath);
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}\.pdf$", doc.StoragePath);
        Assert.True(_storage.Files.ContainsKey(doc.StoragePath));
        Assert.Equal(1, await _ctx.DocumentActivities.CountAsync(a => a.ActionType == DocumentActionType.Upload && a.DocumentId == doc.DocumentId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_title_rejected(string title)
    {
        var r = await _sut.UploadAsync(Req(TestData.Pdf(), title: title), TestData.EngEmployeeId);
        Assert.False(r.Succeeded);
        Assert.Empty(_storage.Files);
        Assert.Equal(0, await _ctx.Documents.CountAsync());
    }

    [Fact]
    public async Task Missing_category_rejected()
    {
        var r = await _sut.UploadAsync(Req(TestData.Pdf(), category: (DocumentCategory)99), TestData.EngEmployeeId);
        Assert.False(r.Succeeded);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task Oversize_unsupported_mismatch_zero_byte_and_eicar_rejected_with_nothing_stored()
    {
        var big = new byte[26_214_401];
        "%PDF-"u8.CopyTo(big);
        var cases = new (string Name, byte[] Bytes)[]
        {
            ("big.pdf", big),
            ("tool.exe", TestData.Pdf()),
            ("fake.pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }),
            ("empty.pdf", Array.Empty<byte>()),
            ("eicar.txt", TestData.Eicar()),
        };

        // EICAR is caught by the real stub scanner; use it for that case.
        var realScanner = new StubMalwareScanService();
        var sut = _db.CreateDocumentService(_ctx, _storage, realScanner);

        foreach (var (name, bytes) in cases)
        {
            var r = await sut.UploadAsync(Req(bytes, name), TestData.EngEmployeeId);
            Assert.False(r.Succeeded, name);
            Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage), name);
        }
        Assert.Empty(_storage.Files);
        Assert.Equal(0, await _ctx.Documents.CountAsync());
    }

    [Fact]
    public async Task Infected_file_from_scanner_rejected()
    {
        _scanner.Infected = true;
        var r = await _sut.UploadAsync(Req(TestData.Pdf()), TestData.EngEmployeeId);
        Assert.False(r.Succeeded);
        Assert.Contains("malware", r.ErrorMessage);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task Same_name_files_are_both_stored_without_overwrite()
    {
        var a = await _sut.UploadAsync(Req(TestData.Pdf(10), "same.pdf", "One"), TestData.EngEmployeeId);
        var b = await _sut.UploadAsync(Req(TestData.Pdf(20), "same.pdf", "Two"), TestData.EngEmployeeId);
        Assert.True(a.Succeeded && b.Succeeded);
        Assert.Equal(2, _storage.Files.Count);
        var paths = await _ctx.Documents.Select(d => d.StoragePath).Distinct().CountAsync();
        Assert.Equal(2, paths);
    }

    [Fact]
    public async Task Tags_are_normalized_trimmed_lowercased_deduped()
    {
        var r = await _sut.UploadAsync(Req(TestData.Pdf(), tags: new[] { " Budget ", "BUDGET", "q3", "", "  " }), TestData.EngEmployeeId);
        Assert.True(r.Succeeded);
        var tags = await _ctx.DocumentTags.Where(t => t.DocumentId == r.DocumentId).Select(t => t.Tag).OrderBy(t => t).ToListAsync();
        Assert.Equal(new[] { "budget", "q3" }, tags);
    }

    [Fact]
    public async Task Too_many_or_too_long_tags_rejected()
    {
        var many = Enumerable.Range(0, 11).Select(i => "t" + i).ToArray();
        Assert.False((await _sut.UploadAsync(Req(TestData.Pdf(), tags: many), TestData.EngEmployeeId)).Succeeded);

        var ten = Enumerable.Range(0, 10).Select(i => "t" + i).ToArray();
        Assert.True((await _sut.UploadAsync(Req(TestData.Pdf(), tags: ten), TestData.EngEmployeeId)).Succeeded);

        Assert.False((await _sut.UploadAsync(Req(TestData.Pdf(), tags: new[] { new string('x', 31) }), TestData.EngEmployeeId)).Succeeded);
        Assert.True((await _sut.UploadAsync(Req(TestData.Pdf(), tags: new[] { new string('x', 30) }), TestData.EngEmployeeId)).Succeeded);
    }

    [Fact]
    public async Task Storage_failure_leaves_no_record_or_file()
    {
        _storage.FailOnUpload = true;
        var r = await _sut.UploadAsync(Req(TestData.Pdf()), TestData.EngEmployeeId);
        Assert.False(r.Succeeded);
        Assert.Equal(0, await _ctx.Documents.CountAsync());
        Assert.Equal(0, await _ctx.DocumentActivities.CountAsync());
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task Db_save_failure_triggers_compensating_file_delete()
    {
        // Administrator may associate any existing project; the project row is removed right after the
        // file is stored so the metadata save fails on the FK and the stored file must be cleaned up.
        var hooking = new HookedStorage(_storage, () =>
        {
            using var other = _db.CreateContext();
            other.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            other.Database.ExecuteSqlRaw("DELETE FROM Projects WHERE ProjectId = 10");
        });
        var sut = _db.CreateDocumentService(_ctx, hooking);

        var r = await sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.OtherProject), TestData.AdminId);

        Assert.False(r.Succeeded);
        Assert.Equal(0, await _ctx.Documents.CountAsync());
        Assert.Single(_storage.DeletedKeys);
        Assert.Empty(_storage.Files);
    }

    private sealed class HookedStorage : IFileStorageService
    {
        private readonly FakeFileStorageService _inner;
        private readonly Action _afterUpload;
        public HookedStorage(FakeFileStorageService inner, Action afterUpload) { _inner = inner; _afterUpload = afterUpload; }
        public async Task<string> UploadAsync(Stream c, string p, CancellationToken ct = default)
        { var k = await _inner.UploadAsync(c, p, ct); _afterUpload(); return k; }
        public Task<Stream> DownloadAsync(string p, CancellationToken ct = default) => _inner.DownloadAsync(p, ct);
        public Task DeleteAsync(string p, CancellationToken ct = default) => _inner.DeleteAsync(p, ct);
        public Task<string?> GetUrlAsync(string p, TimeSpan? v = null, CancellationToken ct = default) => _inner.GetUrlAsync(p, v, ct);
    }

    [Fact]
    public async Task Project_association_refused_for_non_member_allowed_for_member_manager_admin()
    {
        var denied = await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.Project1), TestData.SalesEmployeeId);
        Assert.False(denied.Succeeded);
        Assert.Empty(_storage.Files);

        var deniedOtherPm = await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.Project1), TestData.OtherProjectManagerId);
        Assert.False(deniedOtherPm.Succeeded);

        Assert.True((await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.Project1), TestData.EngMemberId)).Succeeded);
        Assert.True((await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.Project1), TestData.ProjectManagerId)).Succeeded);
        Assert.True((await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.OtherProject), TestData.AdminId)).Succeeded);
    }

    [Fact]
    public async Task Upload_notifies_other_project_members_but_not_uploader()
    {
        var r = await _sut.UploadAsync(Req(TestData.Pdf(), projectId: TestData.Project1), TestData.EngEmployeeId);
        Assert.True(r.Succeeded);

        var recipients = await _ctx.Notifications.Where(n => n.Type == NotificationType.DocumentAdded).Select(n => n.UserId).ToListAsync();
        Assert.DoesNotContain(TestData.EngEmployeeId, recipients);
        Assert.Contains(TestData.ProjectManagerId, recipients);
        Assert.Contains(TestData.EngTeamLeadId, recipients);
    }

    [Fact]
    public async Task Non_seekable_stream_is_buffered_and_uploaded()
    {
        var bytes = TestData.Pdf(50);
        var req = Req(bytes);
        req.File.Content = new NonSeekable(bytes);
        var r = await _sut.UploadAsync(req, TestData.EngEmployeeId);
        Assert.True(r.Succeeded, r.ErrorMessage);
        Assert.Equal(bytes, _storage.Files.Single().Value);
    }

    private sealed class NonSeekable : MemoryStream
    {
        public NonSeekable(byte[] b) : base(b) { }
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task Unknown_user_cannot_upload()
    {
        var r = await _sut.UploadAsync(Req(TestData.Pdf()), 9999);
        Assert.False(r.Succeeded);
        Assert.Empty(_storage.Files);
    }
}
