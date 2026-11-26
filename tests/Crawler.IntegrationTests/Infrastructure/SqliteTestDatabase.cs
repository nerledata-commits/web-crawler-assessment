using Crawler.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>In-memory SQLite database that lives as long as this object (the connection stays open).</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    public SqliteConnection Connection { get; } = new("Data Source=:memory:");

    public SqliteTestDatabase()
    {
        Connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<CrawlerDbContext> Options =>
        new DbContextOptionsBuilder<CrawlerDbContext>().UseSqlite(Connection).Options;

    public CrawlerDbContext CreateContext() => new(Options);

    public void Dispose() => Connection.Dispose();
}
