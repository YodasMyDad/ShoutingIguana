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
    public override string DisplayName => "Images";
    public override string Description => "Checks image alt text, file sizes, formats, and optimization opportunities";
    public override int Priority => 60;

    // Helper class to track unique findings with occurrence counts
    private class FindingTracker
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public FindingDetails? Data { get; set; }
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

            // Track findings to deduplicate them
            var findingsMap = new Dictionary<string, FindingTracker>();

            foreach (var imgNode in imgNodes)
            {
                await AnalyzeImageAsync(ctx, imgNode, findingsMap);
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

    private async Task AnalyzeImageAsync(UrlContext ctx, HtmlNode imgNode, Dictionary<string, FindingTracker> findingsMap)
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
            await AnalyzeDataUriAsync(ctx, src, findingsMap);
            return;
        }

        if (string.IsNullOrEmpty(src))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Image element has no src attribute")
                .AddItem("❌ Image will not display")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add a valid src attribute to the img tag")
                    .AddItem("Or remove the invalid img element")
                .EndNested()
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .WithTechnicalMetadata("outerHtml", imgNode.OuterHtml.Length > 200 ? imgNode.OuterHtml.Substring(0, 200) : imgNode.OuterHtml)
                .Build();
            
            TrackFinding(findingsMap,
                "missing_src",
                Severity.Error,
                "IMAGE_MISSING_SRC",
                "Image element has no src attribute",
                details);
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
        AnalyzeAltText(ctx, absoluteSrc, alt, title, width, height, findingsMap);

        // Dimensions analysis (CLS prevention)
        if (!width.HasValue || !height.HasValue)
        {
            // Only warn for images likely to be above the fold or large
            if (height.GetValueOrDefault(0) == 0 || width.GetValueOrDefault(0) == 0 || 
                (width.GetValueOrDefault(100) * height.GetValueOrDefault(100)) > 10000)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Image: {absoluteSrc}")
                    .AddItem($"Width attribute: {(width.HasValue ? width.Value.ToString() : "missing")}")
                    .AddItem($"Height attribute: {(height.HasValue ? height.Value.ToString() : "missing")}")
                    .BeginNested("⚠️ Impact")
                        .AddItem("Missing dimensions cause Cumulative Layout Shift (CLS)")
                        .AddItem("Page content jumps when images load")
                        .AddItem("Hurts Core Web Vitals score")
                    .EndNested()
                    .BeginNested("💡 Recommendations")
                        .AddItem("Add width and height attributes to img tag")
                        .AddItem("Set dimensions even if using CSS (helps browser reserve space)")
                    .EndNested()
                    .WithTechnicalMetadata("imageUrl", absoluteSrc)
                    .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("hasWidth", width.HasValue)
                    .WithTechnicalMetadata("hasHeight", height.HasValue)
                    .WithTechnicalMetadata("altText", alt)
                    .Build();
                
                var key = $"{absoluteSrc}|IMAGE_NO_DIMENSIONS|{width.HasValue}|{height.HasValue}";
                TrackFinding(findingsMap, key, Severity.Warning, "IMAGE_NO_DIMENSIONS",
                    $"Image missing width/height attributes (causes Cumulative Layout Shift): {absoluteSrc}",
                    details);
            }
        }

        // Lazy loading check
        if (!loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
        {
            if ((width.GetValueOrDefault(0) * height.GetValueOrDefault(0)) > 50000 || 
                (!width.HasValue && !height.HasValue))
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Image: {absoluteSrc}")
                    .AddItem($"Dimensions: {width ?? 0} × {height ?? 0}")
                    .AddItem("⚡ No lazy loading attribute")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Add loading=\"lazy\" attribute")
                        .AddItem("Defers loading of below-fold images")
                        .AddItem("Improves initial page load speed")
                    .EndNested()
                    .WithTechnicalMetadata("imageUrl", absoluteSrc)
                    .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("width", width)
                    .WithTechnicalMetadata("height", height)
                    .Build();
                
                var key = $"{absoluteSrc}|IMAGE_NO_LAZY_LOADING";
                TrackFinding(findingsMap, key, Severity.Info, "IMAGE_NO_LAZY_LOADING",
                    $"Large image not lazy-loaded (causes slow page load): {absoluteSrc}",
                    details);
            }
        }

        // Responsive images check
        if (string.IsNullOrWhiteSpace(srcset) && !isExternal && isLegacyFormat)
        {
            if (width.HasValue && width.Value > 400)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Image: {absoluteSrc}")
                    .AddItem($"Width: {width}px")
                    .AddItem("📱 No srcset for responsive images")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Use srcset to serve different sizes for different devices")
                        .AddItem("Saves bandwidth on mobile devices")
                        .AddItem("Example: srcset=\"image-400w.jpg 400w, image-800w.jpg 800w\"")
                    .EndNested()
                    .WithTechnicalMetadata("imageUrl", absoluteSrc)
                    .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("width", width)
                    .WithTechnicalMetadata("height", height)
                    .Build();
                
                var key = $"{absoluteSrc}|IMAGE_MISSING_SRCSET";
                TrackFinding(findingsMap, key, Severity.Info, "IMAGE_MISSING_SRCSET",
                    $"Image lacks srcset for responsive optimization: {absoluteSrc}",
                    details);
            }
        }

        // Format optimization check
        if (isLegacyFormat && !isExternal)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {absoluteSrc}")
                .AddItem($"Format: {extension.ToUpperInvariant()}")
                .BeginNested("💡 Optimization")
                    .AddItem("Consider converting to WebP or AVIF format")
                    .AddItem("WebP: 25-35% better compression than JPEG/PNG")
                    .AddItem("AVIF: Even better compression (but check browser support)")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", absoluteSrc)
                .WithTechnicalMetadata("format", extension)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{absoluteSrc}|IMAGE_LEGACY_FORMAT";
            TrackFinding(findingsMap, key, Severity.Info, "IMAGE_LEGACY_FORMAT",
                $"Image uses legacy format (consider WebP/AVIF for better compression): {absoluteSrc}",
                details);
        }

        // Hotlinking check
        if (isExternal)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {absoluteSrc}")
                .AddItem("🌐 External image (hotlinked)")
                .BeginNested("⚠️ Risks")
                    .AddItem("External images may break if source changes")
                    .AddItem("No control over availability or performance")
                    .AddItem("May violate licensing terms")
                .EndNested()
                .BeginNested("💡 Recommendations")
                    .AddItem("Host images on your own domain")
                    .AddItem("Or use a CDN you control")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", absoluteSrc)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{absoluteSrc}|IMAGE_EXTERNAL_HOTLINK";
            TrackFinding(findingsMap, key, Severity.Info, "IMAGE_EXTERNAL_HOTLINK",
                $"Image hotlinked from external source: {absoluteSrc}",
                details);
        }

        // SVG with pixel dimensions
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (width.HasValue || height.HasValue)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"SVG image: {absoluteSrc}")
                    .AddItem($"Has pixel dimensions: {width} × {height}")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Remove pixel dimensions from SVG images")
                        .AddItem("Use CSS for sizing instead")
                        .AddItem("SVGs are vector graphics and scale infinitely")
                    .EndNested()
                    .WithTechnicalMetadata("imageUrl", absoluteSrc)
                    .WithTechnicalMetadata("width", width)
                    .WithTechnicalMetadata("height", height)
                    .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                    .Build();
                
                var key = $"{absoluteSrc}|SVG_WITH_PIXEL_DIMENSIONS|{width}|{height}";
                TrackFinding(findingsMap, key, Severity.Info, "SVG_WITH_PIXEL_DIMENSIONS",
                    $"SVG image has pixel dimensions (should use CSS for scalability): {absoluteSrc}",
                    details);
            }
        }
    }

    private void AnalyzeAltText(UrlContext ctx, string imageUrl, string alt, string title, int? width, int? height, Dictionary<string, FindingTracker> findingsMap)
    {
        // Check for missing alt attribute
        if (string.IsNullOrWhiteSpace(alt))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Dimensions: {width} × {height}")
                .AddItem("❌ Missing alt text")
                .BeginNested("♿ Accessibility Impact")
                    .AddItem("Screen readers cannot describe this image")
                    .AddItem("Required for WCAG compliance")
                .EndNested()
                .BeginNested("🔍 SEO Impact")
                    .AddItem("Proper alt text helps images rank in Google Image Search")
                    .AddItem("Image search drives significant traffic to websites")
                    .AddItem("Alt text helps search engines understand image content")
                .EndNested()
                .BeginNested("💡 Recommendations")
                    .AddItem("Add descriptive alt text for accessibility and SEO")
                    .AddItem("Use alt=\"\" only for purely decorative images")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .WithTechnicalMetadata("width", width)
                .WithTechnicalMetadata("height", height)
                .Build();
            
            var key = $"{imageUrl}|MISSING_ALT_TEXT";
            TrackFinding(findingsMap, key, Severity.Warning, "MISSING_ALT_TEXT",
                $"Image missing alt text: {imageUrl}",
                details);
            return;
        }

        // Check alt text quality
        if (alt.Length < MIN_ALT_TEXT_LENGTH && !IsLikelyDecorativeAlt(alt))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Alt text: \"{alt}\" ({alt.Length} chars)")
                .BeginNested("💡 Recommendations")
                    .AddItem("Alt text should be descriptive (at least 5 characters)")
                    .AddItem("Describe what the image shows or its purpose")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("altText", alt)
                .WithTechnicalMetadata("length", alt.Length)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{imageUrl}|ALT_TEXT_TOO_SHORT|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "ALT_TEXT_TOO_SHORT",
                $"Alt text is very short ({alt.Length} chars): \"{alt}\"",
                details);
        }
        else if (alt.Length > MAX_ALT_TEXT_LENGTH)
        {
            var preview = alt.Length > 50 ? alt.Substring(0, 50) + "..." : alt;
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Alt text: \"{preview}\" ({alt.Length} chars)")
                .AddItem($"Recommended: Under {MAX_ALT_TEXT_LENGTH} characters")
                .BeginNested("💡 Recommendations")
                    .AddItem("Keep alt text concise and descriptive")
                    .AddItem("Focus on the most important details")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("altText", alt)
                .WithTechnicalMetadata("length", alt.Length)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var altKey = alt.Length > 100 ? alt.Substring(0, 100) : alt;
            var key = $"{imageUrl}|ALT_TEXT_TOO_LONG|{altKey}";
            TrackFinding(findingsMap, key, Severity.Warning, "ALT_TEXT_TOO_LONG",
                $"Alt text is too long ({alt.Length} chars, recommend <{MAX_ALT_TEXT_LENGTH}): \"{preview}\"",
                details);
        }

        // Check for decorative images that should have empty alt
        if (IsLikelyDecorative(imageUrl, alt))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Alt text: \"{alt}\"")
                .AddItem("🎨 Appears to be decorative")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use alt=\"\" for purely decorative images")
                    .AddItem("This tells screen readers to skip the image")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("altText", alt)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{imageUrl}|POTENTIALLY_DECORATIVE|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "POTENTIALLY_DECORATIVE",
                $"Image appears decorative but has alt text (consider alt=\"\"): {imageUrl}",
                details);
        }

        // Check for redundant title attribute
        if (!string.IsNullOrWhiteSpace(title) && title.Equals(alt, StringComparison.OrdinalIgnoreCase))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Alt = Title = \"{alt}\"")
                .AddItem("ℹ️ Title duplicates alt text")
                .BeginNested("💡 Recommendations")
                    .AddItem("Remove redundant title attribute")
                    .AddItem("Or make title provide additional context")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("altText", alt)
                .WithTechnicalMetadata("title", title)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{imageUrl}|REDUNDANT_TITLE_ATTRIBUTE|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "REDUNDANT_TITLE_ATTRIBUTE",
                $"Image title attribute duplicates alt text: {imageUrl}",
                details);
        }

        // Check for common bad patterns
        if (Regex.IsMatch(alt, @"^(image|picture|photo|img|graphic)(\s+of)?", RegexOptions.IgnoreCase))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Image: {imageUrl}")
                .AddItem($"Alt text: \"{alt}\"")
                .AddItem("⚠️ Starts with redundant word (image/picture/photo)")
                .BeginNested("💡 Recommendations")
                    .AddItem("Screen readers already announce it's an image")
                    .AddItem("Start with the actual description")
                    .AddItem($"Example: Instead of \"Image of sunset\", use \"Sunset over ocean\"")
                .EndNested()
                .WithTechnicalMetadata("imageUrl", imageUrl)
                .WithTechnicalMetadata("altText", alt)
                .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                .Build();
            
            var key = $"{imageUrl}|ALT_TEXT_BAD_PATTERN|{alt}";
            TrackFinding(findingsMap, key, Severity.Info, "ALT_TEXT_BAD_PATTERN",
                $"Alt text starts with redundant word (image/picture/photo): \"{alt}\"",
                details);
        }
    }

    private async Task AnalyzeDataUriAsync(UrlContext ctx, string dataUri, Dictionary<string, FindingTracker> findingsMap)
    {
        try
        {
            // Estimate size of data URI (base64 encoding inflates by ~33%)
            var estimatedSize = (dataUri.Length * 3) / 4;
            var sizeKB = estimatedSize / 1024;

            if (sizeKB > MAX_DATA_URI_SIZE_KB)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Data URI size: {sizeKB}KB")
                    .AddItem($"Recommended: Under {MAX_DATA_URI_SIZE_KB}KB")
                    .BeginNested("⚠️ Impact")
                        .AddItem("Large data URIs bloat HTML size")
                        .AddItem("Cannot be cached separately")
                        .AddItem("Increases page load time")
                    .EndNested()
                    .BeginNested("💡 Recommendations")
                        .AddItem("Use external image files instead")
                        .AddItem("Enable browser caching for better performance")
                    .EndNested()
                    .WithTechnicalMetadata("sizeKB", sizeKB)
                    .WithTechnicalMetadata("pageUrl", ctx.Url.ToString())
                    .Build();
                
                // Use hash of data URI for deduplication (data URI itself is too long for key)
                var dataUriHash = dataUri.GetHashCode().ToString();
                var key = $"datauri_{dataUriHash}|LARGE_DATA_URI";
                TrackFinding(findingsMap, key, Severity.Warning, "LARGE_DATA_URI",
                    $"Large data URI embedded in HTML ({sizeKB}KB, recommend <{MAX_DATA_URI_SIZE_KB}KB)",
                    details);
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
    private void TrackFinding(Dictionary<string, FindingTracker> findingsMap, string key, Severity severity, string code, string message, FindingDetails? data)
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
                Data = data,
                OccurrenceCount = 1
            };
        }
    }

    /// <summary>
    /// Report all unique findings with occurrence counts to the findings sink.
    /// </summary>
    private async Task ReportUniqueFindings(UrlContext ctx, Dictionary<string, FindingTracker> findingsMap)
    {
        foreach (var kvp in findingsMap.Values)
        {
            var tracker = kvp;
            
            // Add occurrence count to the message if > 1
            var message = tracker.Message;
            if (tracker.OccurrenceCount > 1)
            {
                message += $" (occurs {tracker.OccurrenceCount} times on this page)";
            }

            // Add occurrence count to finding details if there are duplicates
            var details = tracker.Data;
            if (details != null && tracker.OccurrenceCount > 1)
            {
                // Add occurrence count to technical metadata
                details.TechnicalMetadata ??= new Dictionary<string, object?>();
                details.TechnicalMetadata["occurrenceCount"] = tracker.OccurrenceCount;
            }

            await ctx.Findings.ReportAsync(
                Key,
                tracker.Severity,
                tracker.Code,
                message,
                details);
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

