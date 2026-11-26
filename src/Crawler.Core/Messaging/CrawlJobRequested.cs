namespace Crawler.Core.Messaging;

/// <summary>
/// Message published by the API when a job is created and consumed by the worker.
/// Contract version 1. The worker only trusts JobId; job details are re-read from the database.
/// </summary>
public sealed record CrawlJobRequested(
    Guid MessageId,
    Guid JobId,
    string Url,
    int MaxDepth,
    string? CorrelationId,
    DateTime RequestedAt)
{
    public const int SchemaVersion = 1;
}
