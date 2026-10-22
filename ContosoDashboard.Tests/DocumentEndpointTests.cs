using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public sealed class EndpointFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");
    public FakeFileStorageService Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var toRemove = services.Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                            || d.ServiceType == typeof(DbContextOptions)).ToList();
            foreach (var d in toRemove) services.Remove(d);
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<IFileStorageService>();
            services.AddSingleton<IFileStorageService>(Storage);

            services.AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.Scheme;
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

file static class ServiceCollectionExtras
{
    public static void RemoveAll<T>(this IServiceCollection s)
    {
        foreach (var d in s.Where(x => x.ServiceType == typeof(T)).ToList()) s.Remove(d);
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var id)) return Task.FromResult(AuthenticateResult.NoResult());
        var role = Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Employee";
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Role, role)
        }, Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public class DocumentEndpointTests : IClassFixture<EndpointFactory>
{
    private readonly EndpointFactory _factory;
    public DocumentEndpointTests(EndpointFactory factory) => _factory = factory;

    private HttpClient Client(int? user = null, string role = "Employee")
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (user is int u)
        {
            c.DefaultRequestHeaders.Add("X-Test-User", u.ToString());
            c.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        return c;
    }

    private async Task<int> SeedDocAsync(string name = "My plan.pdf", string type = "pdf", string contentType = "application/pdf")
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.EnsureCreatedAsync();
        if (!await ctx.Users.AnyAsync(u => u.UserId == TestData.SalesEmployeeId)) TestData.Seed(ctx);
        var path = $"2026/01/{Guid.NewGuid():N}.{type}";
        _factory.Storage.Files[path] = TestData.Pdf(64);
        var doc = new Document
        {
            Title = "Plan", OriginalFileName = name, FileType = type, ContentType = contentType,
            FileSizeBytes = 64, StoragePath = path, Category = DocumentCategory.Reports,
            UploadedByUserId = TestData.EngEmployeeId, UploadDate = DateTime.UtcNow
        };
        ctx.Documents.Add(doc);
        await ctx.SaveChangesAsync();
        return doc.DocumentId;
    }

    [Fact]
    public async Task Unauthenticated_requests_are_challenged_and_do_not_leak()
    {
        var id = await SeedDocAsync();
        var r = await Client().GetAsync($"/documents/{id}/download");
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_download_returns_attachment_headers()
    {
        var id = await SeedDocAsync();
        var r = await Client(TestData.EngEmployeeId).GetAsync($"/documents/{id}/download");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", r.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains("My plan.pdf", r.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains("nosniff", r.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(_factory.Storage.Files.Values.Last().Length, (await r.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Preview_is_inline()
    {
        var id = await SeedDocAsync();
        var r = await Client(TestData.EngEmployeeId).GetAsync($"/documents/{id}/preview");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("inline", r.Content.Headers.ContentDisposition?.ToString());
    }

    [Fact]
    public async Task Unauthorized_user_and_missing_document_get_identical_404()
    {
        var id = await SeedDocAsync();
        var c = Client(TestData.SalesEmployeeId);
        var denied = await c.GetAsync($"/documents/{id}/download");
        var missing = await c.GetAsync("/documents/987654/download");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_of_non_previewable_type_is_404()
    {
        var id = await SeedDocAsync("n.txt", "txt", "text/plain");
        var r = await Client(TestData.EngEmployeeId).GetAsync($"/documents/{id}/preview");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
