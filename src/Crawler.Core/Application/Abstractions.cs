using Crawler.Core.Domain;
using Crawler.Core.Messaging;

namespace Crawler.Core.Application;

public sealed record FetchResult(Uri FinalUri, int? StatusCode, string? ContentType, string? Html, string? Error)
{
    public bool IsHtml => Html is not null;
}

public interface IPageFetcher
{
    /// <summary>Fetches a page. Never throws for HTTP/network problems: they are reported in <see cref="FetchResult.Error"/>.</summary>
    Task<FetchResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record HtmlLinks(string? BaseHref, IReadOnlyList<string> Hrefs);

public interface ILinkExtractor
{
    HtmlLinks Extract(string html);
}

/// <summary>Persistence used by the worker. Every write is idempotent so a redelivered message is safe.</summary>
public interface ICrawlStore
{
    Task<CrawlJob?> GetJobAsync(Guid jobId, CancellationToken ct);
    Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct);

    /// <summary>Pending/Running -> Running. Returns false if the job is already terminal.</summary>
    Task<bool> TryMarkRunningAsync(Guid jobId, DateTime now, CancellationToken ct);

    /// <summary>Insert or update the page keyed by (JobId, Url). Returns the page id.</summary>
    Task<long> UpsertPageAsync(CrawledPage page, CancellationToken ct);

    /// <summary>Insert links keyed by (PageId, Url), ignoring ones that already exist.</summary>
    Task AddLinksAsync(IReadOnlyList<PageLink> links, CancellationToken ct);

    Task UpdateProgressAsync(Guid jobId, int pagesCrawled, int pagesQueued, CancellationToken ct);

    /// <summary>Running -> Completed. No-op if the job was canceled meanwhile.</summary>
    Task MarkCompletedAsync(Guid jobId, DateTime now, CancellationToken ct);

    /// <summary>Pending/Running -> Failed.</summary>
    Task MarkFailedAsync(Guid jobId, string reason, DateTime now, CancellationToken ct);
}

public interface IJobPublisher
{
    Task PublishAsync(CrawlJobRequested message, CancellationToken ct);
}

/// <summary>A message that can never succeed (unknown job, malformed payload): goes straight to the DLQ.</summary>
public sealed class PoisonMessageException(string message) : Exception(message);
