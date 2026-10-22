using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public class DocumentDeliveryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ApplicationDbContext _ctx;
    private readonly FakeFileStorageService _storage = new();
    private readonly DocumentService _sut;

    public DocumentDeliveryTests()
    {
        _ctx = _db.CreateContext();
        _sut = _db.CreateDocumentService(_ctx, _storage);
    }

    public void Dispose() { _ctx.Dispose(); _db.Dispose(); }

    private async Task<int> UploadAsync(byte[] bytes, string fileName, int userId = TestData.EngEmployeeId, int? projectId = null)
    {
        var r = await _sut.UploadAsync(new UploadRequest
        {
            File = new FileUpload { Content = new MemoryStream(bytes), FileName = fileName, Length = bytes.Length },
            Metadata = new DocumentMetadata { Title = "T", Category = DocumentCategory.Reports, ProjectId = projectId }
        }, userId);
        Assert.True(r.Succeeded, r.ErrorMessage);
        return r.DocumentId!.Value;
    }

    private Task<int> ActivityCount(DocumentActionType t, int docId)
        => _ctx.DocumentActivities.CountAsync(a => a.DocumentId == docId && a.ActionType == t);

    [Fact]
    public async Task Download_returns_original_bytes_name_and_type_and_logs_one_download()
    {
        var bytes = TestData.Pdf(300);
        var id = await UploadAsync(bytes, "Plan v2.pdf");

        await using var doc = await _sut.OpenReadAsync(id, TestData.EngEmployeeId, DocumentAccessKind.Download);

        Assert.NotNull(doc);
        using var ms = new MemoryStream();
        await doc!.Stream.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
        Assert.Equal("Plan v2.pdf", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(bytes.Length, doc.Length);
        Assert.Equal(1, await ActivityCount(DocumentActionType.Download, id));
        Assert.Equal(0, await ActivityCount(DocumentActionType.Preview, id));
    }

    [Fact]
    public async Task Preview_logs_distinct_preview_activity_for_pdf_and_png()
    {
        var pdf = await UploadAsync(TestData.Pdf(), "a.pdf");
        var png = await UploadAsync(TestData.Png(), "a.png");

        await using var a = await _sut.OpenReadAsync(pdf, TestData.EngEmployeeId, DocumentAccessKind.Preview);
        await using var b = await _sut.OpenReadAsync(png, TestData.EngEmployeeId, DocumentAccessKind.Preview);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal("image/png", b!.ContentType);
        Assert.Equal(1, await ActivityCount(DocumentActionType.Preview, pdf));
        Assert.Equal(1, await ActivityCount(DocumentActionType.Preview, png));
        Assert.Equal(0, await ActivityCount(DocumentActionType.Download, pdf));
    }

    [Fact]
    public async Task Preview_of_non_previewable_type_is_null_but_download_works()
    {
        var id = await UploadAsync(TestData.Text("hi"), "notes.txt");

        Assert.Null(await _sut.OpenReadAsync(id, TestData.EngEmployeeId, DocumentAccessKind.Preview));
        Assert.Equal(0, await ActivityCount(DocumentActionType.Preview, id));
        await using var dl = await _sut.OpenReadAsync(id, TestData.EngEmployeeId, DocumentAccessKind.Download);
        Assert.NotNull(dl);
    }

    [Fact]
    public async Task Missing_inaccessible_and_unknown_user_are_indistinguishable_null_with_no_activity()
    {
        var id = await UploadAsync(TestData.Pdf(), "a.pdf");

        Assert.Null(await _sut.OpenReadAsync(9999, TestData.EngEmployeeId, DocumentAccessKind.Download));       // missing id
        Assert.Null(await _sut.OpenReadAsync(id, TestData.SalesEmployeeId, DocumentAccessKind.Download));       // unauthorized
        Assert.Null(await _sut.OpenReadAsync(id, TestData.SalesTeamLeadId, DocumentAccessKind.Preview));        // other-dept lead
        Assert.Null(await _sut.OpenReadAsync(id, 424242, DocumentAccessKind.Download));                          // unknown user
        Assert.Equal(0, await _ctx.DocumentActivities.CountAsync(a => a.ActionType != DocumentActionType.Upload));
    }

    [Fact]
    public async Task Missing_stored_file_returns_null_and_records_no_activity()
    {
        var id = await UploadAsync(TestData.Pdf(), "a.pdf");
        _storage.Files.Clear();

        Assert.Null(await _sut.OpenReadAsync(id, TestData.EngEmployeeId, DocumentAccessKind.Download));
        Assert.Equal(0, await ActivityCount(DocumentActionType.Download, id));
    }

    [Fact]
    public async Task Project_members_can_download_project_documents()
    {
        var id = await UploadAsync(TestData.Pdf(), "p.pdf", projectId: TestData.Project1);
        await using var asMember = await _sut.OpenReadAsync(id, TestData.EngMemberId, DocumentAccessKind.Download);
        await using var asManager = await _sut.OpenReadAsync(id, TestData.ProjectManagerId, DocumentAccessKind.Download);
        Assert.NotNull(asMember);
        Assert.NotNull(asManager);
        Assert.Null(await _sut.OpenReadAsync(id, TestData.OtherProjectManagerId, DocumentAccessKind.Download));
    }

    [Fact]
    public async Task GetDocument_returns_null_for_inaccessible()
    {
        var id = await UploadAsync(TestData.Pdf(), "a.pdf");
        Assert.Null(await _sut.GetDocumentAsync(id, TestData.SalesEmployeeId));
        Assert.Null(await _sut.GetDocumentAsync(99999, TestData.EngEmployeeId));
    }

    [Fact]
    public async Task GetDocument_capability_flags_per_role()
    {
        var id = await UploadAsync(TestData.Pdf(), "a.pdf", projectId: TestData.Project1);

        async Task<(bool edit, bool del, bool replace, bool share)> Flags(int user)
        {
            var d = await _sut.GetDocumentAsync(id, user);
            Assert.NotNull(d);
            return (d!.CanEdit, d.CanDelete, d.CanReplace, d.CanShare);
        }

        Assert.Equal((true, true, true, true), await Flags(TestData.EngEmployeeId));       // uploader
        Assert.Equal((true, true, false, false), await Flags(TestData.AdminId));            // administrator
        Assert.Equal((true, true, false, false), await Flags(TestData.ProjectManagerId));   // project manager
        Assert.Equal((true, true, false, false), await Flags(TestData.EngTeamLeadId));      // same-dept team lead
        Assert.Equal((false, false, false, false), await Flags(TestData.EngMemberId));      // plain project member
    }

    [Fact]
    public async Task GetDocument_populates_details()
    {
        var id = await UploadAsync(TestData.Pdf(), "a.pdf");
        var d = await _sut.GetDocumentAsync(id, TestData.EngEmployeeId);
        Assert.Equal("Ni Kang", d!.UploadedByName);
        Assert.True(d.IsPreviewable);
        Assert.Equal("application/pdf", d.ContentType);
    }
}
