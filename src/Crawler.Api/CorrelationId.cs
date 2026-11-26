namespace Crawler.Api;

/// <summary>
/// Reads X-Correlation-Id (or generates one), echoes it in the response, adds it to the log scope
/// and passes it to the worker inside the message.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";

    public static string Get(HttpContext context) =>
        context.Items[ItemKey] as string ?? context.TraceIdentifier;

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
            var id = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 100
                ? incoming
                : Guid.NewGuid().ToString("N");

            context.Items[ItemKey] = id;
            context.Response.Headers[HeaderName] = id;

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Crawler.Api.Requests");
            using (logger.BeginScope("CorrelationId:{CorrelationId}", id))
            {
                await next();
            }
        });
}
