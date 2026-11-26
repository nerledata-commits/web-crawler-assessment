using System.Text.Json.Serialization;
using Crawler.Api;
using Crawler.Api.Services;
using Crawler.Infrastructure;
using Crawler.Infrastructure.Observability;
using Crawler.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddCrawlerLogging(builder.Environment);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddCrawlerPersistence(builder.Configuration, builder.Environment.ContentRootPath)
    .AddRabbitMq(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<JobService>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapCrawlerHealthChecks();

app.Run();

public partial class Program;
