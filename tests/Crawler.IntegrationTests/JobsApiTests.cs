using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crawler.Api.Contracts;
using Crawler.Core.Application;
using Crawler.Core.Domain;
using Crawler.Core.Messaging;
using Crawler.Infrastructure.Persistence;
using Crawler.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Crawler.IntegrationTests;

/// <summary>API tests with SQLite in memory and a fake publisher (no RabbitMQ needed).</summary>
public sealed class JobsApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SqliteTestDatabase _database = new();
    private readonly FakePublisher _publisher = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public JobsApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CrawlerDbContext>>();
                services.AddDbContext<CrawlerDbContext>(o => o.UseSqlite(_database.Connection));
                services.RemoveAll<IJobPublisher>();
                services.AddSingleton<IJobPublisher>(_publisher);
            });
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Create_returns_job_id_publishes_message_and_job_is_pending()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new { url = "HTTPS://Fixture.Test/start#x" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateJobResponse>(Json);
        Assert.NotNull(created);

        var message = Assert.Single(_publisher.Messages);
        Assert.Equal(created.JobId, message.JobId);
        Assert.Equal(CrawlJob.DefaultMaxDepth, message.MaxDepth);

        var job = await _client.GetFromJsonAsync<JobSummaryDto>($"/api/jobs/{created.JobId}", Json);
        Assert.Equal(JobStatus.Pending, job!.Status);
        Assert.Equal("https://fixture.test/start", job.Url);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Theory]
    [InlineData("""{ "url": "not a url" }""")]
    [InlineData("""{ "url": "ftp://example.com" }""")]
    [InlineData("""{ "url": "/relative" }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "url": "https://example.com", "maxDepth": 99 }""")]
    [InlineData("""{ "url": "https://example.com", "maxDepth": -1 }""")]
    public async Task Create_rejects_invalid_input(string body)
    {
        var response = await _client.PostAsync("/api/jobs", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_publisher.Messages);
    }

    [Fact]
    public async Task Create_returns_503_and_fails_job_when_broker_is_down()
    {
        _publisher.FailNext = true;

        var response = await _client.PostAsJsonAsync("/api/jobs", new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var db = _database.CreateContext();
        Assert.Equal(JobStatus.Failed, (await db.Jobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task History_is_paginated_most_recent_first()
    {
        await using (var db = _database.CreateContext())
        {
            for (var i = 0; i < 5; i++)
            {
                db.Jobs.Add(new CrawlJob { Id = Guid.NewGuid(), Url = $"https://example.com/{i}", CreatedAt = DateTime.UtcNow.AddMinutes(i) });
            }

            await db.SaveChangesAsync();
        }

        var page1 = await _client.GetFromJsonAsync<PagedResult<JobSummaryDto>>("/api/jobs?page=1&pageSize=2", Json);
        var page3 = await _client.GetFromJsonAsync<PagedResult<JobSummaryDto>>("/api/jobs?page=3&pageSize=2", Json);

        Assert.Equal(5, page1!.TotalCount);
        Assert.Equal(["https://example.com/4", "https://example.com/3"], page1.Items.Select(j => j.Url));
        Assert.Equal(["https://example.com/0"], page3!.Items.Select(j => j.Url));
    }

    [Fact]
    public async Task Tree_returns_pages_nested_by_parent()
    {
        var jobId = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Jobs.Add(new CrawlJob { Id = jobId, Url = "https://example.com/", Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow });
            var root = new CrawledPage { JobId = jobId, Url = "https://example.com/", DomainLinkRatio = 0.5 };
            db.Pages.Add(root);
            await db.SaveChangesAsync();
            db.Pages.Add(new CrawledPage { JobId = jobId, Url = "https://example.com/a", ParentPageId = root.Id, Depth = 1 });
            await db.SaveChangesAsync();
        }

        var tree = await _client.GetFromJsonAsync<JobTreeDto>($"/api/jobs/{jobId}/tree", Json);

        Assert.Equal(2, tree!.PageCount);
        Assert.Equal(0.5, tree.Root!.DomainLinkRatio);
        Assert.Equal("https://example.com/a", Assert.Single(tree.Root.Children).Url);
    }

    [Fact]
    public async Task Cancel_moves_pending_job_to_canceled_and_rejects_finished_jobs()
    {
        var created = await (await _client.PostAsJsonAsync("/api/jobs", new { url = "https://example.com" }))
            .Content.ReadFromJsonAsync<CreateJobResponse>(Json);

        var first = await _client.PostAsync($"/api/jobs/{created!.JobId}/cancel", null);
        var second = await _client.PostAsync($"/api/jobs/{created.JobId}/cancel", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Unknown_job_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/jobs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/jobs/{Guid.NewGuid()}/tree")).StatusCode);
    }

    private sealed class FakePublisher : IJobPublisher
    {
        public ConcurrentQueue<CrawlJobRequested> Messages { get; } = new();
        public bool FailNext { get; set; }

        public Task PublishAsync(CrawlJobRequested message, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Broker down");
            }

            Messages.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
