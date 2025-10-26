using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Data;

public abstract class ShoutingIguanaDbContextBase : DbContext
{
    protected ShoutingIguanaDbContextBase(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Url> Urls => Set<Url>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<CrawlQueueItem> CrawlQueue => Set<CrawlQueueItem>();
    public DbSet<CrawlCheckpoint> CrawlCheckpoints => Set<CrawlCheckpoint>();
    public DbSet<Header> Headers => Set<Header>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Redirect> Redirects => Set<Redirect>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Hreflang> Hreflangs => Set<Hreflang>();
    public DbSet<StructuredData> StructuredData => Set<StructuredData>();
    public DbSet<CustomExtractionRule> CustomExtractionRules => Set<CustomExtractionRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
    
    public new DatabaseFacade Database => base.Database;
}

