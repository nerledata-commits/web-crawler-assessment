using Crawler.Infrastructure;
using Crawler.Infrastructure.Observability;
using Crawler.Infrastructure.Persistence;
using Crawler.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddCrawlerLogging(builder.Environment);
builder.Services
    .AddCrawlerPersistence(builder.Configuration, builder.Environment.ContentRootPath)
    .AddRabbitMq(builder.Configuration)
    .AddCrawler(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq");
builder.Services.AddHostedService<CrawlJobConsumer>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

// The worker is a background consumer; it only exposes a health endpoint.
app.MapCrawlerHealthChecks();
app.MapGet("/", () => "Crawler worker. See /health.");

app.Run();
