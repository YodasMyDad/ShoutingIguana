# Shouting Iguana Plugin SDK

**Build powerful SEO analysis plugins with minimal code.** Write your logic, publish to NuGet, and instantly make it available to every Shouting Iguana user. No complex integrations, no steep learning curves—just clean, intuitive APIs that get out of your way.

## Quick Start

### Installation

```bash
dotnet add package ShoutingIguana.PluginSdk
```

### Your First Plugin (5 minutes)

```csharp
using ShoutingIguana.PluginSdk;

namespace MyCompany.Plugins;

[Plugin(Id = "com.mycompany.myplugin", Name = "My SEO Plugin")]
public class MyPlugin : IPlugin
{
    public string Id => "com.mycompany.myplugin";
    public string Name => "My SEO Plugin";
    public Version Version => new(1, 0, 0);
    public string Description => "Checks for common SEO issues";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema
        var schema = ReportSchema.Create("MySEOCheck")
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("Title", ReportColumnType.String, "Title")
            .AddColumn("Length", ReportColumnType.Integer, "Length")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        var logger = context.CreateLogger<MyTask>();
        context.RegisterTask(new MyTask(logger));
    }
}

public class MyTask : UrlTaskBase
{
    private readonly ILogger _logger;

    public MyTask(ILogger logger) => _logger = logger;
    
    public override string Key => "MySEOCheck";
    public override string DisplayName => "My SEO Check";
    public override string Description => "Custom SEO analysis";

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
            return;

        // Example: Check for short titles
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "";

        if (title.Length < 30)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Title Too Short")
                .Set("Title", title)
                .Set("Length", title.Length);

            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }
}
```

**That's it!** Package it, publish to NuGet, and your findings automatically appear in every user's exports.

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/YourPlugin.1.0.0.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json
```

## Built-in Plugins

Shouting Iguana ships with professional-grade plugins:

- **Broken Links** - Detects 404s and unreachable resources
- **Canonical Tags** - Validates canonical URL implementation
- **Custom Extraction** - Extract custom data using CSS selectors
- **Duplicate Content** - Identifies pages with identical content
- **Image Audit** - Analyzes image attributes, alt text, and size
- **Internal Linking** - Maps internal link structure and anchor text
- **Inventory** - Catalogs all discovered URLs and page types
- **Link Graph** - Generates visual site architecture maps
- **List Mode** - Import and analyze specific URL lists
- **Redirects** - Traces redirect chains and identifies issues
- **Robots.txt** - Validates robots.txt directives and blocked URLs
- **Sitemap** - Analyzes XML sitemaps and validates entries
- **Structured Data** - Validates JSON-LD and microdata markup
- **Titles & Meta** - Audits title tags, meta descriptions, and length

## Core Concepts

### Plugin Lifecycle

```
Install from NuGet → Initialize() → Register Tasks → Crawl → ExecuteAsync() per URL → Export
```

### Key Interfaces

- **IPlugin** - Entry point for your plugin
- **UrlTaskBase** - Analysis logic that runs for each crawled URL
- **ReportSchema** - Define custom datagrid columns with typed data
- **ReportRow** - Populate custom columns with data
- **IRepositoryAccessor** - Query crawled data efficiently
- **IHostContext** - Access logging and task registration

## Custom Report Schemas (Advanced Datagrid)

Plugins can define custom column layouts for the datagrid view, providing a specialized, scannable interface for findings. This is optional—findings work without custom schemas, but schemas dramatically improve the user experience for tabular data.

### When to Use Custom Schemas

- **Use schemas for**: Tabular data with consistent fields (broken links, internal linking, redirects)
- **Skip schemas for**: One-off issues or highly variable findings

### Registering a Schema

Define columns in your plugin's `Initialize()` method:

```csharp
public void Initialize(IHostContext context)
{
    // Register custom report schema with specialized columns
    var schema = ReportSchema.Create("LinkGraph")
        .AddPrimaryColumn("FromURL", ReportColumnType.Url, "From URL")
        .AddColumn("ToURL", ReportColumnType.Url, "To URL")
        .AddColumn("AnchorText", ReportColumnType.String, "Anchor Text")
        .AddColumn("LinkType", ReportColumnType.String, "Link Type")
        .Build();
    
    context.RegisterReportSchema(schema);
    
    // Register your task as usual
    context.RegisterTask(new LinkGraphTask(context.CreateLogger<LinkGraphTask>()));
}
```

### Creating Report Rows

In your task, create rows that match your schema:

```csharp
public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
{
    // Create report row with custom columns
    // IMPORTANT: Plugins with registered schemas should create ONLY report rows, not findings
    // Do NOT call ctx.Findings.ReportAsync() - this causes UI to show legacy columns
    var row = ReportRow.Create()
        .Set("FromURL", ctx.Url.ToString())
        .Set("ToURL", targetUrl)
        .Set("AnchorText", anchorText)
        .Set("LinkType", linkType);
    
    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
}
```

**IMPORTANT:** If you register a custom schema, create **ONLY** report rows using `ctx.Reports.ReportAsync()`. Do NOT also call `ctx.Findings.ReportAsync()`, as this creates duplicate data and causes the UI to display legacy columns instead of your custom columns.

### Column Types

- **String** - Text values (anchor text, descriptions)
- **Integer** - Whole numbers (counts, depths)
- **Decimal** - Numbers with decimals (percentages, ratios)
- **DateTime** - Timestamps (last modified dates)
- **Boolean** - True/false (rendered as checkboxes)
- **Url** - Hyperlinks (clickable, monospace font)

### Column Configuration

```csharp
var schema = ReportSchema.Create("MyPlugin")
    .AddPrimaryColumn("URL", ReportColumnType.Url, "Page URL")  // Primary column (bold, shown first)
    .AddColumn("Count", ReportColumnType.Integer, "Issue Count")
    .AddColumn("LastChecked", ReportColumnType.DateTime, "Last Checked")
    .Build();
