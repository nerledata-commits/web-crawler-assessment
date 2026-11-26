namespace Crawler.Core.Domain;

/// <summary>A page visited by a job. Unique per (JobId, Url) where Url is normalized.</summary>
public class CrawledPage
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>The page through which this page was first discovered (tree parent). Null for the root.</summary>
    public long? ParentPageId { get; set; }

    public int Depth { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ContentType { get; set; }
    public int OutgoingLinkCount { get; set; }
    public int InternalLinkCount { get; set; }
    public double DomainLinkRatio { get; set; }

    /// <summary>Why the page could not be parsed (network error, non-HTML, HTTP error). Null on success.</summary>
    public string? Error { get; set; }

    public DateTime CrawledAt { get; set; }
}
