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
                .SetPage(ctx.Url)
                .Set("Issue", "Title Too Short")
                .Set("Title", title)
                .Set("Length", title.Length)
                .SetSeverity(Severity.Warning);

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

Every plugin registers a schema that tells the UI which columns to render. Think of this as configuring the datagrid: keep it minimal for simple checks (Page + Issue) or invest in richer layouts for data-heavy plugins like Broken Links or Link Graph.

### Schema Design Tips

- **Build richer schemas for**: Tabular data with consistent fields (broken links, internal linking, redirects)
- **Keep schemas lean for**: One-off issues or highly variable findings (Page + Issue + Details is plenty)

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
    // Only report via ctx.Reports.ReportAsync() so the UI can build dynamic columns
    var row = ReportRow.Create()
        .SetPage(ctx.Url)
        .Set("ToURL", targetUrl)
        .Set("AnchorText", anchorText)
        .Set("LinkType", linkType)
        .SetSeverity(Severity.Info);
    
    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
}
```

**IMPORTANT:** If you register a custom schema, create **ONLY** report rows using `ctx.Reports.ReportAsync()`. Legacy `ctx.Findings` APIs have been removed.

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
    .AddPrimaryColumn("Page", ReportColumnType.Url, "Page URL")  // Primary column (bold, shown first)
    .AddColumn("Count", ReportColumnType.Integer, "Issue Count")
    .AddColumn("LastChecked", ReportColumnType.DateTime, "Last Checked")
    .Build();
```

Primary columns are shown first and typically bold—use for the main identifier (URL, page title, etc.).

**Note:** The `Severity` column is automatically added to all schemas by `Build()`. You don't need to add it manually.

## Reporting Issues

All plugins must use **report schemas** to display data with custom columns. There is no legacy "findings" system - everything uses typed report rows.

## Accessing Crawled Data

Query URLs and redirects without reflection:

```csharp
public class CanonicalTask : UrlTaskBase
{
    private readonly IRepositoryAccessor _accessor;

    public CanonicalTask(IRepositoryAccessor accessor)
    {
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
                    .SetPage(ctx.Url)
                    .Set("Issue", "Canonical Target Not Found")
                    .Set("CanonicalURL", canonical)
                    .Set("Status", "Not Crawled")
                    .SetSeverity(Severity.Warning);
                
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
var absolute = UrlHelper.Resolve(ctx.Url, "../other");

// Check if external
bool isExternal = UrlHelper.IsExternal(ctx.Project.BaseUrl, targetUrl);

// Get domain
var domain = UrlHelper.GetDomain("https://www.example.com/page");
// Result: "example.com"
```

## Best Practices

### Do

- Use `UrlTaskBase` instead of implementing `IUrlTask` directly
- Return early for non-applicable URLs (check content type, status)
- **Always register a custom report schema** - this is required for all plugins
- **Create ONLY report rows** using `ctx.Reports.ReportAsync()`
- **Use helper methods** - prefer `.SetSeverity(Severity.Error)` over `.Set("Severity", "Error")`
- **Use `.SetPage(ctx.Url)`** instead of `.Set("Page", ctx.Url.ToString())` for cleaner code
- Design columns that make sense for your data type
- Implement `CleanupProject()` if using static state for memory management

### Don't

- Don't block the thread (use async/await)
- **Use `ctx.Reports.ReportAsync()` exclusively** - the legacy findings system has been removed
- **Don't use magic strings for Severity** - use `.SetSeverity(Severity.Info)` instead of `.Set("Severity", "Info")`
- Don't create `IExportProvider` unless you need specialized export formats
- Don't store per-URL state in instance fields (use static dictionaries with CleanupProject)
- Don't parse HTML twice (use `ctx.RenderedHtml` which is already parsed)

## Helper Methods

ReportRow provides convenient helper methods for common columns:

### SetSeverity

Use the `Severity` enum instead of magic strings:

```csharp
var row = ReportRow.Create()
    .SetSeverity(Severity.Error)    // Preferred - type-safe
    // .Set("Severity", "Error")    // Avoid - magic string
```

### SetPage

Convenient helper for the common "Page" column:

```csharp
var row = ReportRow.Create()
    .SetPage(ctx.Url)               // Preferred - cleaner
    // .Set("Page", ctx.Url.ToString())  // Works but verbose
```

You can also use `SetPage(string url)` if you already have a URL string.

## Severity Levels

- **Error** - Critical issues (404s, broken links, missing required tags)
- **Warning** - Issues to review (suboptimal titles, missing meta)
- **Info** - Informational notices (redirects, statistics)

Use the `Severity` enum with `SetSeverity()` for type safety:
```csharp
.SetSeverity(Severity.Error)
.SetSeverity(Severity.Warning)
.SetSeverity(Severity.Info)
```

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
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("DuplicateOf", ReportColumnType.Url, "Duplicate Of")
            .AddColumn("Similarity", ReportColumnType.Integer, "Similarity %")
            // Severity column is automatically added by Build()
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
                    .SetPage(ctx.Url)
                    .Set("Issue", "Exact duplicate content")
                    .Set("DuplicateOf", duplicateOf)
                    .Set("Similarity", 100)
                    .SetSeverity(Severity.Error);
                
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
