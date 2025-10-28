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
- **FindingDetailsBuilder** - Create structured, user-friendly reports
- **IRepositoryAccessor** - Query crawled data efficiently
- **IHostContext** - Access logging and task registration

## Building Findings

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
    .BeginNested("💡 Recommendations")
        .AddItem("Fix or remove broken links")
        .AddItem("Implement 301 redirects where appropriate")
    .WithTechnicalMetadata("brokenLinkCount", count)
    .Build();
```

> **Note:** `EndNested()` is optional - `Build()` automatically closes any open nested sections. You can still use `EndNested()` for explicit control when working with complex multi-level nesting or when you need to add items at different nesting levels.

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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "CANONICAL_TARGET_NOT_FOUND",
                    $"Canonical points to uncrawled URL: {canonical}",
                    null);
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
- Use `FindingDetailsBuilder` for structured findings
- Add technical metadata for debugging
- Use emojis in section headers (📉, 💡, ⚠️, ✅)
- Implement `CleanupProject()` if using static state

### ❌ Don't

- Don't block the thread (use async/await)
- Don't create `IExportProvider` unless you need specialized formats
- Don't store per-URL state in instance fields
- Don't parse HTML twice (use `ctx.RenderedHtml`)

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

## Example: Duplicate Content Detector

```csharp
public class DuplicateContentTask : UrlTaskBase
{
    private static readonly ConcurrentDictionary<int, Dictionary<string, List<string>>> _contentHashes = new();

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (ctx.Metadata.Status != 200) return;
        
        var hash = ComputeContentHash(ctx.RenderedHtml);
        var hashes = _contentHashes.GetOrAdd(ctx.Project.ProjectId, _ => new());
        
        lock (hashes)
        {
            if (!hashes.ContainsKey(hash))
                hashes[hash] = new List<string>();
            
            hashes[hash].Add(ctx.Url.ToString());
            
            if (hashes[hash].Count > 1)
            {
                var others = hashes[hash].Where(u => u != ctx.Url.ToString());
                
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Duplicate content detected")
                    .BeginNested("📄 Other pages with same content")
                        .AddItems(others.ToArray())
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

## Troubleshooting

**Plugin Not Loading**
- Check `[Plugin]` attribute has correct Id and Name
- Verify class implements `IPlugin`
- Ensure package references `ShoutingIguana.PluginSdk`
- Check logs in `%LocalAppData%/ShoutingIguana/logs/`

**Findings Not Appearing**
- Verify `ctx.Findings.ReportAsync()` is awaited
- Check task is registered in `Initialize()`
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
