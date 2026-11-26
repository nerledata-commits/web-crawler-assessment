namespace Crawler.Core.Domain;

public sealed record PageAnalysis(
    IReadOnlyList<Uri> OutgoingLinks,
    IReadOnlyList<Uri> InternalLinks,
    double DomainLinkRatio);

/// <summary>Turns the raw hrefs of a page into its de-duplicated outgoing links and metrics.</summary>
public static class PageAnalyzer
{
    public static PageAnalysis Analyze(Uri pageUri, Uri baseUri, IEnumerable<string> hrefs, string startingHost)
    {
        var selfUrl = UrlNormalizer.Normalize(pageUri.AbsoluteUri);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outgoing = new List<Uri>();

        foreach (var href in hrefs)
        {
            if (!UrlNormalizer.TryNormalize(href, baseUri, out var link))
            {
                continue; // non-http(s) scheme or malformed
            }

            // "#section" links point back to the page itself once the fragment is removed:
            // they are not navigation to another page, so they are not counted.
            if (link.AbsoluteUri == selfUrl || !seen.Add(link.AbsoluteUri))
            {
                continue;
            }

            outgoing.Add(link);
        }

        var internalLinks = outgoing.Where(l => DomainLinkRatio.IsWithinDomain(l, startingHost)).ToList();
        return new PageAnalysis(outgoing, internalLinks, DomainLinkRatio.Calculate(outgoing, startingHost));
    }
}
