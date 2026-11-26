using Crawler.Api.Contracts;
using Crawler.Core.Application;
using Crawler.Core.Domain;
using Crawler.Core.Messaging;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crawler.Api.Services;

public enum CancelResult
{
    Canceled,
    NotFound,
    AlreadyFinished
}

/// <summary>The job could not be handed to the broker; the job is marked Failed.</summary>
public sealed class JobEnqueueException(string message, Exception inner) : Exception(message, inner);

public sealed class JobService(
    CrawlerDbContext db,
    IJobPublisher publisher,
    TimeProvider clock,
    ILogger<JobService> logger)
{
    public const int MaxPageSize = 100;

    /// <summary>Normalizes and validates the start URL. Returns null if it is not an absolute http(s) URL.</summary>
    public static string? NormalizeStartUrl(string? url) => UrlNormalizer.Normalize(url);

    public async Task<Guid> CreateAsync(string normalizedUrl, int? maxDepth, string correlationId, CancellationToken ct)
    {
        var job = new CrawlJob
        {
            Id = Guid.NewGuid(),
            Url = normalizedUrl,
            MaxDepth = maxDepth ?? CrawlJob.DefaultMaxDepth,
            Status = JobStatus.Pending,
            CreatedAt = Now,
            CorrelationId = correlationId
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        using var _ = logger.BeginScope("JobId:{JobId}", job.Id);
        try
        {
            await publisher.PublishAsync(
                new CrawlJobRequested(Guid.NewGuid(), job.Id, job.Url, job.MaxDepth, correlationId, job.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            // Without an outbox the DB insert and the publish are not atomic: fail the job visibly
            // instead of leaving it Pending forever.
            logger.LogError(ex, "Could not publish job to the broker");
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.FailureReason, "Could not enqueue the job (message broker unavailable).")
                .SetProperty(j => j.CompletedAt, Now), CancellationToken.None);
            throw new JobEnqueueException("The message broker is unavailable. Please try again.", ex);
        }

        logger.LogInformation("Job created for {Url} (maxDepth {MaxDepth})", job.Url, job.MaxDepth);
        return job.Id;
    }

    public Task<JobSummaryDto?> GetAsync(Guid jobId, CancellationToken ct) =>
        db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(ToSummary)
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<JobSummaryDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var total = await db.Jobs.CountAsync(ct);
        var items = await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return new PagedResult<JobSummaryDto>(items, page, pageSize, total);
    }

    /// <summary>Tree of pages by first discovery (ParentPageId). Available while running (partial) and when done.</summary>
    public async Task<JobTreeDto?> GetTreeAsync(Guid jobId, CancellationToken ct)
    {
        var status = await db.Jobs.Where(j => j.Id == jobId).Select(j => (JobStatus?)j.Status).FirstOrDefaultAsync(ct);
        if (status is null)
        {
            return null;
        }

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderBy(p => p.Depth).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var childrenByParent = pages.ToLookup(p => p.ParentPageId);
        var root = childrenByParent[null].FirstOrDefault();

        return new JobTreeDto(jobId, status.Value, pages.Count, root is null ? null : BuildNode(root));

        PageNodeDto BuildNode(CrawledPage page) => new(
            page.Id, page.Url, page.Depth, page.HttpStatusCode, page.Error,
            page.OutgoingLinkCount, page.InternalLinkCount, page.DomainLinkRatio,
            childrenByParent[page.Id].Select(BuildNode).ToList());
    }

    public async Task<IReadOnlyList<PageLinkDto>?> GetLinksAsync(Guid jobId, long pageId, CancellationToken ct)
    {
        var pageExists = await db.Pages.AnyAsync(p => p.Id == pageId && p.JobId == jobId, ct);
        if (!pageExists)
        {
            return null;
        }

        return await db.PageLinks.AsNoTracking()
            .Where(l => l.PageId == pageId)
            .OrderBy(l => l.Id)
            .Select(l => new PageLinkDto(l.Url, l.IsInternal))
            .ToListAsync(ct);
    }

    public async Task<CancelResult> CancelAsync(Guid jobId, CancellationToken ct)
    {
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Canceled)
                .SetProperty(j => j.CompletedAt, Now), ct);

        if (updated > 0)
        {
            logger.LogInformation("Job {JobId} canceled", jobId);
            return CancelResult.Canceled;
        }

        return await db.Jobs.AnyAsync(j => j.Id == jobId, ct) ? CancelResult.AlreadyFinished : CancelResult.NotFound;
    }

    private static readonly System.Linq.Expressions.Expression<Func<CrawlJob, JobSummaryDto>> ToSummary = j =>
        new JobSummaryDto(j.Id, j.Url, j.MaxDepth, j.Status, j.CreatedAt, j.StartedAt, j.CompletedAt,
            j.FailureReason, j.PagesCrawled, j.PagesQueued);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
