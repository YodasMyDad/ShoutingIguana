using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.ImageAudit;

/// <summary>
/// Comprehensive image optimization, accessibility, and performance analysis.
/// </summary>
public class ImageAuditTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MAX_IMAGE_SIZE_KB = 500;
    private const int LARGE_IMAGE_THRESHOLD_KB = 200;
    private const int MAX_ALT_TEXT_LENGTH = 125;
    private const int MIN_ALT_TEXT_LENGTH = 5;
    private const int MAX_DATA_URI_SIZE_KB = 10;

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
                await AnalyzeImageAsync(ctx, imgNode);
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

    private async Task AnalyzeImageAsync(UrlContext ctx, HtmlNode imgNode)
    {
        var src = imgNode.GetAttributeValue("src", "");
        var srcset = imgNode.GetAttributeValue("srcset", "");
        var loading = imgNode.GetAttributeValue("loading", "");
        var alt = imgNode.GetAttributeValue("alt", "");
        var title = imgNode.GetAttributeValue("title", "");
        var widthStr = imgNode.GetAttributeValue("width", "");
        var heightStr = imgNode.GetAttributeValue("height", "");

        // Handle data URIs
        if (src.StartsWith("data:"))
        {
            await AnalyzeDataUriAsync(ctx, src);
            return;
        }

        if (string.IsNullOrEmpty(src))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "IMAGE_MISSING_SRC",
                "Image element has no src attribute",
                new { pageUrl = ctx.Url.ToString(), outerHtml = imgNode.OuterHtml.Length > 200 ? imgNode.OuterHtml.Substring(0, 200) : imgNode.OuterHtml });
            return;
        }

        // Parse dimensions
        int? width = int.TryParse(widthStr, out var w) ? w : null;
        int? height = int.TryParse(heightStr, out var h) ? h : null;

        // Resolve relative URLs
        var absoluteSrc = ResolveUrl(ctx.Url, src);

        // Determine if external image
        bool isExternal = IsExternalImage(ctx.Project.BaseUrl, absoluteSrc);

        // Extract file extension
        var extension = GetFileExtension(absoluteSrc);
        bool isModernFormat = extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".avif", StringComparison.OrdinalIgnoreCase);
        bool isLegacyFormat = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

        // Alt text analysis
        await AnalyzeAltTextAsync(ctx, absoluteSrc, alt, title, width, height);

        // Dimensions analysis (CLS prevention)
        if (!width.HasValue || !height.HasValue)
        {
            // Only warn for images likely to be above the fold or large
            if (height.GetValueOrDefault(0) == 0 || width.GetValueOrDefault(0) == 0 || 
                (width.GetValueOrDefault(100) * height.GetValueOrDefault(100)) > 10000)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "IMAGE_NO_DIMENSIONS",
                    $"Image missing width/height attributes (causes Cumulative Layout Shift): {absoluteSrc}",
                    new
                    {
                        imageUrl = absoluteSrc,
                        pageUrl = ctx.Url.ToString(),
                        hasWidth = width.HasValue,
                        hasHeight = height.HasValue,
                        altText = alt
                    });
            }
        }

        // Lazy loading check
        if (!loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
        {
            // Only recommend lazy loading for images that are likely below the fold
            // We'll assume images without dimensions or large images should be lazy-loaded
            if ((width.GetValueOrDefault(0) * height.GetValueOrDefault(0)) > 50000 || 
                (!width.HasValue && !height.HasValue))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "IMAGE_NO_LAZY_LOADING",
                    $"Large image not lazy-loaded (causes slow page load): {absoluteSrc}",
                    new
                    {
                        imageUrl = absoluteSrc,
                        pageUrl = ctx.Url.ToString(),
                        width,
                        height
                    });
            }
        }

        // Responsive images check
        if (string.IsNullOrWhiteSpace(srcset) && !isExternal && isLegacyFormat)
        {
            if (width.HasValue && width.Value > 400)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "IMAGE_MISSING_SRCSET",
                    $"Image lacks srcset for responsive optimization: {absoluteSrc}",
                    new
                    {
                        imageUrl = absoluteSrc,
                        pageUrl = ctx.Url.ToString(),
                        width,
                        height,
                        recommendation = "Use srcset to serve different sizes for different devices"
                    });
            }
        }

        // Format optimization check
        if (isLegacyFormat && !isExternal)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "IMAGE_LEGACY_FORMAT",
                $"Image uses legacy format (consider WebP/AVIF for better compression): {absoluteSrc}",
                new
                {
                    imageUrl = absoluteSrc,
                    format = extension,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "WebP offers 25-35% better compression than JPEG/PNG"
                });
        }

        // Hotlinking check
        if (isExternal)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "IMAGE_EXTERNAL_HOTLINK",
                $"Image hotlinked from external source: {absoluteSrc}",
                new
                {
                    imageUrl = absoluteSrc,
                    pageUrl = ctx.Url.ToString(),
                    warning = "External images may break if source changes or removes them"
                });
        }

        // SVG with pixel dimensions
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (width.HasValue || height.HasValue)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "SVG_WITH_PIXEL_DIMENSIONS",
                    $"SVG image has pixel dimensions (should use CSS for scalability): {absoluteSrc}",
                    new
                    {
                        imageUrl = absoluteSrc,
                        width,
                        height,
                        pageUrl = ctx.Url.ToString()
                    });
            }
        }
    }

    private async Task AnalyzeAltTextAsync(UrlContext ctx, string imageUrl, string alt, string title, int? width, int? height)
    {
        // Check for missing alt attribute
        if (string.IsNullOrWhiteSpace(alt))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_ALT_TEXT",
                $"Image missing alt text: {imageUrl}",
                new
                {
                    imageUrl,
                    pageUrl = ctx.Url.ToString(),
                    width,
                    height,
                    recommendation = "Add descriptive alt text for accessibility and SEO"
                });
            return;
        }

        // Check alt text quality
        if (alt.Length < MIN_ALT_TEXT_LENGTH && !IsLikelyDecorativeAlt(alt))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "ALT_TEXT_TOO_SHORT",
                $"Alt text is very short ({alt.Length} chars): \"{alt}\"",
                new
                {
                    imageUrl,
                    altText = alt,
                    length = alt.Length,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "Alt text should be descriptive (at least 5 characters)"
                });
        }
        else if (alt.Length > MAX_ALT_TEXT_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "ALT_TEXT_TOO_LONG",
                $"Alt text is too long ({alt.Length} chars, recommend <{MAX_ALT_TEXT_LENGTH}): \"{alt.Substring(0, Math.Min(alt.Length, 50))}...\"",
                new
                {
                    imageUrl,
                    altText = alt,
                    length = alt.Length,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "Keep alt text concise and descriptive"
                });
        }

        // Check for decorative images that should have empty alt
        if (IsLikelyDecorative(imageUrl, alt))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "POTENTIALLY_DECORATIVE",
                $"Image appears decorative but has alt text (consider alt=\"\"): {imageUrl}",
                new
                {
                    imageUrl,
                    altText = alt,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "Use alt=\"\" for purely decorative images"
                });
        }

        // Check for redundant title attribute
        if (!string.IsNullOrWhiteSpace(title) && title.Equals(alt, StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "REDUNDANT_TITLE_ATTRIBUTE",
                $"Image title attribute duplicates alt text: {imageUrl}",
                new
                {
                    imageUrl,
                    altText = alt,
                    title,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "Remove redundant title attribute or make it different from alt"
                });
        }

        // Check for common bad patterns
        if (Regex.IsMatch(alt, @"^(image|picture|photo|img|graphic)(\s+of)?", RegexOptions.IgnoreCase))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "ALT_TEXT_BAD_PATTERN",
                $"Alt text starts with redundant word (image/picture/photo): \"{alt}\"",
                new
                {
                    imageUrl,
                    altText = alt,
                    pageUrl = ctx.Url.ToString(),
                    recommendation = "Screen readers already announce it's an image - start with the description"
                });
        }
    }

    private async Task AnalyzeDataUriAsync(UrlContext ctx, string dataUri)
    {
        try
        {
            // Estimate size of data URI (base64 encoding inflates by ~33%)
            var estimatedSize = (dataUri.Length * 3) / 4;
            var sizeKB = estimatedSize / 1024;

            if (sizeKB > MAX_DATA_URI_SIZE_KB)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "LARGE_DATA_URI",
                    $"Large data URI embedded in HTML ({sizeKB}KB, recommend <{MAX_DATA_URI_SIZE_KB}KB)",
                    new
                    {
                        sizeKB,
                        pageUrl = ctx.Url.ToString(),
                        recommendation = "Large data URIs bloat HTML size and prevent caching - use external image files"
                    });
            }
        }
        catch
        {
            // Ignore errors in data URI analysis
        }
    }

    private bool IsLikelyDecorative(string imageUrl, string altText)
    {
        var lowercaseUrl = imageUrl.ToLowerInvariant();
        var lowercaseAlt = altText.ToLowerInvariant();

        return lowercaseUrl.Contains("icon") ||
               lowercaseUrl.Contains("spacer") ||
               lowercaseUrl.Contains("divider") ||
               lowercaseUrl.Contains("bullet") ||
               lowercaseAlt.Contains("decorative") ||
               lowercaseAlt.Contains("spacer") ||
               lowercaseAlt.Contains("decoration");
    }

    private bool IsLikelyDecorativeAlt(string altText)
    {
        return string.IsNullOrEmpty(altText) || 
               altText.Equals("*", StringComparison.Ordinal) ||
               altText.Equals("-", StringComparison.Ordinal) ||
               altText.Equals("·", StringComparison.Ordinal);
    }

    private string GetFileExtension(string url)
    {
        try
        {
            // Remove query string and fragment
            var urlWithoutQuery = url.Split('?', '#')[0];
            var lastDot = urlWithoutQuery.LastIndexOf('.');
            if (lastDot >= 0)
            {
                return urlWithoutQuery.Substring(lastDot);
            }
        }
        catch
        {
            // Ignore
        }
        return string.Empty;
    }

    private bool IsExternalImage(string baseUrl, string imageUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return true;
        }

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imgUri))
        {
            var baseHost = baseUri.Host.ToLowerInvariant().TrimStart('w', 'w', 'w', '.');
            var imgHost = imgUri.Host.ToLowerInvariant().TrimStart('w', 'w', 'w', '.');
            return baseHost != imgHost;
        }

        return false; // Relative URL is internal
    }
}