```

Primary columns are shown first and typically bold—use for the main identifier (URL, page title, etc.).

## Reporting Issues

All plugins must use **report schemas** to display data with custom columns. There is no legacy "findings" system - everything uses typed report rows.

## Accessing Crawled Data

Query URLs and redirects without reflection:

```csharp
public class CanonicalTask : UrlTaskBase
{
    private readonly IRepositoryAccessor _accessor;

    public CanonicalTask(ILogger logger, IRepositoryAccessor accessor)
    {
        _logger = logger;
        _accessor = accessor;
    }

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        var canonical = GetCanonicalFromHtml(ctx.RenderedHtml);
        
        if (!string.IsNullOrEmpty(canonical))
        {
            // Check if canonical target was crawled
            var targetUrl = await _accessor.GetUrlByAddressAsync(
                ctx.Project.ProjectId, 
                canonical);
            
            if (targetUrl == null)
            {
                var row = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Canonical Target Not Found")
                    .Set("CanonicalURL", canonical)
                    .Set("Status", "Not Crawled")
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }
}
```

### Repository Methods

- `GetUrlByAddressAsync(projectId, address)` - Get single URL
- `GetUrlsAsync(projectId)` - Stream all URLs
- `GetRedirectsAsync(projectId)` - Stream all redirects
- `GetRedirectAsync(projectId, sourceUrl)` - Check if URL redirects

## URL Helper Utilities

```csharp
using ShoutingIguana.PluginSdk.Helpers;

// Normalize URLs for comparison
var normalized = UrlHelper.Normalize("https://Example.COM/page/");
// Result: "https://example.com/page"

// Resolve relative URLs
var absolute = UrlHelper.Resolve(new Uri(ctx.Url), "../other");

// Check if external
bool isExternal = UrlHelper.IsExternal(ctx.Project.BaseUrl, targetUrl);

// Get domain
var domain = UrlHelper.GetDomain("https://www.example.com/page");
// Result: "example.com"
```

## Best Practices

### ✅ Do

- Use `UrlTaskBase` instead of implementing `IUrlTask` directly
- Return early for non-applicable URLs (check content type, status)
- **Always register a custom report schema** - this is required for all plugins
- **Create ONLY report rows** using `ctx.Reports.ReportAsync()`
- Design columns that make sense for your data type
- Implement `CleanupProject()` if using static state for memory management

### ❌ Don't

- Don't block the thread (use async/await)
- **Don't use `ctx.Findings.ReportAsync()`** - the legacy findings system has been removed
- Don't create `IExportProvider` unless you need specialized export formats
- Don't store per-URL state in instance fields (use static dictionaries with CleanupProject)
- Don't parse HTML twice (use `ctx.RenderedHtml` which is already parsed)

## Severity Levels

- **Error** - Critical issues (404s, broken links, missing required tags)
- **Warning** - Issues to review (suboptimal titles, missing meta)
- **Info** - Informational notices (redirects, statistics)

## Memory Management

Clean up static state when projects close:

```csharp
public class MyTask : UrlTaskBase
{
    private static readonly ConcurrentDictionary<int, HashSet<string>> _cache = new();

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        var projectCache = _cache.GetOrAdd(ctx.Project.ProjectId, _ => new HashSet<string>());
        // Use cache...
    }

