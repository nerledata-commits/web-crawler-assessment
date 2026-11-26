using Crawler.Core.Application;
using Crawler.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// Worker-side persistence. Upserts use INSERT ... ON CONFLICT, which is supported by both
/// PostgreSQL and SQLite, so redelivered messages never create duplicate rows.
/// </summary>
public sealed class EfCrawlStore(CrawlerDbContext db) : ICrawlStore
{
    private const int LinkInsertBatchSize = 200;

    public Task<CrawlJob?> GetJobAsync(Guid jobId, CancellationToken ct) =>
        db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct) =>
        await db.Jobs.Where(j => j.Id == jobId).Select(j => (JobStatus?)j.Status).FirstOrDefaultAsync(ct);

    public async Task<bool> TryMarkRunningAsync(Guid jobId, DateTime now, CancellationToken ct)
    {
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, j => j.StartedAt ?? now), ct);
        return updated > 0;
    }

    public async Task<long> UpsertPageAsync(CrawledPage page, CancellationToken ct)
    {
        // Depth and ParentPageId keep their first-discovered values; fetch results are refreshed.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Pages" ("JobId", "Url", "ParentPageId", "Depth", "HttpStatusCode", "ContentType",
                                 "OutgoingLinkCount", "InternalLinkCount", "DomainLinkRatio", "Error", "CrawledAt")
            VALUES ({page.JobId}, {page.Url}, {page.ParentPageId}, {page.Depth}, {page.HttpStatusCode}, {page.ContentType},
                    {page.OutgoingLinkCount}, {page.InternalLinkCount}, {page.DomainLinkRatio}, {page.Error}, {page.CrawledAt})
            ON CONFLICT ("JobId", "Url") DO UPDATE SET
                "HttpStatusCode" = EXCLUDED."HttpStatusCode",
                "ContentType" = EXCLUDED."ContentType",
                "OutgoingLinkCount" = EXCLUDED."OutgoingLinkCount",
                "InternalLinkCount" = EXCLUDED."InternalLinkCount",
                "DomainLinkRatio" = EXCLUDED."DomainLinkRatio",
                "Error" = EXCLUDED."Error",
                "CrawledAt" = EXCLUDED."CrawledAt"
            """, ct);

        return await db.Pages
            .Where(p => p.JobId == page.JobId && p.Url == page.Url)
            .Select(p => p.Id)
            .SingleAsync(ct);
    }

    public async Task AddLinksAsync(IReadOnlyList<PageLink> links, CancellationToken ct)
    {
        foreach (var batch in links.Chunk(LinkInsertBatchSize))
        {
            var parameters = new List<object>(batch.Length * 4);
            var rows = new List<string>(batch.Length);
            foreach (var link in batch)
            {
                var i = parameters.Count;
                rows.Add($"({{{i}}}, {{{i + 1}}}, {{{i + 2}}}, {{{i + 3}}})");
                parameters.Add(link.JobId);
                parameters.Add(link.PageId);
                parameters.Add(link.Url);
                parameters.Add(link.IsInternal);
            }

            var sql = $"""
                INSERT INTO "PageLinks" ("JobId", "PageId", "Url", "IsInternal")
                VALUES {string.Join(", ", rows)}
                ON CONFLICT ("PageId", "Url") DO NOTHING
                """;
            await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    public Task UpdateProgressAsync(Guid jobId, int pagesCrawled, int pagesQueued, CancellationToken ct) =>
        db.Jobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.PagesCrawled, pagesCrawled)
                .SetProperty(j => j.PagesQueued, pagesQueued), ct);

    public Task MarkCompletedAsync(Guid jobId, DateTime now, CancellationToken ct) =>
        db.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedAt, now), ct);

    public Task MarkFailedAsync(Guid jobId, string reason, DateTime now, CancellationToken ct) =>
        db.Jobs
            .Where(j => j.Id == jobId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.FailureReason, Truncate(reason, 2000))
                .SetProperty(j => j.CompletedAt, now), ct);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
