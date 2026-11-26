namespace Crawler.Core.Application;

public sealed class CrawlOptions
{
    public const string SectionName = "Crawl";

    /// <summary>Safety limit: maximum number of pages fetched for one job.</summary>
    public int MaxPagesPerJob { get; set; } = 200;

    /// <summary>Maximum outgoing links stored per page (protects the DB from link farms).</summary>
    public int MaxLinksPerPage { get; set; } = 1000;
}
