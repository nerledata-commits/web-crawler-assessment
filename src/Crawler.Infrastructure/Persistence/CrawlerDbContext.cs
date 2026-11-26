using Crawler.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crawler.Infrastructure.Persistence;

public class CrawlerDbContext(DbContextOptions<CrawlerDbContext> options) : DbContext(options)
{
    public DbSet<CrawlJob> Jobs => Set<CrawlJob>();
    public DbSet<CrawledPage> Pages => Set<CrawledPage>();
    public DbSet<PageLink> PageLinks => Set<PageLink>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are UTC. SQLite loses DateTime.Kind, so mark values as UTC when reading.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CrawlJob>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Url).HasMaxLength(UrlNormalizer.MaxUrlLength).IsRequired();
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
            job.Property(j => j.FailureReason).HasMaxLength(2000);
            job.Property(j => j.CorrelationId).HasMaxLength(100);

            // History screen: ORDER BY CreatedAt DESC with paging.
            job.HasIndex(j => j.CreatedAt).IsDescending();
        });

        modelBuilder.Entity<CrawledPage>(page =>
        {
            page.ToTable("Pages");
            page.HasKey(p => p.Id);
            page.Property(p => p.Url).HasMaxLength(UrlNormalizer.MaxUrlLength).IsRequired();
            page.Property(p => p.ContentType).HasMaxLength(200);
            page.Property(p => p.Error).HasMaxLength(2000);

            // Idempotency key: a URL is stored once per job. Also serves "all pages of a job" (tree).
            page.HasIndex(p => new { p.JobId, p.Url }).IsUnique();

            page.HasOne<CrawlJob>().WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
            page.HasOne<CrawledPage>().WithMany().HasForeignKey(p => p.ParentPageId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PageLink>(link =>
        {
            link.ToTable("PageLinks");
            link.HasKey(l => l.Id);
            link.Property(l => l.Url).HasMaxLength(UrlNormalizer.MaxUrlLength).IsRequired();

            // Idempotency key: an edge is stored once per source page. Also serves "links of a page".
            link.HasIndex(l => new { l.PageId, l.Url }).IsUnique();

            link.HasOne<CrawledPage>().WithMany().HasForeignKey(l => l.PageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
