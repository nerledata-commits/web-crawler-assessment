using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>
/// Serves https://fixture.test/... from the local Fixtures/site folder ("/" maps to index.html).
/// Any other host (external links) returns 404. Records every requested URL.
/// </summary>
public sealed class FixtureSiteHandler : HttpMessageHandler
{
    public const string Host = "fixture.test";
    public static readonly Uri Root = new($"https://{Host}/");

    private static readonly string SiteFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "site");

    public ConcurrentBag<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Add(url.AbsoluteUri);

        var relativePath = url.AbsolutePath.TrimStart('/');
        if (relativePath.Length == 0 || relativePath.EndsWith('/'))
        {
            relativePath += "index.html";
        }

        var file = Path.Combine(SiteFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (url.Host != Host || !File.Exists(file))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }

        var content = new ByteArrayContent(File.ReadAllBytes(file));
        content.Headers.ContentType = new MediaTypeHeaderValue(
            Path.GetExtension(file) == ".html" ? "text/html" : "image/png") { CharSet = Path.GetExtension(file) == ".html" ? "utf-8" : null };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
    }
}
