using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Data;
using ShoutingIguana.Data.Repositories;
using ShoutingIguana.PluginSdk.Helpers;
using Xunit;

namespace ShoutingIguana.Tests.Core.Services;

/// <summary>
/// Regression tests for link discovery: links found on a crawled page must reach the crawl queue.
/// The discovery worker persists a page's link graph inside the page's transaction
/// (<c>PersistLinkRelationshipsAsync</c>, which writes a Pending <see cref="Url"/> row for every link
/// target) and enqueues the links after that commits (<c>ExtractAndEnqueueNewLinksAsync</c>). Both are
/// private, so they are invoked by reflection in that same order against the real repositories and a
/// real on-disk SQLite project database. No Playwright or browser is involved.
/// </summary>
public sealed class CrawlEngineLinkEnqueueTests : IDisposable
{
    private const string BaseUrl = "https://example.com/";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sitest-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;
    private readonly CrawlEngine _engine;

    public CrawlEngineLinkEnqueueTests()
    {
        // Pooling off so each scope's connection really closes and the temp file can be deleted.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ToString();

        var services = new ServiceCollection();
        services.AddScoped<IShoutingIguanaDbContext>(_ => new SqliteShoutingIguanaDbContext(
            new DbContextOptionsBuilder<SqliteShoutingIguanaDbContext>().Options, connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUrlRepository, UrlRepository>();
        services.AddScoped<ILinkRepository, LinkRepository>();
        services.AddScoped<ICrawlQueueRepository, CrawlQueueRepository>();
        _services = services.BuildServiceProvider();

        _engine = new CrawlEngine(
            new Mock<ILogger<CrawlEngine>>().Object,
            _services,
            new Mock<IRobotsService>().Object,
            new Mock<ILinkExtractor>().Object,
            new Mock<IPlaywrightService>().Object,
            new Mock<IHttpClientFactory>().Object,
            new HostThrottle(new Mock<ILogger<HostThrottle>>().Object));
    }

    public void Dispose()
    {
        _engine.Dispose();
        _services.Dispose();

        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task LinksDiscoveredOnCrawledPageAreEnqueued()
    {
        var page = await SeedCrawledHomePageAsync();

        await DiscoverLinksAsync(page,
            Link("https://example.com/about", LinkType.Hyperlink),
            Link("https://example.com/styles/site.css", LinkType.Stylesheet),
            Link("https://example.org/partner", LinkType.Hyperlink));

        // Hyperlinks go one level deeper, page resources stay at the page's depth,
        // and external links are queued at -1 for a status check only.
        (string Address, int Depth)[] expected =
        [
            ("https://example.com/about", 1),
            ("https://example.com/styles/site.css", 0),
            ("https://example.org/partner", -1)
        ];
        Assert.Equal(expected, await GetQueuedAsync(page.ProjectId));
    }

    [Fact]
    public async Task AddressVariantsOfOneUrlAreEnqueuedOnce()
    {
        var page = await SeedCrawledHomePageAsync();

        // All three normalize to https://example.com/about (path case, tracking parameter).
        await DiscoverLinksAsync(page,
            Link("https://example.com/about", LinkType.Hyperlink),
            Link("https://example.com/About", LinkType.Hyperlink),
            Link("https://example.com/about?utm_source=nav", LinkType.Hyperlink));

        (string Address, int Depth)[] expected = [("https://example.com/about", 1)];
        Assert.Equal(expected, await GetQueuedAsync(page.ProjectId));
    }

    [Fact]
    public async Task VariantOfAlreadyCrawledUrlIsNotEnqueued()
    {
        var page = await SeedCrawledHomePageAsync();

        // Logo self-link: normalizes to the page being crawled, which is already Completed.
        await DiscoverLinksAsync(page, Link("https://example.com/?utm_source=logo", LinkType.Hyperlink));

        Assert.Empty(await GetQueuedAsync(page.ProjectId));
    }

    private static ExtractedLink Link(string url, LinkType linkType) => new() { Url = url, LinkType = linkType };

    /// <summary>
    /// Creates the project and its home page as the discovery worker leaves them just before it
    /// persists the page's links: the Url row saved as Completed and its queue item marked Completed.
    /// </summary>
    private async Task<Url> SeedCrawledHomePageAsync()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IShoutingIguanaDbContext>();
        await context.Database.MigrateAsync();

        var project = new Project
        {
            Name = "Link enqueue regression",
            BaseUrl = BaseUrl,
            CreatedUtc = DateTime.UtcNow,
            LastOpenedUtc = DateTime.UtcNow
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var homePage = new Url
        {
            ProjectId = project.Id,
            Address = BaseUrl,
            NormalizedUrl = UrlHelper.Normalize(BaseUrl),
            Scheme = "https",
            Host = "example.com",
            Path = "/",
            Depth = 0,
            FirstSeenUtc = DateTime.UtcNow,
            LastCrawledUtc = DateTime.UtcNow,
            Status = UrlStatus.Completed,
            HttpStatus = 200
        };
        context.Urls.Add(homePage);
        context.CrawlQueue.Add(new CrawlQueueItem
        {
            ProjectId = project.Id,
            Address = BaseUrl,
            Priority = 1000,
            Depth = 0,
            HostKey = "example.com",
            EnqueuedUtc = DateTime.UtcNow,
            State = QueueState.Completed
        });
        await context.SaveChangesAsync();

        return homePage;
    }

    /// <summary>
    /// Runs the discovery worker's link steps for <paramref name="page"/> in production order:
    /// persist the link graph inside the page's transaction, commit, then enqueue.
    /// </summary>
    private async Task DiscoverLinksAsync(Url page, params ExtractedLink[] links)
    {
        List<ExtractedLink> extractedLinks = [.. links];

        using (var scope = _services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync();
            await InvokePrivateAsync("PersistLinkRelationshipsAsync", scope, page.ProjectId, page, extractedLinks, page.Depth, null);
            await transaction.CommitAsync();
        }

        await InvokePrivateAsync("ExtractAndEnqueueNewLinksAsync", page.ProjectId, extractedLinks, page.Depth, BaseUrl, int.MaxValue);
    }

    private Task InvokePrivateAsync(string methodName, params object?[] arguments)
    {
        var method = typeof(CrawlEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(CrawlEngine), methodName);
        return (Task)method.Invoke(_engine, arguments)!;
    }

    private async Task<List<(string Address, int Depth)>> GetQueuedAsync(int projectId)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IShoutingIguanaDbContext>();
        var queued = await context.CrawlQueue
            .AsNoTracking()
            .Where(q => q.ProjectId == projectId && q.State == QueueState.Queued)
            .Select(q => new { q.Address, q.Depth })
            .ToListAsync();

        return queued
            .OrderBy(q => q.Address, StringComparer.Ordinal)
            .Select(q => (q.Address, q.Depth))
            .ToList();
    }
}
