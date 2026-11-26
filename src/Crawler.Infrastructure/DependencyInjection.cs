using Crawler.Core.Application;
using Crawler.Infrastructure.Html;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Messaging;
using Crawler.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Crawler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Database:Provider = "Postgres" (docker-compose) or "Sqlite" (local runs without Docker, tests).
    /// ConnectionStrings:Crawler holds the connection string. Relative SQLite paths resolve against contentRoot.
    /// </summary>
    public static IServiceCollection AddCrawlerPersistence(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connectionString = config.GetConnectionString("Crawler") ?? "Data Source=crawler.db";

        services.AddDbContext<CrawlerDbContext>(options =>
        {
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));
            }
            else
            {
                options.UseSqlite(ResolveSqlitePath(connectionString, contentRoot));
            }
        });

        services.AddHealthChecks().AddDbContextCheck<CrawlerDbContext>("database");
        return services;
    }

    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IJobPublisher, RabbitMqJobPublisher>();
        return services;
    }

    /// <summary>Crawler services: fetcher with timeouts + retries, link extractor, store, processor.</summary>
    public static IServiceCollection AddCrawler(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CrawlOptions>(config.GetSection(CrawlOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILinkExtractor, AngleSharpLinkExtractor>();
        services.AddScoped<ICrawlStore, EfCrawlStore>();
        services.AddScoped<CrawlJobProcessor>();

        services.AddHttpClient<IPageFetcher, HttpPageFetcher>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CrawlerAssessmentBot/1.0");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            })
            .AddStandardResilienceHandler(options =>
            {
                // Retries: 5xx, 408, 429, HttpRequestException and per-attempt timeouts.
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(35);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        return services;
    }

    private static string ResolveSqlitePath(string connectionString, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
            Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        }

        return builder.ToString();
    }
}
