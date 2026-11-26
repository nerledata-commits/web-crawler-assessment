using Crawler.Api.Contracts;
using Crawler.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crawler.Api.Controllers;

[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
public sealed class JobsController(JobService jobs) : ControllerBase
{
    /// <summary>Creates a crawl job and queues it for the worker.</summary>
    [HttpPost]
    [ProducesResponseType<CreateJobResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateJobRequest request, CancellationToken ct)
    {
        var url = JobService.NormalizeStartUrl(request.Url);
        if (url is null)
        {
            ModelState.AddModelError(nameof(request.Url), "Url must be an absolute http or https URL.");
            return ValidationProblem(ModelState);
        }

        try
        {
            var jobId = await jobs.CreateAsync(url, request.MaxDepth, CorrelationId.Get(HttpContext), ct);
            return AcceptedAtAction(nameof(Get), new { jobId }, new CreateJobResponse(jobId));
        }
        catch (JobEnqueueException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Job status, timestamps and progress.</summary>
    [HttpGet("{jobId:guid}")]
    [ProducesResponseType<JobSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct) =>
        await jobs.GetAsync(jobId, ct) is { } job ? Ok(job) : NotFound();

    /// <summary>Job history, most recent first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<JobSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<JobSummaryDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        await jobs.ListAsync(page, pageSize, ct);

    /// <summary>Hierarchical tree of crawled pages with Domain Link Ratio per page.</summary>
    [HttpGet("{jobId:guid}/tree")]
    [ProducesResponseType<JobTreeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTree(Guid jobId, CancellationToken ct) =>
        await jobs.GetTreeAsync(jobId, ct) is { } tree ? Ok(tree) : NotFound();

    /// <summary>Outgoing links discovered on one page (loaded on demand; can be large).</summary>
    [HttpGet("{jobId:guid}/pages/{pageId:long}/links")]
    [ProducesResponseType<IReadOnlyList<PageLinkDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLinks(Guid jobId, long pageId, CancellationToken ct) =>
        await jobs.GetLinksAsync(jobId, pageId, ct) is { } links ? Ok(links) : NotFound();

    /// <summary>Cancels a pending or running job. The worker stops before its next page.</summary>
    [HttpPost("{jobId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken ct) =>
        await jobs.CancelAsync(jobId, ct) switch
        {
            CancelResult.Canceled => Accepted(),
            CancelResult.NotFound => NotFound(),
            _ => Problem("Job has already finished.", statusCode: StatusCodes.Status409Conflict)
        };
}
