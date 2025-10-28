using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace MyCompany.ShoutingIguana.Plugins;

/// <summary>
/// Minimal plugin template - analyzes pages for a specific SEO issue.
/// Copy this file and customize for your needs.
/// </summary>
[Plugin(Id = "com.mycompany.myplugin", Name = "My SEO Plugin", MinSdkVersion = "1.0.0")]
public class MyPlugin : IPlugin
{
    public string Id => "com.mycompany.myplugin";
    public string Name => "My SEO Plugin";
    public Version Version => new(1, 0, 0);
    public string Description => "Checks for [describe what your plugin does]";

    public void Initialize(IHostContext context)
    {
        var logger = context.CreateLogger<MyTask>();
        context.RegisterTask(new MyTask(logger));
    }
}

/// <summary>
/// Analysis task that runs for each crawled URL.
/// </summary>
public class MyTask : UrlTaskBase
{
    private readonly ILogger _logger;

    public MyTask(ILogger logger)
    {
        _logger = logger;
    }

    public override string Key => "MyCheck";
    public override string DisplayName => "My SEO Check";
    public override string Description => "Analyzes pages for [your specific check]";
    public override int Priority => 100; // Default priority (lower numbers run first)

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

        // Check if we have HTML content
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        try
        {
            // Parse HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Example: Check for short titles
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode?.InnerText?.Trim() ?? "";

            if (string.IsNullOrEmpty(title))
            {
                await ReportMissingTitle(ctx);
            }
            else if (title.Length < 30)
            {
                await ReportShortTitle(ctx, title);
            }

            // Add more checks here...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing {Url}", ctx.Url);
        }
    }

    private async Task ReportMissingTitle(UrlContext ctx)
    {
        var details = FindingDetailsBuilder.Create()
            .AddItem($"Page: {ctx.Url}")
            .AddItem("❌ No title tag found")
            .BeginNested("📉 SEO Impact")
                .AddItem("Page will not rank well in search results")
                .AddItem("Users won't see a title in search results")
            .EndNested()
            .BeginNested("💡 Recommendation")
                .AddItem("Add a <title> tag in the <head> section")
                .AddItem("Make it descriptive and unique (30-60 characters)")
            .EndNested()
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("statusCode", ctx.Metadata.StatusCode)
            .Build();

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Error,
            "MISSING_TITLE",
            "Page has no title tag",
            details);
    }

    private async Task ReportShortTitle(UrlContext ctx, string title)
    {
        var details = FindingDetailsBuilder.Simple(
            $"Title: \"{title}\"",
            $"Length: {title.Length} characters",
            $"Recommended: At least 30 characters",
            "Short titles may not convey enough information to users and search engines"
        );

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "SHORT_TITLE",
            $"Title is too short ({title.Length} chars)",
            details);
    }
}

