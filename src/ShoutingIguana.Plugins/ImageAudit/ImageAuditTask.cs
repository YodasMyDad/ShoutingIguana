using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.ImageAudit;

/// <summary>
/// Analyzes pages for image usage, alt text, and accessibility issues.
/// </summary>
public class ImageAuditTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;

    public override string Key => "ImageAudit";
    public override string DisplayName => "Image Audit";
    public override int Priority => 60;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful HTML pages
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract all image tags
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            if (imgNodes == null || imgNodes.Count == 0)
            {
                // No images found - this is informational, not an error
                return;
            }

            _logger.LogDebug("Found {Count} images on {Url}", imgNodes.Count, ctx.Url);

            foreach (var imgNode in imgNodes)
            {
                var src = imgNode.GetAttributeValue("src", "");
                if (string.IsNullOrEmpty(src) || src.StartsWith("data:"))
                {
                    // Skip data URIs
                    continue;
                }

                var alt = imgNode.GetAttributeValue("alt", "");
                var widthStr = imgNode.GetAttributeValue("width", "");
                var heightStr = imgNode.GetAttributeValue("height", "");

                // Parse dimensions
                int? width = null;
                int? height = null;
                
                if (int.TryParse(widthStr, out var w))
                {
                    width = w;
                }
                if (int.TryParse(heightStr, out var h))
                {
                    height = h;
                }

                // Resolve relative URLs
                var absoluteSrc = ResolveUrl(ctx.Url, src);

                // Report findings for missing alt text
                if (string.IsNullOrWhiteSpace(alt))
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "MISSING_ALT_TEXT",
                        $"Image missing alt text: {absoluteSrc}",
                        new
                        {
                            imageUrl = absoluteSrc,
                            pageUrl = ctx.Url.ToString(),
                            width,
                            height
                        });
                }

                // Report findings for decorative images with alt text
                if (!string.IsNullOrEmpty(alt) && alt.ToLowerInvariant().Contains("decorative"))
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "DECORATIVE_IMAGE",
                        $"Image appears to be decorative: {absoluteSrc}",
                        new
                        {
                            imageUrl = absoluteSrc,
                            altText = alt,
                            pageUrl = ctx.Url.ToString()
                        });
                }

                // Report findings for images without dimensions
                if (!width.HasValue || !height.HasValue)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "MISSING_DIMENSIONS",
                        $"Image missing width/height attributes: {absoluteSrc}",
                        new
                        {
                            imageUrl = absoluteSrc,
                            altText = alt,
                            pageUrl = ctx.Url.ToString(),
                            hasWidth = width.HasValue,
                            hasHeight = height.HasValue
                        });
                }
            }

            _logger.LogDebug("Completed image audit for {Url}: {Count} images analyzed", ctx.Url, imgNodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing images for {Url}", ctx.Url);
        }
    }

    private string ResolveUrl(Uri baseUri, string relativeUrl)
    {
        try
        {
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (Uri.TryCreate(baseUri, relativeUrl, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }

            return relativeUrl;
        }
        catch
        {
            return relativeUrl;
        }
    }
}

