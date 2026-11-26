namespace Crawler.Core.Domain;

/// <summary>
/// Turns raw hrefs into canonical absolute http(s) URLs so that duplicates can be detected.
/// Rules: resolve relative to the base URL, accept only http/https, lower-case scheme and host,
/// drop default ports and the fragment, keep the path and query as-is.
/// </summary>
public static class UrlNormalizer
{
    public const int MaxUrlLength = 2048;

    public static bool TryNormalize(string? href, Uri? baseUri, out Uri normalized)
    {
        normalized = null!;
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var trimmed = href.Trim();
        if (!TryResolve(trimmed, baseUri, out var resolved))
        {
            return false;
        }

        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
        {
            return false; // mailto:, tel:, javascript:, ftp:, data:, file: ...
        }

        if (string.IsNullOrEmpty(resolved.Host))
        {
            return false;
        }

        // GetLeftPart(Query) = scheme://host[:non-default-port]/path?query  (fragment removed,
        // scheme/host already lower-cased by System.Uri, empty path becomes "/").
        var canonical = resolved.GetLeftPart(UriPartial.Query);
        if (canonical.Length > MaxUrlLength)
        {
            return false;
        }

        normalized = new Uri(canonical);
        return true;
    }

    public static string? Normalize(string? href, Uri? baseUri = null) =>
        TryNormalize(href, baseUri, out var uri) ? uri.AbsoluteUri : null;

    private static bool TryResolve(string href, Uri? baseUri, out Uri resolved)
    {
        if (baseUri is not null)
        {
            return Uri.TryCreate(baseUri, href, out resolved!);
        }

        return Uri.TryCreate(href, UriKind.Absolute, out resolved!);
    }
}
