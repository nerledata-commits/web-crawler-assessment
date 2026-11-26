namespace Crawler.Core.Domain;

/// <summary>An outgoing link (edge) from a crawled page. Unique per (PageId, Url).</summary>
public class PageLink
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public long PageId { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
}
