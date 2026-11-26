using Crawler.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crawler.Core.Application;

/// <summary>
/// Runs one crawl job: breadth-first over pages on the starting host, up to MaxDepth and MaxPagesPerJob.
/// Safe to run again for the same job (at-least-once delivery): all writes are upserts keyed by
/// (JobId, Url) and (PageId, Url), and status changes are guarded by the current status.
/// </summary>
public sealed class CrawlJobProcessor(
    ICrawlStore store,
    IPageFetcher fetcher,
    ILinkExtractor linkExtractor,
    IOptions<CrawlOptions> options,
    TimeProvider clock,
    ILogger<CrawlJobProcessor> logger)
{
    private readonly CrawlOptions _options = options.Value;

    private sealed record QueuedPage(Uri Url, int Depth, long? ParentPageId);

    private sealed record PageOutcome(long PageId, IReadOnlyList<Uri> InternalLinks, string? Error);

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await store.GetJobAsync(jobId, ct)
                  ?? throw new PoisonMessageException($"Job {jobId} does not exist.");

        if (job.Status.IsTerminal())
        {
            logger.LogInformation("Job already {Status}; ignoring duplicate delivery", job.Status);
            return;
        }

        if (!await store.TryMarkRunningAsync(jobId, Now, ct))
        {
            logger.LogInformation("Job could not be moved to Running; skipping");
            return;
        }

        if (!UrlNormalizer.TryNormalize(job.Url, null, out var startUrl))
        {
            await store.MarkFailedAsync(jobId, $"Invalid start URL '{job.Url}'.", Now, ct);
            return;
        }

        logger.LogInformation("Crawl started for {StartUrl} (maxDepth {MaxDepth}, maxPages {MaxPages})",
            startUrl, job.MaxDepth, _options.MaxPagesPerJob);

        await CrawlAsync(job, startUrl, ct);
    }

    private async Task CrawlAsync(CrawlJob job, Uri startUrl, CancellationToken ct)
    {
        var startingHost = startUrl.Host;
        var seen = new HashSet<string>(StringComparer.Ordinal) { startUrl.AbsoluteUri };
        var queue = new Queue<QueuedPage>();
        queue.Enqueue(new QueuedPage(startUrl, 0, null));
        var crawled = 0;

        while (queue.TryDequeue(out var next))
        {
            ct.ThrowIfCancellationRequested();
            if (await store.GetStatusAsync(job.Id, ct) == JobStatus.Canceled)
            {
                logger.LogInformation("Job was canceled after {PagesCrawled} pages", crawled);
                return;
            }

            var outcome = await CrawlPageAsync(job.Id, next, startingHost, ct);
            crawled++;

            if (next.Depth == 0 && outcome.Error is not null)
            {
                await store.MarkFailedAsync(job.Id, $"Start page could not be crawled: {outcome.Error}", Now, ct);
                logger.LogWarning("Job failed: start page error {Error}", outcome.Error);
                return;
            }

            if (next.Depth < job.MaxDepth)
            {
                EnqueueNewLinks(outcome, next.Depth + 1, seen, queue);
            }

            await store.UpdateProgressAsync(job.Id, crawled, seen.Count, ct);
        }

        await store.MarkCompletedAsync(job.Id, Now, ct);
        logger.LogInformation("Crawl completed: {PagesCrawled} pages", crawled);
    }

    private void EnqueueNewLinks(PageOutcome outcome, int depth, HashSet<string> seen, Queue<QueuedPage> queue)
    {
        foreach (var link in outcome.InternalLinks)
        {
            if (seen.Count >= _options.MaxPagesPerJob)
            {
                logger.LogInformation("Max pages per job ({MaxPages}) reached; not scheduling more pages",
                    _options.MaxPagesPerJob);
                return;
            }

            if (seen.Add(link.AbsoluteUri))
            {
                queue.Enqueue(new QueuedPage(link, depth, outcome.PageId));
            }
        }
    }

    private async Task<PageOutcome> CrawlPageAsync(Guid jobId, QueuedPage item, string startingHost, CancellationToken ct)
    {
        var fetch = await fetcher.FetchAsync(item.Url, ct);
        var page = new CrawledPage
        {
            JobId = jobId,
            Url = item.Url.AbsoluteUri,
            ParentPageId = item.ParentPageId,
            Depth = item.Depth,
            HttpStatusCode = fetch.StatusCode,
            ContentType = fetch.ContentType,
            Error = fetch.Error,
            CrawledAt = Now
        };

        if (!fetch.IsHtml)
        {
            var failedPageId = await store.UpsertPageAsync(page, ct);
            logger.LogInformation("Page {Url} not parsed: {Error}", item.Url, fetch.Error);
            return new PageOutcome(failedPageId, [], fetch.Error ?? "Not HTML");
        }

        var analysis = Analyze(fetch, startingHost);
        page.OutgoingLinkCount = analysis.OutgoingLinks.Count;
        page.InternalLinkCount = analysis.InternalLinks.Count;
        page.DomainLinkRatio = analysis.DomainLinkRatio;
        var pageId = await store.UpsertPageAsync(page, ct);

        var links = analysis.OutgoingLinks
            .Take(_options.MaxLinksPerPage)
            .Select(link => new PageLink
            {
                JobId = jobId,
                PageId = pageId,
                Url = link.AbsoluteUri,
                IsInternal = DomainLinkRatio.IsWithinDomain(link, startingHost)
            })
            .ToList();
        await store.AddLinksAsync(links, ct);

        logger.LogInformation(
            "Crawled {Url} depth {Depth} status {StatusCode}: {LinkCount} links, ratio {DomainLinkRatio:F2}",
            item.Url, item.Depth, fetch.StatusCode, page.OutgoingLinkCount, page.DomainLinkRatio);

        return new PageOutcome(pageId, analysis.InternalLinks, null);
    }

    private PageAnalysis Analyze(FetchResult fetch, string startingHost)
    {
        var html = linkExtractor.Extract(fetch.Html!);
        var baseUri = fetch.FinalUri;
        if (html.BaseHref is not null && Uri.TryCreate(fetch.FinalUri, html.BaseHref, out var declaredBase))
        {
            baseUri = declaredBase;
        }

        return PageAnalyzer.Analyze(fetch.FinalUri, baseUri, html.Hrefs, startingHost);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
