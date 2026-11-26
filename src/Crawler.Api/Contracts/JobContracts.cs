using System.ComponentModel.DataAnnotations;
using Crawler.Core.Domain;

namespace Crawler.Api.Contracts;

public sealed record CreateJobRequest
{
    [Required]
    [MaxLength(2048)]
    public string? Url { get; init; }

    [Range(0, CrawlJob.MaxAllowedDepth)]
    public int? MaxDepth { get; init; }
}

public sealed record CreateJobResponse(Guid JobId);

public sealed record JobSummaryDto(
    Guid JobId,
    string Url,
    int MaxDepth,
    JobStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason,
    int PagesCrawled,
    int PagesQueued);

public sealed record PageNodeDto(
    long PageId,
    string Url,
    int Depth,
    int? HttpStatusCode,
    string? Error,
    int OutgoingLinkCount,
    int InternalLinkCount,
    double DomainLinkRatio,
    IReadOnlyList<PageNodeDto> Children);

public sealed record JobTreeDto(Guid JobId, JobStatus Status, int PageCount, PageNodeDto? Root);

public sealed record PageLinkDto(string Url, bool IsInternal);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
