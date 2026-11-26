namespace Crawler.Core.Domain;

public class CrawlJob
{
    public const int DefaultMaxDepth = 2;
    public const int MaxAllowedDepth = 5;

    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public int MaxDepth { get; set; } = DefaultMaxDepth;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Pages fetched so far (progress numerator).</summary>
    public int PagesCrawled { get; set; }

    /// <summary>Unique pages discovered and scheduled so far (progress denominator).</summary>
    public int PagesQueued { get; set; }

    public string? CorrelationId { get; set; }
}
