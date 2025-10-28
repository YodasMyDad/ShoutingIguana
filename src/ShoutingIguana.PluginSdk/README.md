# Shouting Iguana Plugin SDK

Build powerful SEO analysis plugins for the Shouting Iguana crawler with minimal code.

## Quick Start

### Installation

```bash
dotnet add package ShoutingIguana.PluginSdk
```

### Your First Plugin (5 minutes)

Create a file `MyPlugin.cs`:

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
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Title: \"{title}\"")
                .AddItem($"Length: {title.Length} characters")
                .BeginNested("💡 Recommendation")
                    .AddItem("Titles should be at least 30 characters")
                    .AddItem("Include relevant keywords")
                .EndNested()
                .Build();

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "SHORT_TITLE",
                $"Title is too short ({title.Length} chars)",
                details);
        }
    }
}
```

**That's it!** Your findings automatically appear in CSV/Excel/PDF exports.

## Architecture Overview

### Plugin Lifecycle

```
Plugin Installation → Initialize() → Register Tasks → Crawl Starts → ExecuteAsync() per URL → Export
```

### Key Concepts

- **IPlugin**: Entry point for your plugin
- **IUrlTask**: Analysis logic that runs for each crawled URL
- **FindingDetailsBuilder**: Create structured, user-friendly finding reports
- **IRepositoryAccessor**: Query crawled data without reflection
- **Automatic Exports**: All findings export to CSV/Excel/PDF automatically

## Core Interfaces

### IPlugin

The plugin entry point. Implement this to register your analysis tasks.

```csharp
[Plugin(Id = "com.example.plugin", Name = "My Plugin")]
public class MyPlugin : IPlugin
{
    public string Id => "com.example.plugin";
    public string Name => "My Plugin";
    public Version Version => new(1, 0, 0);
    public string Description => "Plugin description";

    public void Initialize(IHostContext context)
    {
        // Register tasks here
        context.RegisterTask(new MyTask(context.CreateLogger<MyTask>()));
    }
}
```

### IUrlTask

Analysis logic that runs for each URL. Extend `UrlTaskBase` for convenience.

```csharp
public class MyTask : UrlTaskBase
{
    public override string Key => "MyCheck";
    public override string DisplayName => "My Check";
    public override int Priority => 100; // Lower = runs earlier

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // ctx.Url - Current URL being analyzed
        // ctx.RenderedHtml - HTML content
        // ctx.Page - Browser page (if JavaScript rendering used)
        // ctx.Metadata - Status code, content type, etc.
        // ctx.Findings - Report issues
        // ctx.Logger - Log messages
        
        // Your analysis logic here
    }
}
```

## Building Findings

Use `FindingDetailsBuilder` to create well-structured findings:

### Simple Finding

```csharp
var details = FindingDetailsBuilder.Simple(
    "Page URL: https://example.com",
    "Missing H1 tag",
    "H1 tags are important for SEO"
);

await ctx.Findings.ReportAsync(
    Key,
    Severity.Error,
    "MISSING_H1",
    "Page has no H1 tag",
    details);
```

### Structured Finding with Sections

```csharp
var details = FindingDetailsBuilder.Create()
    .AddItem($"Page: {ctx.Url}")
    .AddItem($"Found {count} broken links")
    .BeginNested("📉 SEO Impact")
        .AddItem("Broken links harm user experience")
        .AddItem("May reduce page authority")
    .EndNested()
    .BeginNested("💡 Recommendations")
        .AddItem("Fix or remove broken links")
        .AddItem("Implement 301 redirects where appropriate")
    .EndNested()
    .WithTechnicalMetadata("brokenLinkCount", count)
    .WithTechnicalMetadata("pageDepth", ctx.Metadata.Depth)
    .Build();
```

## Accessing Crawled Data

Use `IRepositoryAccessor` to query URLs and redirects without reflection:

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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "CANONICAL_TARGET_NOT_FOUND",
                    $"Canonical points to uncrawled URL: {canonical}",
                    null);
            }
            else if (targetUrl.Status >= 400)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "CANONICAL_TARGET_ERROR",
                    $"Canonical points to error page ({targetUrl.Status})",
                    null);
            }
        }
    }
}
```

### Repository Accessor Methods

- `GetUrlByAddressAsync(projectId, address)` - Get single URL
- `GetUrlsAsync(projectId)` - Stream all URLs (efficient for large datasets)
- `GetRedirectsAsync(projectId)` - Stream all redirects
- `GetRedirectAsync(projectId, sourceUrl)` - Check if URL redirects

## Helper Utilities

### UrlHelper

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

// Check HTTPS
bool secure = UrlHelper.IsHttps(url);
```

## Memory Management

If your plugin uses static state (caches, dictionaries), implement cleanup:

```csharp
public class MyTask : UrlTaskBase
{
    // Static cache shared across all instances
    private static readonly ConcurrentDictionary<int, HashSet<string>> _cache = new();

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        var projectCache = _cache.GetOrAdd(ctx.Project.ProjectId, _ => new HashSet<string>());
        // Use cache...
    }

    // IMPORTANT: Clean up when project closes
    public override void CleanupProject(int projectId)
    {
        _cache.TryRemove(projectId, out _);
    }
}
```

## Export Providers (Advanced)

**99% of plugins don't need this.** Findings automatically export to CSV/Excel/PDF.

Only implement `IExportProvider` if you need specialized formats:

```csharp
public class JsonExporter : IExportProvider
{
    public string Key => "MyPluginJson";
    public string DisplayName => "My Plugin (JSON)";
    public string FileExtension => ".json";