    public override void CleanupProject(int projectId)
    {
        _cache.TryRemove(projectId, out _);
    }
}
```

## Complete Example: Duplicate Content Detector

```csharp
// 1. Register schema in plugin
public class DuplicateContentPlugin : IPlugin
{
    public void Initialize(IHostContext context)
    {
        var schema = ReportSchema.Create("DuplicateContent")
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("DuplicateOf", ReportColumnType.Url, "Duplicate Of")
            .AddColumn("ContentHash", ReportColumnType.String, "Hash")
            .AddColumn("Similarity", ReportColumnType.Integer, "Similarity %")
            .Build();
        
        context.RegisterReportSchema(schema);
        context.RegisterTask(new DuplicateContentTask(context.CreateLogger<DuplicateContentTask>()));
    }
}

// 2. Create report rows in task
public class DuplicateContentTask : UrlTaskBase
{
    private static readonly ConcurrentDictionary<int, Dictionary<string, List<string>>> _contentHashes = new();

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (ctx.Metadata.StatusCode != 200) return;
        
        var hash = ComputeContentHash(ctx.RenderedHtml);
        var hashes = _contentHashes.GetOrAdd(ctx.Project.ProjectId, _ => new());
        
        lock (hashes)
        {
            if (!hashes.ContainsKey(hash))
                hashes[hash] = new List<string>();
            
            hashes[hash].Add(ctx.Url.ToString());
            
            if (hashes[hash].Count > 1)
            {
                var duplicateOf = hashes[hash].First(u => u != ctx.Url.ToString());
                
                var row = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("DuplicateOf", duplicateOf)
                    .Set("ContentHash", hash[..12])
                    .Set("Similarity", 100);
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }
    
    public override void CleanupProject(int projectId)
    {
        _contentHashes.TryRemove(projectId, out _);
    }
}
```

## Troubleshooting

**Plugin Not Loading**
- Check `[Plugin]` attribute has correct Id and Name
- Verify class implements `IPlugin`
- Ensure package references `ShoutingIguana.PluginSdk`
- Check logs in `%LocalAppData%/ShoutingIguana/logs/`

**Data Not Appearing**
- Verify you registered a report schema in `Initialize()`
- Verify `ctx.Reports.ReportAsync()` is awaited in your task
- Check that column names in `ReportRow.Create().Set()` match your schema
- Ensure your condition logic isn't filtering out all pages

**Memory Leaks**
- Implement `CleanupProject()` if using static dictionaries
- Don't store URL-specific data in instance fields
- Use weak references for large caches

## Support

- **Issues**: Report bugs and request features on GitHub
- **Examples**: See built-in plugins in `ShoutingIguana.Plugins`
- **API Docs**: XML documentation available in your IDE

## License

MIT License - see LICENSE file for details
