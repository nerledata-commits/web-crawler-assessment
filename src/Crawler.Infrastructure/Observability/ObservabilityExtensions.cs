using System.Text.Json;
using Crawler.Infrastructure.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crawler.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Structured logs with scopes (jobId, correlationId, messageId). JSON outside Development,
    /// single-line text in Development for readability.
    /// </summary>
    public static ILoggingBuilder AddCrawlerLogging(this ILoggingBuilder logging, IHostEnvironment environment)
    {
        logging.ClearProviders();
        if (environment.IsDevelopment())
        {
            logging.AddSimpleConsole(o =>
            {
                o.IncludeScopes = true;
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
        }
        else
        {
            logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "O";
            });
        }

        return logging;
    }

    /// <summary>GET /health returns 200/503 with a JSON body listing each check.</summary>
    public static IEndpointConventionBuilder MapCrawlerHealthChecks(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJsonAsync });

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description, error = e.Value.Exception?.Message })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var conn = await connection.GetConnectionAsync(timeout.Token);
            return conn.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Connection closed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ unreachable", ex);
        }
    }
}
