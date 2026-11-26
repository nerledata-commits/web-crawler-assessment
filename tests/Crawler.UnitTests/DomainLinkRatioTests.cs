using Crawler.Core.Domain;

namespace Crawler.UnitTests;

public class DomainLinkRatioTests
{
    private const string Host = "example.com";

    private static Uri[] Links(params string[] urls) => urls.Select(u => new Uri(u)).ToArray();

    [Fact]
    public void Zero_links_gives_zero() =>
        Assert.Equal(0, DomainLinkRatio.Calculate([], Host));

    [Fact]
    public void All_internal_links_gives_one() =>
        Assert.Equal(1, DomainLinkRatio.Calculate(Links("https://example.com/a", "http://example.com/b"), Host));

    [Fact]
    public void All_external_links_gives_zero() =>
        Assert.Equal(0, DomainLinkRatio.Calculate(Links("https://other.com/a"), Host));

    [Fact]
    public void Mixed_links_gives_internal_over_total() =>
        Assert.Equal(0.75, DomainLinkRatio.Calculate(
            Links("https://example.com/a", "https://example.com/b", "https://example.com/c", "https://other.com/"), Host));

    [Fact]
    public void Subdomains_are_not_the_starting_domain() =>
        Assert.Equal(0.5, DomainLinkRatio.Calculate(Links("https://example.com/a", "https://www.example.com/a"), Host));

    [Fact]
    public void Host_comparison_is_case_insensitive() =>
        Assert.True(DomainLinkRatio.IsWithinDomain(new Uri("https://example.com/"), "EXAMPLE.com"));
}
