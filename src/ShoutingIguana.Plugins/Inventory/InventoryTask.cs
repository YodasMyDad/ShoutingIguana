using System;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

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
                
                // NOTE: HTTP error checks are duplicates of BrokenLinks plugin
                // Skipping to avoid duplicate reporting
            }
        }
        
        // Analyze successful pages
        if (ctx.Metadata.StatusCode >= 200 && ctx.Metadata.StatusCode < 300)
        {
            await AnalyzeUrlStructureAsync(ctx);
            await AnalyzeIndexabilityAsync(ctx);
            await AnalyzeContentQualityAsync(ctx);
            await CheckThirdPartyResourcesAsync(ctx);
        }
    }

    private async Task AnalyzeUrlStructureAsync(UrlContext ctx)
    {
        // Only analyze HTML pages for SEO-related URL structure issues
        // Skip JS, CSS, images, and other static resources
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }
        
        var url = ctx.Url.ToString();
        
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, url))
        {
            return;
        }
        // Calculate URL length WITHOUT query string (SEO best practice)
        var urlWithoutQuery = ctx.Url.GetLeftPart(UriPartial.Path);
        var urlLength = urlWithoutQuery.Length;

        // Check URL length
        if (urlLength > MAX_URL_LENGTH)
        {
            await ReportIssueAsync(
                ctx,
                "URL length > 100 characters (excluding query)",
                "Warning",
                "Search engines and visitors prefer short, descriptive URLs; keep this URL under roughly 100 characters (excluding the query) for clarity.");
        }
        else if (urlLength > WARNING_URL_LENGTH)
        {
            await ReportIssueAsync(
                ctx,
                "URL length > 75 characters (excluding query)",
                "Info",
                "This URL is getting long; consider trimming optional segments so it stays easy to read and share.");
        }

        // Check for uppercase in URL
        if (url != url.ToLowerInvariant())
        {
            await ReportIssueAsync(
                ctx,
                "Uppercase characters detected in URL",
                "Warning",
                "Uppercase characters can lead to duplicate URLs on case-sensitive servers; keep the path lowercase to maintain consistency.");
        }

        // Check query parameters
        if (!string.IsNullOrEmpty(ctx.Url.Query))
        {
            var queryParams = ctx.Url.Query.TrimStart('?').Split('&');
            if (queryParams.Length > MAX_QUERY_PARAMETERS)
            {
                await ReportIssueAsync(
                    ctx,
                    $"Too many query parameters ({queryParams.Length})",
                    "Info",
                    $"Having {queryParams.Length} query parameters can produce many near-duplicate URLs; reduce optional parameters or canonicalize to keep indexing clean.");
            }

            // Check for pagination parameters
            var paginationPattern = @"[?&](page|p|pg|offset|start)=";
            if (Regex.IsMatch(url, paginationPattern, RegexOptions.IgnoreCase))
            {
                await ReportIssueAsync(
                    ctx,
                    "Pagination parameter detected in URL",
                    "Info",
                    "Pagination parameters (page, offset, start, etc.) create extra URLs; use rel=\"next\"/\"prev\" or canonical tags to signal the canonical entry point.");
            }
            
            // Check for session IDs in URL (CRITICAL SEO ISSUE)
            await CheckSessionIdsAsync(ctx, url);
        }

        // Check for special characters
        var specialCharsPattern = @"[^a-zA-Z0-9\-_.~:/?#\[\]@!$&'()*+,;=%]";
        if (Regex.IsMatch(url, specialCharsPattern))
        {
            await ReportIssueAsync(
                ctx,
                "Special characters found in URL",
                "Info",
                "Characters outside the safe URL set may be percent-encoded inconsistently; keep the path simple with alphanumerics, hyphens, and dots.");
        }

        // Check for underscores (Google treats them differently than hyphens)
        if (ctx.Url.AbsolutePath.Contains('_'))
        {
            await ReportIssueAsync(
                ctx,
                "Underscore detected in path",
                "Info",
                "Google treats hyphens as word separators and underscores as literal characters; replace underscores with hyphens for better readability.");
        }
    }

    /// <summary>
    /// Check for session IDs in URLs - a critical SEO issue causing infinite duplicate content.
    /// </summary>
    private async Task CheckSessionIdsAsync(UrlContext ctx, string url)
    {
        // Common session ID patterns across different platforms
        var sessionIdPatterns = new[]
        {
            // Java
            (Pattern: @"[?&;]jsessionid=", Name: "JSESSIONID (Java)", Example: "jsessionid=ABC123"),
            // PHP
            (Pattern: @"[?&]PHPSESSID=", Name: "PHPSESSID (PHP)", Example: "PHPSESSID=ABC123"),
            (Pattern: @"[?&]sid=", Name: "SID (PHP)", Example: "sid=ABC123"),
            // ASP.NET
            (Pattern: @"[?&;\(]ASPSESSIONID[A-Z]{8}=", Name: "ASPSESSIONID (ASP.NET)", Example: "ASPSESSIONID=ABC123"),
            (Pattern: @"\(S\([a-z0-9]{24}\)\)", Name: "ASP.NET Cookieless Session", Example: "(S(abc123xyz))"),
            // ColdFusion
            (Pattern: @"[?&]CFID=", Name: "CFID (ColdFusion)", Example: "CFID=123456"),
            (Pattern: @"[?&]CFTOKEN=", Name: "CFTOKEN (ColdFusion)", Example: "CFTOKEN=ABC123"),
            // Generic
            (Pattern: @"[?&]sessionid=", Name: "SESSIONID (Generic)", Example: "sessionid=ABC123"),
            (Pattern: @"[?&]session_id=", Name: "SESSION_ID (Generic)", Example: "session_id=ABC123"),
            (Pattern: @"[?&]sess=", Name: "SESS (Generic)", Example: "sess=ABC123"),
        };

        foreach (var (Pattern, Name, Example) in sessionIdPatterns)
        {
            if (Regex.IsMatch(url, Pattern, RegexOptions.IgnoreCase))
            {
                await ReportIssueAsync(
                    ctx,
                    $"{Name} parameter detected ({Example})",
                    "Error",
                    "Session identifiers in the URL create infinite near-duplicate pages; store session state in cookies or headers so the URL remains canonical.",
                    indexable: false);
                
                // Only report once per URL (first match found)
                return;
            }
        }
    }

    private async Task AnalyzeIndexabilityAsync(UrlContext ctx)
    {
        // Only analyze HTML pages for SEO-related indexability issues
        // Skip JS, CSS, images, and other static resources
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }
        
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }
        
        // Use crawler-parsed robots data
        if (ctx.Metadata.RobotsNoindex == true)
        {
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? 
                "X-Robots-Tag header" : "meta robots tag";
            
            if (ctx.Metadata.HasRobotsConflict)
            {
                source = "conflicting meta and X-Robots-Tag (most restrictive applied)";
            }
            
            // NOTE: Noindex and canonical checks are duplicates
            // Already handled by Robots and Canonical plugins
            // Skipping to avoid duplicate reporting
        }
    }

    private async Task AnalyzeContentQualityAsync(UrlContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
            return;

        // Only analyze HTML pages for SEO-related content quality issues
        // Skip JS, CSS, images, and other static resources
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }
        
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

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
                await ReportIssueAsync(
                    ctx,
                    $"Very short content (<{WARNING_CONTENT_LENGTH} characters)",
                    "Warning",
                    "This page contains less than ~150 words, which search engines often consider thin content; add more helpful detail.");
            }
            else if (contentLength < MIN_CONTENT_LENGTH)
            {
                await ReportIssueAsync(
                    ctx,
                    $"Limited content length (<{MIN_CONTENT_LENGTH} characters)",
                    "Info",
                    "Content under ~300 words may not fully address the topic; expand it so the page can satisfy user intent.");
            }
        }
    }

    /// <summary>
    /// Check for excessive third-party resources that impact performance
    /// </summary>
    private async Task CheckThirdPartyResourcesAsync(UrlContext ctx)
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
        
        // Only analyze internal URLs
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }
        
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);
        
        var thirdPartyDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heavyThirdPartyScripts = new List<string>();
        var currentDomain = new Uri(ctx.Project.BaseUrl).Host;
        
        // Known heavy third-party services
        var knownHeavyServices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "google-analytics.com", "Google Analytics" },
            { "googletagmanager.com", "Google Tag Manager" },
            { "facebook.net", "Facebook Pixel" },
            { "facebook.com", "Facebook SDK" },
            { "doubleclick.net", "Google Ads" },
            { "googlesyndication.com", "Google AdSense" },
            { "adservice.google.com", "Google Ads" },
            { "analytics.tiktok.com", "TikTok Pixel" },
            { "connect.facebook.net", "Facebook Connect" },
            { "bat.bing.com", "Bing Ads" },
            { "hotjar.com", "Hotjar Analytics" },
            { "mouseflow.com", "Mouseflow Analytics" },
            { "clarity.ms", "Microsoft Clarity" },
            { "fullstory.com", "FullStory Analytics" }
        };
        
        // Check scripts
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
        if (scriptNodes != null)
        {
            foreach (var script in scriptNodes)
            {
                var src = script.GetAttributeValue("src", "");
                if (string.IsNullOrEmpty(src))
                    continue;
                
                if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
                {
                    var domain = uri.Host.ToLowerInvariant();
                    
                    // Skip same-domain resources
                    if (domain.Equals(currentDomain, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    // Skip CDN domains that are just hosting your resources
                    if (domain.Contains(currentDomain.Replace("www.", "")))
                        continue;
                    
                    thirdPartyDomains.Add(domain);
                    
                    // Check if it's a known heavy service
                    foreach (var kvp in knownHeavyServices)
                    {
                        if (domain.Contains(kvp.Key))
                        {
                            if (!heavyThirdPartyScripts.Contains(kvp.Value))
                            {
                                heavyThirdPartyScripts.Add(kvp.Value);
                            }
                            break;
                        }
                    }
                }
            }
        }
        
        // Check stylesheets (less impactful but still third-party)
        var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet'][@href]");
        if (linkNodes != null)
        {
            foreach (var link in linkNodes)
            {
                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href))
                    continue;
                
                if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
                {
                    var domain = uri.Host.ToLowerInvariant();
                    
                    if (domain.Equals(currentDomain, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    if (domain.Contains(currentDomain.Replace("www.", "")))
                        continue;
                    
                    thirdPartyDomains.Add(domain);
                }
            }
        }
        
        // Report if excessive third-party domains (> 10)
        if (thirdPartyDomains.Count > 10)
        {
            await ReportIssueAsync(
                ctx,
                $"More than 10 third-party domains ({thirdPartyDomains.Count})",
                "Warning",
                $"Loading {thirdPartyDomains.Count} external domains adds DNS lookups and scripts that slow the page; try to consolidate or drop unnecessary services.");
        }
    }

    private Task ReportIssueAsync(UrlContext ctx, string issue, string severity, string explanation, bool indexable = true)
    {
        var row = ReportRow.Create()
            .Set("Issue", issue)
            .Set("Explanation", explanation)
            .Set("URL", ctx.Url.ToString())
            .Set("ContentType", ctx.Metadata.ContentType ?? "")
            .Set("Status", ctx.Metadata.StatusCode)
            .Set("Depth", ctx.Metadata.Depth)
            .Set("Indexable", indexable ? "Yes" : "No")
            .Set("Severity", severity);

        return ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
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

