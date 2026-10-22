using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public class DocumentQueryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ApplicationDbContext _ctx;
    private readonly DocumentService _sut;

    public DocumentQueryTests()
    {
        _ctx = _db.CreateContext();
        _sut = _db.CreateDocumentService(_ctx);
    }

    public void Dispose() { _ctx.Dispose(); _db.Dispose(); }

    private static readonly DateTime Day = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private void SeedMine()
    {
        TestData.AddDocument(_ctx, TestData.EngEmployeeId, "Bravo", DocumentCategory.Reports, null, "pdf", 300, Day.AddDays(1), "budget summary");
        TestData.AddDocument(_ctx, TestData.EngEmployeeId, "Alpha", DocumentCategory.Presentations, TestData.Project1, "pdf", 100, Day.AddDays(3), null, "roadmap");
        TestData.AddDocument(_ctx, TestData.EngEmployeeId, "Charlie", DocumentCategory.Other, null, "png", 200, Day.AddDays(2));
        // Another user's document, not shared
        TestData.AddDocument(_ctx, TestData.SalesEmployeeId, "Secret", DocumentCategory.Reports, null, "pdf", 50, Day);
    }

    private static List<string> Titles(PagedResult<DocumentListItem> r) => r.Items.Select(i => i.Title).ToList();

    [Fact]
    public async Task My_documents_returns_only_uploaders_documents()
    {
        SeedMine();
        var r = await _sut.GetMyDocumentsAsync(new DocumentQuery(), TestData.EngEmployeeId);
        Assert.Equal(3, r.TotalCount);
        Assert.DoesNotContain("Secret", Titles(r));
    }

    [Theory]
    [InlineData(DocumentSortBy.Title, false, "Alpha,Bravo,Charlie")]
    [InlineData(DocumentSortBy.Title, true, "Charlie,Bravo,Alpha")]
    [InlineData(DocumentSortBy.UploadDate, false, "Bravo,Charlie,Alpha")]
    [InlineData(DocumentSortBy.UploadDate, true, "Alpha,Charlie,Bravo")]
    [InlineData(DocumentSortBy.FileSize, false, "Alpha,Charlie,Bravo")]
    [InlineData(DocumentSortBy.FileSize, true, "Bravo,Charlie,Alpha")]
    [InlineData(DocumentSortBy.Category, false, "Bravo,Alpha,Charlie")] // Reports(3), Presentations(4), Other(5)
    [InlineData(DocumentSortBy.Category, true, "Charlie,Alpha,Bravo")]
    public async Task Sorting(DocumentSortBy by, bool desc, string expected)
    {
        SeedMine();
        var r = await _sut.GetMyDocumentsAsync(new DocumentQuery { SortBy = by, Descending = desc }, TestData.EngEmployeeId);
        Assert.Equal(expected.Split(','), Titles(r));
    }

    [Fact]
    public async Task Filters_alone_and_combined()
    {
        SeedMine();
        var byCat = await _sut.GetMyDocumentsAsync(new DocumentQuery { Category = DocumentCategory.Reports }, TestData.EngEmployeeId);
        Assert.Equal(new[] { "Bravo" }, Titles(byCat));

        var byProject = await _sut.GetMyDocumentsAsync(new DocumentQuery { ProjectId = TestData.Project1 }, TestData.EngEmployeeId);
        Assert.Equal(new[] { "Alpha" }, Titles(byProject));

        var byDate = await _sut.GetMyDocumentsAsync(new DocumentQuery { DateFrom = Day.AddDays(2).Date, DateTo = Day.AddDays(3).Date, SortBy = DocumentSortBy.Title, Descending = false }, TestData.EngEmployeeId);
        Assert.Equal(new[] { "Alpha", "Charlie" }, Titles(byDate));

        var combined = await _sut.GetMyDocumentsAsync(new DocumentQuery { Category = DocumentCategory.Presentations, ProjectId = TestData.Project1, DateFrom = Day.AddDays(3).Date }, TestData.EngEmployeeId);
        Assert.Equal(new[] { "Alpha" }, Titles(combined));

        var none = await _sut.GetMyDocumentsAsync(new DocumentQuery { Category = DocumentCategory.TeamResources }, TestData.EngEmployeeId);
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task Search_matches_title_description_tag_uploader_and_project()
    {
        SeedMine();
        Assert.Equal(new[] { "Charlie" }, Titles(await _sut.SearchAsync("harl", new DocumentQuery(), TestData.EngEmployeeId)));
        Assert.Equal(new[] { "Bravo" }, Titles(await _sut.SearchAsync("budget", new DocumentQuery(), TestData.EngEmployeeId)));
        Assert.Equal(new[] { "Alpha" }, Titles(await _sut.SearchAsync("ROADMAP", new DocumentQuery(), TestData.EngEmployeeId)));
        Assert.Equal(3, (await _sut.SearchAsync("Ni Kang", new DocumentQuery(), TestData.EngEmployeeId)).TotalCount);
        Assert.Equal(new[] { "Alpha" }, Titles(await _sut.SearchAsync("ContosoDashboard Development", new DocumentQuery(), TestData.EngEmployeeId)));
    }

    [Fact]
    public async Task Search_wildcards_are_treated_literally()
    {
        SeedMine();
        Assert.Empty((await _sut.SearchAsync("%", new DocumentQuery(), TestData.EngEmployeeId)).Items);
        Assert.Empty((await _sut.SearchAsync("_", new DocumentQuery(), TestData.EngEmployeeId)).Items);
    }

    [Fact]
    public async Task Search_never_returns_or_counts_inaccessible_documents()
    {
        SeedMine();
        var other = await _sut.SearchAsync("Secret", new DocumentQuery(), TestData.EngEmployeeId);
        Assert.Empty(other.Items);
        Assert.Equal(0, other.TotalCount);

        // Another-department employee cannot see Ni Kang's personal documents at all
        var outsider = await _sut.SearchAsync("a", new DocumentQuery(), TestData.SalesEmployeeId);
        Assert.Equal(new[] { "Secret" }, Titles(outsider));
        Assert.Equal(1, outsider.TotalCount);

        // Once shared, the recipient finds it
        var doc = _ctx.Documents.First(d => d.Title == "Bravo");
        _ctx.DocumentShares.Add(new DocumentShare { DocumentId = doc.DocumentId, SharedWithUserId = TestData.SalesEmployeeId, SharedByUserId = TestData.EngEmployeeId });
        _ctx.SaveChanges();
        var shared = await _sut.SearchAsync("Bravo", new DocumentQuery(), TestData.SalesEmployeeId);
        Assert.Single(shared.Items);
    }

    [Fact]
    public async Task Paging_uses_page_size_and_reports_total()
    {
        for (var i = 0; i < 30; i++)
            TestData.AddDocument(_ctx, TestData.EngEmployeeId, $"Doc{i:00}", uploaded: Day.AddMinutes(i));

        var q = new DocumentQuery { SortBy = DocumentSortBy.Title, Descending = false, Page = 1, PageSize = 25 };
        var p1 = await _sut.GetMyDocumentsAsync(q, TestData.EngEmployeeId);
        Assert.Equal(25, p1.Items.Count);
        Assert.Equal(30, p1.TotalCount);
        q.Page = 2;
        var p2 = await _sut.GetMyDocumentsAsync(q, TestData.EngEmployeeId);
        Assert.Equal(5, p2.Items.Count);
        Assert.Equal("Doc25", p2.Items[0].Title);
    }

    [Fact]
    public async Task Unknown_user_gets_empty_results()
    {
        SeedMine();
        Assert.Empty((await _sut.GetMyDocumentsAsync(new DocumentQuery(), 9999)).Items);
        Assert.Empty((await _sut.SearchAsync("a", new DocumentQuery(), 9999)).Items);
    }
}
