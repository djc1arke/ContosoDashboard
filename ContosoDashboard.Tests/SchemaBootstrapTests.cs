using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;

namespace ContosoDashboard.Tests;

public class SchemaBootstrapTests
{
    [Fact]
    public async Task SqlServer_schema_creates_on_localdb_with_document_tables()
    {
        var cs = "Server=(localdb)\\MSSQLLocalDB;Database=ContosoSchemaCheck_" + Guid.NewGuid().ToString("N") + ";Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options;
        await using var ctx = new ApplicationDbContext(options);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            foreach (var t in new[] { "Documents", "DocumentTags", "DocumentShares", "DocumentActivities", "TaskDocuments" })
                await ctx.Database.ExecuteSqlRawAsync($"IF OBJECT_ID('{t}') IS NULL THROW 50000, 'missing {t}', 1;");
        }
        finally { await ctx.Database.EnsureDeletedAsync(); }
    }
}