    public async Task<ExportResult> ExportAsync(ExportContext ctx, CancellationToken ct)
    {
        // Generate custom JSON export
        // Access ctx.ProjectId, ctx.FilePath
        // Query data, serialize to JSON
        return new ExportResult(true);
    }
}

// Register in Initialize()
context.RegisterExport(new JsonExporter(logger, serviceProvider));
```

## Best Practices

### ✅ Do

- Use `UrlTaskBase` instead of `IUrlTask`
- Return early for non-applicable URLs (check content type, status)
- Use `FindingDetailsBuilder` for structured findings
- Add technical metadata for debugging
- Use emojis in section headers (📉, 💡, ⚠️, ✅)
- Log important events with `ctx.Logger`
- Implement `CleanupProject()` if using static state

### ❌ Don't

- Don't use reflection to access repositories (use `IRepositoryAccessor`)
- Don't block the thread (use async/await)
- Don't create `IExportProvider` unless you need specialized formats
- Don't store per-URL state in instance fields (tasks can be reused)
- Don't parse HTML twice (use `ctx.RenderedHtml`)

## Severity Levels

- **Error**: Critical issues that must be fixed (404s, broken links, missing required tags)
- **Warning**: Issues that should be reviewed (suboptimal titles, missing meta)
- **Info**: Informational notices (redirects, noindex pages, statistics)

## Task Priority

Lower priority numbers run first:

- **10-30**: Early tasks (URL inventory, robots detection)
- **50-70**: Content analysis (broken links, titles, meta tags)
- **100**: Default priority
- **150-200**: Tasks that depend on others (duplicate detection, summaries)

## Example: Duplicate Content Detector

```csharp
public class DuplicateContentTask : UrlTaskBase
{
    private static readonly ConcurrentDictionary<int, Dictionary<string, List<string>>> _contentHashes = new();
    private readonly IRepositoryAccessor _accessor;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (ctx.Metadata.Status != 200) return;
        
        var hash = ComputeContentHash(ctx.RenderedHtml);
        var hashes = _contentHashes.GetOrAdd(ctx.Project.ProjectId, _ => new());
        
        lock (hashes)
        {
            if (!hashes.ContainsKey(hash))
            {
                hashes[hash] = new List<string>();
            }
            hashes[hash].Add(ctx.Url.ToString());
            
            if (hashes[hash].Count > 1)
            {
                var others = hashes[hash].Where(u => u != ctx.Url.ToString());
                
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Duplicate content detected")
                    .BeginNested("📄 Other pages with same content")
                        .AddItems(others.ToArray())
                    .EndNested()
                    .WithTechnicalMetadata("contentHash", hash)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key, Severity.Warning, "DUPLICATE_CONTENT",
                    "Page has duplicate content", details);
            }
        }
    }
    
    public override void CleanupProject(int projectId)
    {
        _contentHashes.TryRemove(projectId, out _);
    }
}
```

## Testing Your Plugin

1. Build your plugin project
2. Package as NuGet: `dotnet pack -c Release`
3. In Shouting Iguana: Extensions → Install from File
4. Run a test crawl
5. Check Findings tab for your issues
6. Export to CSV/Excel to verify formatting

## Publishing to NuGet

```bash
# Build and pack
dotnet pack -c Release

# Publish
dotnet nuget push bin/Release/YourPlugin.1.0.0.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json
```

### Package Metadata

```xml
<PropertyGroup>
  <PackageId>YourCompany.ShoutingIguana.YourPlugin</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Description>Your plugin description</Description>
  <PackageTags>shoutingiguana-plugin;seo;crawler</PackageTags>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
```

## Troubleshooting

### Plugin Not Loading

- Check `[Plugin]` attribute has correct Id and Name
- Verify class implements `IPlugin`
- Ensure package references `ShoutingIguana.PluginSdk`
- Check logs in `%LocalAppData%/ShoutingIguana/logs/`

### Findings Not Appearing

- Verify `ctx.Findings.ReportAsync()` is awaited
- Check severity level (Error, Warning, Info all show)
- Ensure task returns from `ExecuteAsync()` after reporting
- Check task is registered in `Initialize()`

### Memory Leaks

- Implement `CleanupProject()` if using static dictionaries
- Don't store URL-specific data in instance fields
- Use weak references for large caches
- Profile with diagnostic tools

## API Reference

See XML documentation in your IDE for detailed API docs on:

- `IPlugin` - Plugin entry point
- `IUrlTask` / `UrlTaskBase` - URL analysis
- `FindingDetailsBuilder` - Build structured findings
- `IRepositoryAccessor` - Query crawled data
- `UrlHelper` - URL utilities
- `IHostContext` - Plugin initialization context
- `UrlContext` - URL analysis context
- `FindingDetails` - Structured finding data

## Support & Community

- **Documentation**: [github.com/yourorg/ShoutingIguana/docs](https://github.com/yourorg/ShoutingIguana/docs)
- **Issues**: [github.com/yourorg/ShoutingIguana/issues](https://github.com/yourorg/ShoutingIguana/issues)
- **Examples**: See built-in plugins in the source repository

## License

MIT License - see LICENSE file for details

