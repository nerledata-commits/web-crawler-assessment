using Crawler.Core.Application;
using Crawler.Core.Domain;
using Crawler.Infrastructure.Html;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Persistence;
using Crawler.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Crawler.IntegrationTests;

/// <summary>
/// Runs the real crawl pipeline (HttpPageFetcher -> AngleSharp -> PageAnalyzer -> EfCrawlStore on SQLite)
/// against the local HTML fixtures in Fixtures/site.
/// </summary>
public sealed class CrawlJobProcessorTests : IDisposable
{
    private const string Site = "https://fixture.test";

    private readonly SqliteTestDatabase _database = new();
    private readonly FixtureSiteHandler _site = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Crawls_the_fixture_site_to_depth_two()
    {
        var jobId = await CreateJobAsync(maxDepth: 2);

        await ProcessAsync(jobId);

        var job = await GetJobAsync(jobId);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal(6, job.PagesCrawled);

        var pages = await GetPagesAsync(jobId);
        Assert.Equal(
            [
                $"{Site}/", $"{Site}/about.html", $"{Site}/logo.png", $"{Site}/missing.html",
                $"{Site}/products.html", $"{Site}/products/item1.html"
            ],
            pages.Keys.Order());

        // depth-3 page and external links are never fetched
        Assert.DoesNotContain(_site.Requests, r => r.Contains("deep.html") || r.Contains("example"));
        Assert.Equal(2, pages[$"{Site}/products/item1.html"].Depth);
        Assert.Equal(pages[$"{Site}/products.html"].Id, pages[$"{Site}/products/item1.html"].ParentPageId);
    }

    [Fact]
    public async Task Computes_domain_link_ratio_per_page()
    {
        var jobId = await CreateJobAsync();

        await ProcessAsync(jobId);

        var pages = await GetPagesAsync(jobId);

        // index: about, products, logo.png, missing.html internal + 1 external; mailto/tel/js/#top ignored,
        // "about.html#team" is a duplicate of "/about.html".
        var home = pages[$"{Site}/"];
        Assert.Equal(5, home.OutgoingLinkCount);
        Assert.Equal(4, home.InternalLinkCount);
        Assert.Equal(0.8, home.DomainLinkRatio, precision: 10);

        Assert.Equal(1.0 / 3, pages[$"{Site}/about.html"].DomainLinkRatio, precision: 10);
        Assert.Equal(1.0, pages[$"{Site}/products.html"].DomainLinkRatio);

        // non-HTML and broken pages are recorded with an error and ratio 0
        Assert.StartsWith("Skipped: content type 'image/png'", pages[$"{Site}/logo.png"].Error);
        Assert.Equal("HTTP 404", pages[$"{Site}/missing.html"].Error);
        Assert.Equal(0, pages[$"{Site}/missing.html"].DomainLinkRatio);

        await using var db = _database.CreateContext();
        var homeLinks = await db.PageLinks.Where(l => l.PageId == home.Id).Select(l => l.Url).ToListAsync();
        Assert.Contains("https://external.example/x", homeLinks);
        Assert.Equal(5, homeLinks.Count);
    }

    [Fact]
    public async Task Same_message_delivered_twice_does_not_duplicate_pages_or_links()
    {
        var jobId = await CreateJobAsync();
        await ProcessAsync(jobId);
        var (pagesBefore, linksBefore) = await CountRowsAsync(jobId);

        // Simulate a redelivery of a message whose first processing died before the ack:
        // the job is still Running, so the worker crawls it again.
        await using (var db = _database.CreateContext())
        {
            await db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Running));
        }

        await ProcessAsync(jobId);

        var (pagesAfter, linksAfter) = await CountRowsAsync(jobId);
        Assert.Equal(pagesBefore, pagesAfter);
        Assert.Equal(linksBefore, linksAfter);
        Assert.Equal(JobStatus.Completed, (await GetJobAsync(jobId)).Status);
    }

    [Fact]
    public async Task Redelivery_of_a_completed_job_is_ignored()
    {
        var jobId = await CreateJobAsync();
        await ProcessAsync(jobId);
        var requestsAfterFirstRun = _site.Requests.Count;

        await ProcessAsync(jobId);

        Assert.Equal(requestsAfterFirstRun, _site.Requests.Count);
    }

    [Fact]
    public async Task Stops_at_the_max_pages_limit()
    {
        var jobId = await CreateJobAsync(maxDepth: 5);

        await ProcessAsync(jobId, maxPages: 3);

        var pages = await GetPagesAsync(jobId);
        Assert.Equal(3, pages.Count);
        Assert.Equal(JobStatus.Completed, (await GetJobAsync(jobId)).Status);
    }

    [Fact]
    public async Task Max_depth_zero_crawls_only_the_start_page()
    {
        var jobId = await CreateJobAsync(maxDepth: 0);

        await ProcessAsync(jobId);

        Assert.Single(await GetPagesAsync(jobId));
    }

    [Fact]
    public async Task Fails_the_job_when_the_start_page_cannot_be_fetched()
    {
        var jobId = await CreateJobAsync(url: $"{Site}/does-not-exist.html");

        await ProcessAsync(jobId);

        var job = await GetJobAsync(jobId);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("HTTP 404", job.FailureReason);
    }

    [Fact]
    public async Task Canceled_job_is_not_crawled()
    {
        var jobId = await CreateJobAsync(status: JobStatus.Canceled);

        await ProcessAsync(jobId);

        Assert.Empty(_site.Requests);
        Assert.Equal(JobStatus.Canceled, (await GetJobAsync(jobId)).Status);
    }

    [Fact]
    public async Task Unknown_job_is_a_poison_message() =>
        await Assert.ThrowsAsync<PoisonMessageException>(() => ProcessAsync(Guid.NewGuid()));

    private async Task ProcessAsync(Guid jobId, int maxPages = 200)
    {
        await using var db = _database.CreateContext();
        var fetcher = new HttpPageFetcher(new HttpClient(_site), NullLogger<HttpPageFetcher>.Instance);
        var processor = new CrawlJobProcessor(
            new EfCrawlStore(db),
            fetcher,
            new AngleSharpLinkExtractor(),
            Options.Create(new CrawlOptions { MaxPagesPerJob = maxPages }),
            TimeProvider.System,
            NullLogger<CrawlJobProcessor>.Instance);

        await processor.ProcessAsync(jobId, CancellationToken.None);
    }

    private async Task<Guid> CreateJobAsync(int maxDepth = 2, string url = $"{Site}/", JobStatus status = JobStatus.Pending)
    {
        await using var db = _database.CreateContext();
        var job = new CrawlJob { Id = Guid.NewGuid(), Url = url, MaxDepth = maxDepth, Status = status, CreatedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<CrawlJob> GetJobAsync(Guid jobId)
    {
        await using var db = _database.CreateContext();
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private async Task<Dictionary<string, CrawledPage>> GetPagesAsync(Guid jobId)
    {
        await using var db = _database.CreateContext();
        return await db.Pages.AsNoTracking().Where(p => p.JobId == jobId).ToDictionaryAsync(p => p.Url);
    }

    private async Task<(int Pages, int Links)> CountRowsAsync(Guid jobId)
    {
        await using var db = _database.CreateContext();
        return (await db.Pages.CountAsync(p => p.JobId == jobId), await db.PageLinks.CountAsync(l => l.JobId == jobId));
    }
}
