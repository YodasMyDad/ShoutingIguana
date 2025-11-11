using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data;

public interface IShoutingIguanaDbContext : IDisposable
{
    DbSet<Project> Projects { get; }
    DbSet<Url> Urls { get; }
    DbSet<Link> Links { get; }
    DbSet<CrawlQueueItem> CrawlQueue { get; }
    DbSet<CrawlCheckpoint> CrawlCheckpoints { get; }
    DbSet<Header> Headers { get; }
    DbSet<Finding> Findings { get; }
    DbSet<Redirect> Redirects { get; }
    DbSet<Image> Images { get; }
    DbSet<Hreflang> Hreflangs { get; }
    DbSet<StructuredData> StructuredData { get; }
    DbSet<CustomExtractionRule> CustomExtractionRules { get; }
    DbSet<ReportSchema> ReportSchemas { get; }
    DbSet<ReportRow> ReportRows { get; }
    
    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    DbSet<T> Set<T>() where T : class;
    EntityEntry Entry(object entity);
    DatabaseFacade Database { get; }
}

