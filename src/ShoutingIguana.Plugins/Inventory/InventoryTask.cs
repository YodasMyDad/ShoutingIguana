using HtmlAgilityPack;
using ShoutingIguana.PluginSdk;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Inventory;

/// <summary>
/// Tracks indexability, URL structure, and orphaned pages.
/// </summary>
public class InventoryTask : UrlTaskBase
{
    private const int MAX_URL_LENGTH = 100;
    private const int WARNING_URL_LENGTH = 75;
    private const int MAX_QUERY_PARAMETERS = 3;
    private const int MIN_CONTENT_LENGTH = 1800; // ~300 words - modern SEO minimum for ranking
    private const int WARNING_CONTENT_LENGTH = 900; // ~150 words - warning threshold

    public override string Key => "Inventory";
    public override string DisplayName => "Inventory";
    public override string Description => "Tracks URL structure, page depth, and content quality across your site";
    public override int Priority => 10; // Run first

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Report HTTP errors and restricted pages
        if (ctx.Metadata.StatusCode >= 400)
        {
            var statusCode = ctx.Metadata.StatusCode;
            var isRestricted = statusCode == 401 || statusCode == 403 || statusCode == 451;
            
            if (isRestricted)
            {
                // Restricted pages - informational only
                var restrictionType = statusCode switch
                {
                    401 => "Authentication Required",
                    403 => "Forbidden/Restricted",
                    451 => "Unavailable For Legal Reasons",
                    _ => "Restricted"
                };
                
                var note = statusCode switch
                {
                    401 => "This page requires authentication/login to access",
                    403 => "This page is restricted and access is forbidden",
                    451 => "This page is unavailable for legal reasons",
                    _ => "This page has restricted access"
                };
                
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"URL: {ctx.Url}")
                    .AddItem($"HTTP Status: {statusCode}")
                    .AddItem($"Page Depth: {ctx.Metadata.Depth}")
                    .AddItem($"ℹ️ {note}")
                    .BeginNested("💡 Note")
                        .AddItem("If this is expected (e.g., members-only area), this is not an error")
                        .AddItem("Otherwise, check access permissions")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("status", statusCode)
                    .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    $"HTTP_{statusCode}",
                    $"HTTP {statusCode} - {restrictionType}",
                    details);
            }
            else
            {
                // Actual errors
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"URL: {ctx.Url}")
                    .AddItem($"HTTP Status: {statusCode}")
                    .AddItem($"Page Depth: {ctx.Metadata.Depth}")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Investigate why this page returns an error")
                        .AddItem("Fix the page or implement a 301 redirect")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("status", statusCode)
                    .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    $"HTTP_{statusCode}",
                    $"HTTP error {statusCode}",
                    details);
            }
        }
        
        // Analyze successful pages
        if (ctx.Metadata.StatusCode >= 200 && ctx.Metadata.StatusCode < 300)
        {
            await AnalyzeUrlStructureAsync(ctx);
            await AnalyzeIndexabilityAsync(ctx);
            await AnalyzeContentQualityAsync(ctx);
        }
    }

    private async Task AnalyzeUrlStructureAsync(UrlContext ctx)
    {
        var url = ctx.Url.ToString();
        // Calculate URL length WITHOUT query string (SEO best practice)
        var urlWithoutQuery = ctx.Url.GetLeftPart(UriPartial.Path);
        var urlLength = urlWithoutQuery.Length;

        // Check URL length
        if (urlLength > MAX_URL_LENGTH)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"URL length: {urlLength} characters")
                .AddItem($"Recommended: Under {MAX_URL_LENGTH} characters")
                .BeginNested("💡 Recommendations")
                    .AddItem("Shorter URLs are easier to share and remember")
                    .AddItem("They may also rank slightly better in search results")
                .WithTechnicalMetadata("url", url)
                .WithTechnicalMetadata("length", urlLength)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "URL_TOO_LONG",
                $"URL is {urlLength} characters (recommend <{MAX_URL_LENGTH})",
                details);
        }
        else if (urlLength > WARNING_URL_LENGTH)
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> {
                    ["url"] = url,
                    ["length"] = urlLength
                },
                $"URL length: {urlLength} characters",
                $"Recommended: Under {WARNING_URL_LENGTH} characters");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "URL_LONG",
                $"URL is {urlLength} characters (consider shortening)",
                details);
        }

        // Check for uppercase in URL
        if (url != url.ToLowerInvariant())
        {
            var uppercaseParts = Regex.Matches(url, "[A-Z]+").Select(m => m.Value).ToArray();
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Uppercase characters found: {string.Join(", ", uppercaseParts)}")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use lowercase URLs to avoid case-sensitivity issues")
                    .AddItem("Some servers treat /Page and /page as different resources")
                .WithTechnicalMetadata("url", url)
                .WithTechnicalMetadata("uppercaseParts", uppercaseParts)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "UPPERCASE_IN_URL",
                "URL contains uppercase characters (case-sensitivity risk)",
                details);
        }

        // Check query parameters
        if (!string.IsNullOrEmpty(ctx.Url.Query))
        {
            var queryParams = ctx.Url.Query.TrimStart('?').Split('&');
            if (queryParams.Length > MAX_QUERY_PARAMETERS)
            {
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Query parameters: {queryParams.Length}")
                    .AddItem($"Recommended: {MAX_QUERY_PARAMETERS} or fewer");
                
                builder.BeginNested("📊 Parameters");
                foreach (var param in queryParams.Take(5))
                {
                    builder.AddItem(param);
                }
                if (queryParams.Length > 5)
                {
                    builder.AddItem($"... and {queryParams.Length - 5} more");
                }
                
                builder.WithTechnicalMetadata("url", url)
                    .WithTechnicalMetadata("parameterCount", queryParams.Length)
                    .WithTechnicalMetadata("parameters", queryParams);
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "TOO_MANY_PARAMETERS",
                    $"URL has {queryParams.Length} query parameters (recommend <{MAX_QUERY_PARAMETERS})",
                    builder.Build());
            }

            // Check for pagination parameters
            var paginationPattern = @"[?&](page|p|pg|offset|start)=";
            if (Regex.IsMatch(url, paginationPattern, RegexOptions.IgnoreCase))
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem("This appears to be a pagination page")
                    .BeginNested("💡 Best Practices")
                        .AddItem("Consider using rel=prev/next link tags")
                        .AddItem("Use canonical tags to avoid duplicate content issues")
                    .WithTechnicalMetadata("url", url)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "PAGINATION_DETECTED",
                    "URL appears to be a pagination page",
                    details);
            }
        }

        // Check for special characters
        var specialCharsPattern = @"[^a-zA-Z0-9\-_.~:/?#\[\]@!$&'()*+,;=%]";
        if (Regex.IsMatch(url, specialCharsPattern))
        {
            var specialChars = Regex.Matches(url, specialCharsPattern)
                .Select(m => m.Value)
                .Distinct()
                .ToArray();
            
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> {
                    ["url"] = url,
                    ["specialChars"] = specialChars
                },
                $"Non-standard characters: {string.Join(", ", specialChars)}",
                "⚠️ May cause encoding issues in some contexts");
                
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SPECIAL_CHARS_IN_URL",
                "URL contains non-standard characters",
                details);
        }

        // Check for underscores (Google treats them differently than hyphens)
        if (ctx.Url.AbsolutePath.Contains('_'))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("URL contains underscores")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use hyphens (-) instead of underscores (_) for word separation")
                    .AddItem("Google treats hyphens as word separators but underscores as word connectors")
                .WithTechnicalMetadata("url", url)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "UNDERSCORES_IN_URL",
                "URL contains underscores (hyphens are preferred for SEO)",
                details);
        }
    }

    private async Task AnalyzeIndexabilityAsync(UrlContext ctx)
    {
        // Use crawler-parsed robots data
        if (ctx.Metadata.RobotsNoindex == true)
        {
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? 
                "X-Robots-Tag header" : "meta robots tag";
            
            if (ctx.Metadata.HasRobotsConflict)
            {
                source = "conflicting meta and X-Robots-Tag (most restrictive applied)";
            }
            
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Source: {source}")
                .AddItem("⚠️ This page will not appear in search engine results")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .WithTechnicalMetadata("hasConflict", ctx.Metadata.HasRobotsConflict)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NOINDEX_DIRECTIVE",
                $"Page has noindex directive (will not be indexed) - Source: {source}",
                details);
        }

        // Use crawler-parsed canonical data
        var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;
        if (!string.IsNullOrEmpty(canonical))
        {
            var normalizedCurrent = NormalizeUrl(ctx.Url.ToString());
            var normalizedCanonical = NormalizeUrl(canonical);

            if (normalizedCurrent != normalizedCanonical)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Current URL: {ctx.Url}")
                    .AddItem($"Canonical URL: {canonical}")
                    .AddItem($"Cross-domain: {(ctx.Metadata.HasCrossDomainCanonical ? "Yes" : "No")}")
                    .BeginNested("ℹ️ Impact")
                        .AddItem("This page defers indexing to the canonical URL")
                        .AddItem("Search engines will index the canonical URL instead")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("canonical", canonical)
                    .WithTechnicalMetadata("isCrossDomain", ctx.Metadata.HasCrossDomainCanonical)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "CANONICALIZED_PAGE",
                    "Page canonical points elsewhere (will not be indexed)",
                    details);
            }
        }
    }

    private async Task AnalyzeContentQualityAsync(UrlContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
            return;

        // Check for thin content
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);

        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        if (bodyNode != null)
        {
            var bodyText = bodyNode.InnerText ?? "";
            var contentLength = bodyText.Trim().Length;
            var estimatedWords = contentLength / 6; // Rough estimate: 6 chars per word average

            // Check content length thresholds in order: most severe first
            if (contentLength < WARNING_CONTENT_LENGTH)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Content length: {contentLength} characters (~{estimatedWords} words)")
                    .AddItem($"Recommended: At least {MIN_CONTENT_LENGTH} characters (~300 words)")
                    .BeginNested("⚠️ SEO Impact")
                        .AddItem("Thin content rarely ranks in modern search engines")
                        .AddItem("Google's 'Helpful Content Update' favors comprehensive content")
                        .AddItem("Competitive keywords typically need 1,000-2,000+ words")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Add substantive, unique content that thoroughly covers the topic")
                        .AddItem("Include relevant details, examples, and explanations")
                        .AddItem("Focus on user value and answering search intent completely")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("contentLength", contentLength)
                    .WithTechnicalMetadata("estimatedWords", estimatedWords)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "THIN_CONTENT",
                    $"Page has thin content ({estimatedWords} words, recommend 300+ for ranking)",
                    details);
            }
            else if (contentLength < MIN_CONTENT_LENGTH)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Content length: {contentLength} characters (~{estimatedWords} words)")
                    .AddItem($"Minimum met but could be improved")
                    .BeginNested("ℹ️ Note")
                        .AddItem("While technically adequate, more content often ranks better")
                        .AddItem("Consider expanding to 300-500+ words for competitive topics")
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("contentLength", contentLength)
                    .WithTechnicalMetadata("estimatedWords", estimatedWords)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "LIMITED_CONTENT",
                    $"Page has limited content ({estimatedWords} words, consider expanding for better rankings)",
                    details);
            }
        }
    }

    private string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        }
        catch
        {
            return url.TrimEnd('/').ToLowerInvariant();
        }
    }
}

