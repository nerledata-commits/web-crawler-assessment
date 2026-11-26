using Crawler.Core.Domain;

namespace Crawler.UnitTests;

public class UrlNormalizerTests
{
    private static readonly Uri Base = new("https://example.com/docs/guide/index.html");

    [Theory]
    [InlineData("/about", "https://example.com/about")]
    [InlineData("intro.html", "https://example.com/docs/guide/intro.html")]
    [InlineData("../api/", "https://example.com/docs/api/")]
    [InlineData("./a/b?x=1", "https://example.com/docs/guide/a/b?x=1")]
    [InlineData("//cdn.example.org/lib.js", "https://cdn.example.org/lib.js")]
    [InlineData("?page=2", "https://example.com/docs/guide/index.html?page=2")]
    public void Resolves_relative_links_against_the_base_url(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Base));

    [Theory]
    [InlineData("#section", "https://example.com/docs/guide/index.html")]
    [InlineData("/about#team", "https://example.com/about")]
    [InlineData("https://example.com/a?q=1#frag", "https://example.com/a?q=1")]
    public void Removes_the_fragment(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Base));

    [Fact]
    public void Links_differing_only_by_fragment_normalize_to_the_same_url() =>
        Assert.Equal(UrlNormalizer.Normalize("/page#a", Base), UrlNormalizer.Normalize("/page#b", Base));

    [Theory]
    [InlineData("HTTPS://Example.COM/Path", "https://example.com/Path")]
    [InlineData("http://example.com:80/x", "http://example.com/x")]
    [InlineData("https://example.com:443/x", "https://example.com/x")]
    [InlineData("https://example.com:8443/x", "https://example.com:8443/x")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("  https://example.com/x  ", "https://example.com/x")]
    public void Canonicalizes_scheme_host_port_and_whitespace(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Base));

    [Theory]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+123456")]
    [InlineData("javascript:void(0)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("data:text/html,hello")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_non_http_schemes_and_empty_values(string? href) =>
        Assert.Null(UrlNormalizer.Normalize(href, Base));

    [Fact]
    public void Rejects_relative_url_without_a_base() =>
        Assert.Null(UrlNormalizer.Normalize("/about"));

    [Fact]
    public void Rejects_urls_longer_than_the_limit() =>
        Assert.Null(UrlNormalizer.Normalize("https://example.com/" + new string('a', UrlNormalizer.MaxUrlLength)));
}
