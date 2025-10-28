using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace MyCompany.ShoutingIguana.Plugins;

/// <summary>
/// Advanced plugin template showing:
/// - Repository access for cross-page analysis
/// - Static state with proper cleanup
/// - Complex finding structures
/// - URL helper usage
/// - Memory management
/// </summary>
[Plugin(Id = "com.mycompany.advanced", Name = "Advanced SEO Plugin", MinSdkVersion = "1.0.0")]
public class AdvancedPlugin : IPlugin
{
    public string Id => "com.mycompany.advanced";
    public string Name => "Advanced SEO Plugin";
    public Version Version => new(1, 0, 0);
    public string Description => "Advanced SEO analysis with cross-page checking and duplicate detection";

    public void Initialize(IHostContext context)
    {
        var logger = context.CreateLogger<AdvancedTask>();
        var accessor = context.GetRepositoryAccessor();
        
        context.RegisterTask(new AdvancedTask(logger, accessor));
        
        // Optionally register custom exporter for specialized formats
        // context.RegisterExport(new MyCustomExporter(logger, context.GetServiceProvider()));
    }
}

/// <summary>
/// Advanced task demonstrating repository access and state management.
/// </summary>
public class AdvancedTask : UrlTaskBase
{
    private readonly ILogger _logger;
    private readonly IRepositoryAccessor _accessor;
    
    // Static dictionaries for cross-page analysis (keyed by ProjectId)
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> _titlesByProject = new();
    private static readonly ConcurrentDictionary<int, HashSet<string>> _processedUrls = new();

    public AdvancedTask(ILogger logger, IRepositoryAccessor accessor)
    {
        _logger = logger;
        _accessor = accessor;
    }

    public override string Key => "AdvancedCheck";
    public override string DisplayName => "Advanced SEO Check";
    public override string Description => "Advanced analysis with cross-page duplicate detection";
    public override int Priority => 100;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Skip non-HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        // Skip error pages
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Avoid processing the same URL twice
        var processedUrls = _processedUrls.GetOrAdd(ctx.Project.ProjectId, _ => new HashSet<string>());
        var normalizedUrl = UrlHelper.Normalize(ctx.Url.ToString());
        
