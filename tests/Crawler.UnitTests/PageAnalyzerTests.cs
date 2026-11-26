using Crawler.Core.Domain;

namespace Crawler.UnitTests;

public class PageAnalyzerTests
{
    private static readonly Uri Page = new("https://example.com/blog/");

    private static PageAnalysis Analyze(params string[] hrefs) =>
        PageAnalyzer.Analyze(Page, Page, hrefs, "example.com");

    [Fact]
    public void Duplicate_links_after_normalization_are_counted_once()
    {
        var result = Analyze("/a", "/a#x", "https://EXAMPLE.com/a", "a2");

        Assert.Equal(["https://example.com/a", "https://example.com/blog/a2"],
            result.OutgoingLinks.Select(l => l.AbsoluteUri));
    }

    [Fact]
    public void Non_http_links_are_ignored_in_the_ratio()
    {
        var result = Analyze("/a", "https://other.com/", "mailto:x@example.com", "tel:1", "javascript:void(0)");

        Assert.Equal(2, result.OutgoingLinks.Count);
        Assert.Equal(0.5, result.DomainLinkRatio);
    }

    [Fact]
    public void Fragment_only_links_to_the_same_page_are_not_outgoing_links()
    {
        var result = Analyze("#top", "#comments", "/blog/");

        Assert.Empty(result.OutgoingLinks);
        Assert.Equal(0, result.DomainLinkRatio);
    }

    [Fact]
    public void Internal_links_are_the_ones_on_the_starting_host()
    {
        var result = Analyze("/a", "https://sub.example.com/b", "https://other.com/c");

        Assert.Equal(["https://example.com/a"], result.InternalLinks.Select(l => l.AbsoluteUri));
        Assert.Equal(1.0 / 3, result.DomainLinkRatio, precision: 10);
    }
}
