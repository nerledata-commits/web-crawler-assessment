using Crawler.Infrastructure.Html;

namespace Crawler.UnitTests;

public class AngleSharpLinkExtractorTests
{
    private readonly AngleSharpLinkExtractor _extractor = new();

    [Fact]
    public void Extracts_hrefs_from_anchor_and_area_elements()
    {
        const string html = """
            <html><body>
              <a href="/one">One</a>
              <a>no href</a>
              <map><area href="/two" /></map>
              <link href="/style.css" rel="stylesheet" />
              <A HREF="/three">Three</A>
            </body></html>
            """;

        var result = _extractor.Extract(html);

        Assert.Equal(["/one", "/two", "/three"], result.Hrefs);
        Assert.Null(result.BaseHref);
    }

    [Fact]
    public void Reads_base_href()
    {
        var result = _extractor.Extract("""<html><head><base href="https://cdn.example.com/root/"></head><body></body></html>""");

        Assert.Equal("https://cdn.example.com/root/", result.BaseHref);
    }

    [Fact]
    public void Tolerates_malformed_html()
    {
        var result = _extractor.Extract("<div><a href='/ok'>unclosed <p><a href=\"/also-ok\">");

        // HTML5 parsing may re-open the unclosed <a> (as browsers do); duplicates are removed later by PageAnalyzer.
        Assert.Equal(["/also-ok", "/ok"], result.Hrefs.Distinct().Order());
    }
}
