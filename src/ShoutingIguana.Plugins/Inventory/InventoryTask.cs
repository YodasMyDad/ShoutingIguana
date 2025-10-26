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
    private const int MIN_CONTENT_LENGTH = 500;

    public override string Key => "Inventory";
    public override string DisplayName => "URL Inventory";
    public override int Priority => 10; // Run first

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Report HTTP errors
        if (ctx.Metadata.StatusCode >= 400)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                $"HTTP_{ctx.Metadata.StatusCode}",
                $"HTTP error {ctx.Metadata.StatusCode}",
                new
                {
                    url = ctx.Url.ToString(),
                    status = ctx.Metadata.StatusCode,
                    depth = ctx.Metadata.Depth
                });
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
        var urlLength = url.Length;

        // Check URL length
        if (urlLength > MAX_URL_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "URL_TOO_LONG",
                $"URL is {urlLength} characters (recommend <{MAX_URL_LENGTH})",
                new
                {
                    url,
                    length = urlLength,
                    recommendation = "Shorter URLs are easier to share and may rank better"
                });
        }
        else if (urlLength > WARNING_URL_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "URL_LONG",
                $"URL is {urlLength} characters (consider shortening)",
                new
                {
                    url,
                    length = urlLength
                });
        }

        // Check for uppercase in URL
        if (url != url.ToLowerInvariant())
        {
            var uppercaseParts = Regex.Matches(url, "[A-Z]+").Cast<Match>().Select(m => m.Value).ToArray();
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "UPPERCASE_IN_URL",
                "URL contains uppercase characters (case-sensitivity risk)",
                new
                {
                    url,
                    uppercaseParts,
                    recommendation = "Use lowercase URLs to avoid case-sensitivity issues"
                });
        }

        // Check query parameters
        if (!string.IsNullOrEmpty(ctx.Url.Query))
        {
            var queryParams = ctx.Url.Query.TrimStart('?').Split('&');
            if (queryParams.Length > MAX_QUERY_PARAMETERS)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "TOO_MANY_PARAMETERS",
                    $"URL has {queryParams.Length} query parameters (recommend <{MAX_QUERY_PARAMETERS})",
                    new
                    {
                        url,
                        parameterCount = queryParams.Length,
                        parameters = queryParams.Take(5).ToArray()
                    });
            }

            // Check for pagination parameters
            var paginationPattern = @"[?&](page|p|pg|offset|start)=";
            if (Regex.IsMatch(url, paginationPattern, RegexOptions.IgnoreCase))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "PAGINATION_DETECTED",
                    "URL appears to be a pagination page",
                    new
                    {
                        url,
                        note = "Consider using rel=prev/next links and canonical tags"
                    });
            }
        }

        // Check for special characters
        var specialCharsPattern = @"[^a-zA-Z0-9\-_.~:/?#\[\]@!$&'()*+,;=%]";
        if (Regex.IsMatch(url, specialCharsPattern))
        {
            var specialChars = Regex.Matches(url, specialCharsPattern)
                .Cast<Match>()
                .Select(m => m.Value)
                .Distinct()
                .ToArray();
                
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SPECIAL_CHARS_IN_URL",
                "URL contains non-standard characters",
                new
                {
                    url,
                    specialChars,
                    note = "May cause encoding issues in some contexts"
                });
        }

        // Check for underscores (Google treats them differently than hyphens)
        if (ctx.Url.AbsolutePath.Contains('_'))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "UNDERSCORES_IN_URL",
                "URL contains underscores (hyphens are preferred for SEO)",
                new
                {
                    url,
                    recommendation = "Use hyphens (-) instead of underscores (_) for word separation"
                });
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
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NOINDEX_DIRECTIVE",
                $"Page has noindex directive (will not be indexed) - Source: {source}",
                new
                {
                    url = ctx.Url.ToString(),
                    xRobotsTag = ctx.Metadata.XRobotsTag,
                    hasConflict = ctx.Metadata.HasRobotsConflict
                });
        }

        // Use crawler-parsed canonical data
        var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;
        if (!string.IsNullOrEmpty(canonical))
        {
            var normalizedCurrent = NormalizeUrl(ctx.Url.ToString());
            var normalizedCanonical = NormalizeUrl(canonical);

            if (normalizedCurrent != normalizedCanonical)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "CANONICALIZED_PAGE",
                    "Page canonical points elsewhere (will not be indexed)",
                    new
                    {
                        url = ctx.Url.ToString(),
                        canonical,
                        isCrossDomain = ctx.Metadata.HasCrossDomainCanonical,
                        note = "This page defers indexing to the canonical URL"
                    });
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

            if (contentLength < MIN_CONTENT_LENGTH)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "THIN_CONTENT",
                    $"Page has very little content ({contentLength} chars, recommend >{MIN_CONTENT_LENGTH})",
                    new
                    {
                        url = ctx.Url.ToString(),
                        contentLength,
                        recommendation = "Thin content pages may not rank well"
                    });
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

