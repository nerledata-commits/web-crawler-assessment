using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Crawler.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Creates the schema if missing, retrying while the database container starts.
    /// EnsureCreated keeps the assessment self-contained; production would use EF migrations.
    /// Both API and worker call this; if they race on CREATE TABLE, the loser retries and finds the schema.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        const int maxAttempts = 15;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
                await db.Database.EnsureCreatedAsync(ct);
                if (db.Database.IsSqlite())
                {
                    // API and worker share the file: WAL lets readers and the writer work concurrently.
                    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                }

                logger.LogInformation("Database schema ready");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{MaxAttempts}); retrying", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
