namespace Crawler.Core.Domain;

/// <summary>
/// Domain Link Ratio = (# outgoing links on the starting domain) / (total # outgoing links).
/// The starting domain is the host of the job's initial URL (exact, case-insensitive match).
/// A page with no outgoing links has a ratio of 0.
/// </summary>
public static class DomainLinkRatio
{
    public static double Calculate(IReadOnlyCollection<Uri> outgoingLinks, string startingHost)
    {
        if (outgoingLinks.Count == 0)
        {
            return 0;
        }

        var internalCount = outgoingLinks.Count(link => IsWithinDomain(link, startingHost));
        return (double)internalCount / outgoingLinks.Count;
    }

    public static bool IsWithinDomain(Uri link, string startingHost) =>
        string.Equals(link.Host, startingHost, StringComparison.OrdinalIgnoreCase);
}
