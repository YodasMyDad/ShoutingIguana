using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
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
    public override string DisplayName => "Images";
    public override string Description => "Checks image alt text, file sizes, formats, and optimization opportunities";
    public override int Priority => 60;

    // Helper class to track unique findings with occurrence counts
    private class FindingTracker
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string AltText { get; set; } = string.Empty;
        public int? FileSizeBytes { get; set; }
        public int OccurrenceCount { get; set; } = 1;
    }

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful pages (skip 4xx, 5xx errors)
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);
            
            // Extract base tag if present (respects browser behavior for relative URLs)
            Uri? baseTagUri = UrlHelper.ExtractBaseTag(ctx.RenderedHtml, ctx.Url);

            // Extract all image tags
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            if (imgNodes == null || imgNodes.Count == 0)
            {
                // No images found - this is informational, not an error
                return;
            }

            _logger.LogDebug("Found {Count} images on {Url}", imgNodes.Count, ctx.Url);

            // Track findings to deduplicate them
            var findingsMap = new Dictionary<string, FindingTracker>();

            foreach (var imgNode in imgNodes)
            {
                await AnalyzeImageAsync(ctx, imgNode, findingsMap, baseTagUri);
            }

            // Report all unique findings with occurrence counts
            await ReportUniqueFindings(ctx, findingsMap);

            _logger.LogDebug("Completed image audit for {Url}: {Count} images analyzed, {UniqueFindings} unique findings", 
                ctx.Url, imgNodes.Count, findingsMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing images for {Url}", ctx.Url);
        }
    }

    private async Task AnalyzeImageAsync(UrlContext ctx, HtmlNode imgNode, Dictionary<string, FindingTracker> findingsMap, Uri? baseTagUri)
    {
        var src = imgNode.GetAttributeValue("src", "");
        var srcset = imgNode.GetAttributeValue("srcset", "");
        var loading = imgNode.GetAttributeValue("loading", "");
        var alt = imgNode.GetAttributeValue("alt", "");
        var title = imgNode.GetAttributeValue("title", "");
        var widthStr = imgNode.GetAttributeValue("width", "");
        var heightStr = imgNode.GetAttributeValue("height", "");

        // Handle data URIs
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            await AnalyzeDataUriAsync(ctx, src, alt, findingsMap);
            return;
        }

        if (string.IsNullOrEmpty(src))
        {
            TrackFinding(findingsMap,
                "missing_src",
                Severity.Error,
                "IMAGE_MISSING_SRC",
                "Image element has no src attribute",
                "[missing src attribute]",
                alt,
                null);
            return;
        }

        // Parse dimensions
        int? width = int.TryParse(widthStr, out var w) ? w : null;
        int? height = int.TryParse(heightStr, out var h) ? h : null;

        // Resolve relative URLs using UrlHelper (respects base tag)
        var absoluteSrc = UrlHelper.Resolve(ctx.Url, src, baseTagUri);

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
        AnalyzeAltText(ctx, absoluteSrc, alt, title, width, height, findingsMap);

        // Dimensions analysis (CLS prevention)
        if (!width.HasValue || !height.HasValue)
        {
            // Only warn for images likely to be above the fold or large
            if (height.GetValueOrDefault(0) == 0 || width.GetValueOrDefault(0) == 0 || 
                (width.GetValueOrDefault(100) * height.GetValueOrDefault(100)) > 10000)
            {
                var key = $"{absoluteSrc}|IMAGE_NO_DIMENSIONS|{width.HasValue}|{height.HasValue}";
                TrackFinding(findingsMap, key, Severity.Warning, "IMAGE_NO_DIMENSIONS",
                    $"Image missing width/height attributes (causes Cumulative Layout Shift): {absoluteSrc}",
                    absoluteSrc,
                    alt,
                    null);
            }
        }

        // Lazy loading check
        if (!loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
        {
            if ((width.GetValueOrDefault(0) * height.GetValueOrDefault(0)) > 50000 || 
                (!width.HasValue && !height.HasValue))
            {
                var key = $"{absoluteSrc}|IMAGE_NO_LAZY_LOADING";
                TrackFinding(findingsMap, key, Severity.Info, "IMAGE_NO_LAZY_LOADING",
                    $"Large image not lazy-loaded (causes slow page load): {absoluteSrc}",
                    absoluteSrc,
                    alt,
                    null);
            }
        }

        // Responsive images check
        if (string.IsNullOrWhiteSpace(srcset) && !isExternal && isLegacyFormat)
        {
            if (width.HasValue && width.Value > 400)
            {
                var key = $"{absoluteSrc}|IMAGE_MISSING_SRCSET";
                TrackFinding(findingsMap, key, Severity.Info, "IMAGE_MISSING_SRCSET",
                    $"Image lacks srcset for responsive optimization: {absoluteSrc}",
                    absoluteSrc,
                    alt,
                    null);
            }
        }

        // Format optimization check
        if (isLegacyFormat && !isExternal)
        {
            var key = $"{absoluteSrc}|IMAGE_LEGACY_FORMAT";
            TrackFinding(findingsMap, key, Severity.Info, "IMAGE_LEGACY_FORMAT",
                $"Image uses legacy format (consider WebP/AVIF for better compression): {absoluteSrc}",
                absoluteSrc,
                alt,
                null);
        }

        // Hotlinking check
        if (isExternal)
        {
            var key = $"{absoluteSrc}|IMAGE_EXTERNAL_HOTLINK";
            TrackFinding(findingsMap, key, Severity.Info, "IMAGE_EXTERNAL_HOTLINK",
                $"Image hotlinked from external source: {absoluteSrc}",
                absoluteSrc,
                alt,
                null);
        }

        // SVG with pixel dimensions
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (width.HasValue || height.HasValue)
            {
                var key = $"{absoluteSrc}|SVG_WITH_PIXEL_DIMENSIONS|{width}|{height}";
                TrackFinding(findingsMap, key, Severity.Info, "SVG_WITH_PIXEL_DIMENSIONS",
                    $"SVG image has pixel dimensions (should use CSS for scalability): {absoluteSrc}",
                    absoluteSrc,
                    alt,
                    null);
            }
        }
    }

    private void AnalyzeAltText(UrlContext ctx, string imageUrl, string alt, string title, int? width, int? height, Dictionary<string, FindingTracker> findingsMap)
    {
        // Check for missing alt attribute
        if (string.IsNullOrWhiteSpace(alt))
        {
            var key = $"{imageUrl}|MISSING_ALT_TEXT";
            TrackFinding(findingsMap, key, Severity.Warning, "MISSING_ALT_TEXT",
                $"Image missing alt text: {imageUrl}",
                imageUrl,
                alt,
                null);
            return;
        }

        // Check alt text quality
        if (alt.Length < MIN_ALT_TEXT_LENGTH && !IsLikelyDecorativeAlt(alt))
        {
            var key = $"{imageUrl}|ALT_TEXT_TOO_SHORT|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "ALT_TEXT_TOO_SHORT",
                $"Alt text is very short ({alt.Length} chars): \"{alt}\"",
                imageUrl,
                alt,
                null);
        }
        else if (alt.Length > MAX_ALT_TEXT_LENGTH)
        {
            var preview = alt.Length > 50 ? alt.Substring(0, 50) + "..." : alt;
            var altKey = alt.Length > 100 ? alt.Substring(0, 100) : alt;
            var key = $"{imageUrl}|ALT_TEXT_TOO_LONG|{altKey}";
            TrackFinding(findingsMap, key, Severity.Warning, "ALT_TEXT_TOO_LONG",
                $"Alt text is too long ({alt.Length} chars, recommend <{MAX_ALT_TEXT_LENGTH}): \"{preview}\"",
                imageUrl,
                alt,
                null);
        }

        // Check for decorative images that should have empty alt
        if (IsLikelyDecorative(imageUrl, alt))
        {
            var key = $"{imageUrl}|POTENTIALLY_DECORATIVE|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "POTENTIALLY_DECORATIVE",
                $"Image appears decorative but has alt text (consider alt=\"\"): {imageUrl}",
                imageUrl,
                alt,
                null);
        }

        // Check for redundant title attribute
        if (!string.IsNullOrWhiteSpace(title) && title.Equals(alt, StringComparison.OrdinalIgnoreCase))
        {
            var key = $"{imageUrl}|REDUNDANT_TITLE_ATTRIBUTE|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "REDUNDANT_TITLE_ATTRIBUTE",
                $"Image title attribute duplicates alt text: {imageUrl}",
                imageUrl,
                alt,
                null);
        }

        // Check for common bad patterns
        if (Regex.IsMatch(alt, @"^(image|picture|photo|img|graphic)(\s+of)?", RegexOptions.IgnoreCase))
        {
            var key = $"{imageUrl}|ALT_TEXT_BAD_PATTERN|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "ALT_TEXT_BAD_PATTERN",
                $"Alt text starts with redundant word (image/picture/photo): \"{alt}\"",
                imageUrl,
                alt,
                null);
        }
    }

    private async Task AnalyzeDataUriAsync(UrlContext ctx, string dataUri, string altText, Dictionary<string, FindingTracker> findingsMap)
    {
        try
        {
            // Estimate size of data URI (base64 encoding inflates by ~33%)
            var estimatedSize = (dataUri.Length * 3) / 4;
            var sizeKB = estimatedSize / 1024;

            if (sizeKB > MAX_DATA_URI_SIZE_KB)
            {
                // Use hash of data URI for deduplication (data URI itself is too long for key)
                var dataUriHash = dataUri.GetHashCode().ToString();
                var key = $"datauri_{dataUriHash}|LARGE_DATA_URI";
                var displayUri = dataUri.Length > 120 ? dataUri.Substring(0, 120) + "..." : dataUri;
                TrackFinding(findingsMap, key, Severity.Warning, "LARGE_DATA_URI",
                    $"Large data URI embedded in HTML ({sizeKB}KB, recommend <{MAX_DATA_URI_SIZE_KB}KB)",
                    displayUri,
                    altText,
                    estimatedSize);
            }
        }
        catch
        {
            // Ignore errors in data URI analysis
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Track a finding in the deduplication map. If the same finding already exists, increment its occurrence count.
    /// </summary>
    private void TrackFinding(Dictionary<string, FindingTracker> findingsMap, string key, Severity severity, string code, string message, string imageUrl, string altText, int? fileSizeBytes)
    {
        if (findingsMap.TryGetValue(key, out var existing))
        {
            // Increment occurrence count for duplicate findings
            existing.OccurrenceCount++;
        }
        else
        {
            // Add new unique finding
            findingsMap[key] = new FindingTracker
            {
                Severity = severity,
                Code = code,
                Message = message,
                ImageUrl = imageUrl ?? string.Empty,
                AltText = altText ?? string.Empty,
                FileSizeBytes = fileSizeBytes,
                OccurrenceCount = 1
            };
        }
    }

    /// <summary>
    /// Report all unique findings with occurrence counts to the findings sink.
    /// </summary>
    private async Task ReportUniqueFindings(UrlContext ctx, Dictionary<string, FindingTracker> findingsMap)
    {
        foreach (var tracker in findingsMap.Values)
        {
            var row = ReportRow.Create()
                .Set("ImageURL", tracker.ImageUrl)
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", tracker.Code)
                .Set("AltText", tracker.AltText)
                .Set("FileSize", tracker.FileSizeBytes ?? 0)
                .Set("Severity", tracker.Severity.ToString());
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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

