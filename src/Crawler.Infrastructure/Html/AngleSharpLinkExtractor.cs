using AngleSharp.Html.Parser;
using Crawler.Core.Application;

namespace Crawler.Infrastructure.Html;

/// <summary>Extracts raw href values from &lt;a&gt; and &lt;area&gt; elements, plus &lt;base href&gt; if present.</summary>
public sealed class AngleSharpLinkExtractor : ILinkExtractor
{
    public HtmlLinks Extract(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        var hrefs = document
            .QuerySelectorAll("a[href], area[href]")
            .Select(element => element.GetAttribute("href"))
            .OfType<string>()
            .ToList();

        return new HtmlLinks(baseHref, hrefs);
    }
}