        lock (processedUrls)
        {
            if (!processedUrls.Add(normalizedUrl))
            {
                _logger.LogDebug("Already processed {Url}, skipping", ctx.Url);
                return;
            }
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode?.InnerText?.Trim() ?? "";

            if (!string.IsNullOrEmpty(title))
            {
                // Track for duplicate detection
                await CheckDuplicateTitles(ctx, title);
            }

            // Check canonical URLs using repository accessor
            await ValidateCanonicalUrl(ctx, doc);

            // Check external links
            await AnalyzeExternalLinks(ctx, doc);

            // Add more advanced checks...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in advanced analysis for {Url}", ctx.Url);
        }
    }

    /// <summary>
    /// Tracks titles across the site and reports duplicates.
    /// </summary>
    private async Task CheckDuplicateTitles(UrlContext ctx, string title)
    {
        var projectTitles = _titlesByProject.GetOrAdd(
            ctx.Project.ProjectId,
            _ => new ConcurrentDictionary<string, List<string>>());

        var urls = projectTitles.GetOrAdd(title, _ => new List<string>());
        
        lock (urls)
        {
            urls.Add(ctx.Url.ToString());
            
            // Only report if we have duplicates
            if (urls.Count > 1)
            {
                var otherUrls = urls.Where(u => u != ctx.Url.ToString()).Take(5).ToList();
                
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Title: \"{title}\"")
                    .AddItem($"Found on {urls.Count} pages");
                
                builder.BeginNested("📄 Other pages with same title");
                foreach (var url in otherUrls)
                {
                    builder.AddItem(url);
                }
                if (urls.Count > 6)
                {
                    builder.AddItem($"... and {urls.Count - 6} more");
                }
                builder.EndNested();
                
                builder.BeginNested("📉 SEO Impact")
                    .AddItem("Duplicate titles confuse search engines")
                    .AddItem("Makes it harder for users to distinguish pages in search results")
                    .AddItem("Can reduce click-through rates")
                .EndNested();
                
                builder.BeginNested("💡 Recommendations")
                    .AddItem("Make each title unique and descriptive")
                    .AddItem("Include page-specific keywords")
                    .AddItem("Maintain consistent brand naming")
                .EndNested();
                
                builder.WithTechnicalMetadata("title", title)
                    .WithTechnicalMetadata("duplicateCount", urls.Count)
                    .WithTechnicalMetadata("pageDepth", ctx.Metadata.Depth);
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "DUPLICATE_TITLE",
                    $"Title \"{title}\" appears on multiple pages",
                    builder.Build());
            }
        }
    }

    /// <summary>
    /// Validates that canonical URLs point to crawled pages.
    /// Demonstrates repository accessor usage.
    /// </summary>
    private async Task ValidateCanonicalUrl(UrlContext ctx, HtmlDocument doc)
    {
        var canonicalNode = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
        if (canonicalNode == null)
        {
            return;
        }

        var canonical = canonicalNode.GetAttributeValue("href", "");
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        // Resolve to absolute URL
        var canonicalAbsolute = UrlHelper.Resolve(ctx.Url, canonical);
        var normalizedCanonical = UrlHelper.Normalize(canonicalAbsolute);
        var normalizedCurrent = UrlHelper.Normalize(ctx.Url.ToString());

        // Check if canonical points to itself
        if (normalizedCanonical == normalizedCurrent)
        {
            return; // Self-referential canonical is correct
        }

        // Check if canonical target exists in crawl using repository accessor
        var targetUrl = await _accessor.GetUrlByAddressAsync(
            ctx.Project.ProjectId,
            normalizedCanonical);

        if (targetUrl == null)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Current page: {ctx.Url}")
                .AddItem($"Canonical URL: {canonicalAbsolute}")
                .AddItem("⚠️ Canonical target was not found in crawl")
                .BeginNested("💡 Recommendation")
                    .AddItem("Verify the canonical URL is correct")
                    .AddItem("Ensure the target page is accessible")
                    .AddItem("Check if the target is being crawled")
                .EndNested()
                .WithTechnicalMetadata("canonicalUrl", canonicalAbsolute)
                .WithTechnicalMetadata("currentUrl", ctx.Url.ToString())
                .Build();

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CANONICAL_NOT_FOUND",
                $"Canonical points to uncrawled page: {canonicalAbsolute}",
                details);
        }
        else if (targetUrl.Status >= 400)
        {
            var details = FindingDetailsBuilder.Simple(
                $"Current page: {ctx.Url}",
                $"Canonical URL: {canonicalAbsolute}",
                $"❌ Target returns HTTP {targetUrl.Status}",
                "Canonical should point to a valid, accessible page"
            );

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "CANONICAL_ERROR_PAGE",
                $"Canonical points to error page (HTTP {targetUrl.Status})",
                details);
        }
        else if (!targetUrl.IsIndexable)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CANONICAL_NOT_INDEXABLE",
                "Canonical points to non-indexable page (noindex or blocked)",
                FindingDetailsBuilder.Simple(
                    $"Current page: {ctx.Url}",
                    $"Canonical URL: {canonicalAbsolute}",
                    "Target page has noindex or is blocked by robots"
                ));
        }
    }

    /// <summary>
    /// Analyzes external links using URL helper.
    /// </summary>
    private async Task AnalyzeExternalLinks(UrlContext ctx, HtmlDocument doc)
    {
        var externalLinks = new List<string>();
        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        
        if (linkNodes == null)
        {
            return;
        }

        foreach (var linkNode in linkNodes)
        {
            var href = linkNode.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;
            
            // Skip non-HTTP links
            if (href.StartsWith("javascript:") || href.StartsWith("mailto:") || href.StartsWith("tel:"))
            {
                continue;
            }

            // Resolve to absolute URL
            var absoluteUrl = UrlHelper.Resolve(ctx.Url, href);
            
            // Check if external using UrlHelper
            if (UrlHelper.IsExternal(ctx.Project.BaseUrl, absoluteUrl))
            {
                externalLinks.Add(absoluteUrl);
            }
        }

        // Report if page has many external links
        if (externalLinks.Count > 50)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Page: {ctx.Url}")
                .AddItem($"External links: {externalLinks.Count}")
                .BeginNested("⚠️ Issue")
                    .AddItem("Excessive external links may look spammy")
                    .AddItem("May pass too much link equity to other sites")
                .EndNested()
                .BeginNested("💡 Recommendation")
                    .AddItem("Reduce external links to essential references")
                    .AddItem("Use nofollow on less important external links")
                .EndNested()
                .WithTechnicalMetadata("externalLinkCount", externalLinks.Count)
                .WithTechnicalMetadata("externalDomains", externalLinks
                    .Select(l => UrlHelper.GetDomain(l))
                    .Where(d => d != null)
                    .Distinct()
                    .ToArray())
                .Build();

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "EXCESSIVE_EXTERNAL_LINKS",
                $"Page has {externalLinks.Count} external links",
                details);
        }
    }

    /// <summary>
    /// CRITICAL: Clean up static state when project closes to prevent memory leaks.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        _titlesByProject.TryRemove(projectId, out _);
        _processedUrls.TryRemove(projectId, out _);
        
        _logger.LogDebug("Cleaned up state for project {ProjectId}", projectId);
    }
}

/// <summary>
/// Optional: Custom export provider for specialized formats.
/// Most plugins don't need this - findings export to CSV/Excel/PDF automatically.
/// </summary>
public class MyCustomExporter : IExportProvider
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Key => "MyPluginJson";
    public string DisplayName => "My Plugin Data (JSON)";
    public string FileExtension => ".json";

    public MyCustomExporter(ILogger logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<ExportResult> ExportAsync(ExportContext ctx, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Exporting custom JSON for project {ProjectId}", ctx.ProjectId);

            // Your custom export logic here
            // Query data, serialize to JSON, write to ctx.FilePath
            
            var data = new
            {
                ProjectId = ctx.ProjectId,
                ExportDate = DateTime.UtcNow,
                // Add your custom data...
            };

            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(ctx.FilePath, json, ct);

            return new ExportResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting custom JSON");
            return new ExportResult(false, ex.Message);
        }
    }
}

