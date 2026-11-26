using System.Text;
using Crawler.Core.Application;
using Microsoft.Extensions.Logging;

namespace Crawler.Infrastructure.Http;

/// <summary>
/// Fetches a page over HTTP. Timeouts and retries come from the resilience pipeline configured in DI.
/// Network/HTTP problems are returned as <see cref="FetchResult.Error"/> so one bad page never fails the job.
/// </summary>
public sealed class HttpPageFetcher(HttpClient http, ILogger<HttpPageFetcher> logger) : IPageFetcher
{
    public const int MaxHtmlBytes = 2 * 1024 * 1024;

    private static readonly string[] HtmlMediaTypes = ["text/html", "application/xhtml+xml"];

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var finalUri = response.RequestMessage?.RequestUri ?? url;
            var statusCode = (int)response.StatusCode;
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (!response.IsSuccessStatusCode)
            {
                return new FetchResult(finalUri, statusCode, mediaType, null, $"HTTP {statusCode}");
            }

            if (mediaType is null || !HtmlMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            {
                return new FetchResult(finalUri, statusCode, mediaType, null,
                    $"Skipped: content type '{mediaType ?? "unknown"}' is not HTML");
            }

            var html = await ReadLimitedAsync(response.Content, cancellationToken);
            return new FetchResult(finalUri, statusCode, mediaType, html, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // HttpRequestException (DNS, connection refused, TLS), Polly timeout, broken circuit...
            logger.LogWarning("Fetching {Url} failed after retries: {Error}", url, ex.Message);
            return new FetchResult(url, null, null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            var allowed = (int)Math.Min(read, MaxHtmlBytes - buffer.Length);
            buffer.Write(chunk, 0, allowed);
            if (buffer.Length >= MaxHtmlBytes)
            {
                break; // very large page: parse only the first MaxHtmlBytes
            }
        }

        return GetEncoding(content.Headers.ContentType?.CharSet).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
