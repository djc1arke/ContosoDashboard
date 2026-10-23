using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ContosoDashboard.Data;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.TestSupport;

/// <summary>SQLite in-memory database (FK + check constraints enforced). One open connection per instance.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        TestData.Seed(ctx);
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        return new ApplicationDbContext(options);
    }

    public DocumentService CreateDocumentService(
        ApplicationDbContext ctx,
        IFileStorageService? storage = null,
        IMalwareScanService? scanner = null)
        => new(ctx,
            storage ?? new FakeFileStorageService(),
            scanner ?? new FakeMalwareScanService(),
            new NotificationService(ctx),
            NullLogger<DocumentService>.Instance);

    public void Dispose() => _connection.Dispose();
}
