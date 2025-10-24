using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.TitlesMeta;

/// <summary>
/// Extracts and validates page titles and meta descriptions.
/// </summary>
public class TitlesMetaTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MIN_TITLE_LENGTH = 30;
    private const int MAX_TITLE_LENGTH = 60;
    private const int MIN_DESCRIPTION_LENGTH = 50;
    private const int MAX_DESCRIPTION_LENGTH = 160;

    public override string Key => "TitlesMeta";
    public override string DisplayName => "Titles & Meta";
    public override int Priority => 30;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode?.InnerText?.Trim() ?? "";

            // Extract meta description
            var descNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
            var description = descNode?.GetAttributeValue("content", "")?.Trim() ?? "";

            // Extract canonical
            var canonicalNode = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
            var canonical = canonicalNode?.GetAttributeValue("href", "")?.Trim() ?? "";

            // Extract meta robots
            var robotsNode = doc.DocumentNode.SelectSingleNode("//meta[@name='robots']");
            var robots = robotsNode?.GetAttributeValue("content", "")?.Trim() ?? "";

            // Validate title
            if (string.IsNullOrEmpty(title))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "MISSING_TITLE",
                    "Page is missing a title tag",
                    new { url = ctx.Url.ToString() });
            }
            else
            {
                if (title.Length < MIN_TITLE_LENGTH)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "TITLE_TOO_SHORT",
                        $"Title is too short ({title.Length} chars, recommended: {MIN_TITLE_LENGTH}+)",
                        new { url = ctx.Url.ToString(), title, length = title.Length });
                }
                else if (title.Length > MAX_TITLE_LENGTH)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "TITLE_TOO_LONG",
                        $"Title is too long ({title.Length} chars, recommended: <{MAX_TITLE_LENGTH})",
                        new { url = ctx.Url.ToString(), title, length = title.Length });
                }
                // Don't report OK status - only report issues to avoid database bloat
            }

            // Validate description
            if (string.IsNullOrEmpty(description))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "MISSING_DESCRIPTION",
                    "Page is missing a meta description",
                    new { url = ctx.Url.ToString() });
            }
            else
            {
                if (description.Length < MIN_DESCRIPTION_LENGTH)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DESCRIPTION_TOO_SHORT",
                        $"Meta description is too short ({description.Length} chars, recommended: {MIN_DESCRIPTION_LENGTH}+)",
                        new { url = ctx.Url.ToString(), description, length = description.Length });
                }
                else if (description.Length > MAX_DESCRIPTION_LENGTH)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DESCRIPTION_TOO_LONG",
                        $"Meta description is too long ({description.Length} chars, recommended: <{MAX_DESCRIPTION_LENGTH})",
                        new { url = ctx.Url.ToString(), description, length = description.Length });
                }
                // Don't report OK status - only report issues to avoid database bloat
            }

            // Don't report canonical presence - only report if problematic
            // Canonical URL is already stored in Url.CanonicalUrl field

            // Report robots info
            if (!string.IsNullOrEmpty(robots))
            {
                if (robots.Contains("noindex", StringComparison.OrdinalIgnoreCase))
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "NOINDEX_DETECTED",
                        "Page has noindex directive",
                        new { url = ctx.Url.ToString(), robots });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing titles/meta for {Url}", ctx.Url);
        }
    }
}

