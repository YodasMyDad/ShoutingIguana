using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Redirects;

/// <summary>
/// Analyzes redirect chains, loops, and canonicalization issues.
/// </summary>
public class RedirectsTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MAX_REDIRECT_CHAIN_LENGTH = 3;
    private const int WARNING_REDIRECT_CHAIN_LENGTH = 2;
    private const int MAX_CHAIN_HOPS = 5;
    
    // Track redirects per project for chain and loop detection
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, RedirectInfo>> RedirectsByProject = new();

    public override string Key => "Redirects";
    public override string DisplayName => "Redirects";
    public override string Description => "Detects redirect chains, loops, and canonicalization issues";
    public override int Priority => 20;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze internal URLs (external URLs are for BrokenLinks status checking only)
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Check for redirect loop errors that were caught during crawl
            if (ctx.Metadata.IsRedirectLoop)
            {
                await ReportRedirectLoopErrorAsync(ctx);
                return; // No further analysis needed for redirect loops
            }
            
            var statusCode = ctx.Metadata.StatusCode;
            
            // Check if this URL is a redirect
            if (statusCode >= 300 && statusCode < 400)
            {
                var location = ctx.Headers.TryGetValue("location", out var loc) ? loc : null;
                
                // Track redirect for chain/loop detection
                if (!string.IsNullOrEmpty(location))
                {
                    TrackRedirect(ctx.Project.ProjectId, ctx.Url.ToString(), location, statusCode);
                }
                
                await AnalyzeHttpRedirectAsync(ctx);
                
                // NEW: Check for redirect chains and loops
                await CheckRedirectChainsAsync(ctx, location);
                
                // NEW: Validate redirect target status
                await ValidateRedirectTargetAsync(ctx, location);
                
                // NEW: Check redirect caching headers
                await CheckRedirectCachingAsync(ctx, statusCode);
            }
            
            // Check for meta refresh redirects (even on 200 OK pages) - using crawler-parsed data
            if (statusCode == 200 && ctx.Metadata.HasMetaRefresh)
            {
                await ReportMetaRefreshAsync(ctx);
            }
            
            // Check for JavaScript redirects (on any successful page)
            if (statusCode == 200 && !string.IsNullOrEmpty(ctx.RenderedHtml))
            {
                await CheckJavaScriptRedirectAsync(ctx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing redirects for {Url}", ctx.Url);
        }
    }
    
    private void TrackRedirect(int projectId, string fromUrl, string toUrl, int statusCode)
    {
        var projectRedirects = RedirectsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, RedirectInfo>());
        projectRedirects[fromUrl] = new RedirectInfo
        {
            FromUrl = fromUrl,
            ToUrl = toUrl,
            StatusCode = statusCode
        };
    }
    
    private async Task ReportRedirectLoopErrorAsync(UrlContext ctx)
    {
        var row = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", "(Loop)")
            .Set("StatusCode", 0)
            .Set("Issue", "Infinite Redirect Loop (ERR_TOO_MANY_REDIRECTS)")
            .Set("Severity", "Error");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task AnalyzeHttpRedirectAsync(UrlContext ctx)
    {
        var statusCode = ctx.Metadata.StatusCode;
        var location = ctx.Headers.TryGetValue("location", out var loc) ? loc : null;
        
        if (string.IsNullOrEmpty(location))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", "")
                .Set("StatusCode", statusCode)
                .Set("Issue", "Missing Location Header")
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            return;
        }

        // Report the redirect
        var redirectRow = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", location)
            .Set("StatusCode", statusCode)
            .Set("Issue", $"{GetRedirectType(statusCode)} Redirect")
            .Set("Severity", "Info");
        
        await ctx.Reports.ReportAsync(Key, redirectRow, ctx.Metadata.UrlId, default);

        // Check for temporary vs permanent redirects
        if (statusCode == 302 || statusCode == 307)
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", location)
                .Set("StatusCode", statusCode)
                .Set("Issue", "Temporary Redirect - Consider 301")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for mixed content (HTTPS -> HTTP)
        if (ctx.Url.Scheme == "https" && location.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", location)
                .Set("StatusCode", statusCode)
                .Set("Issue", "HTTPS to HTTP Redirect (Security Issue)")
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for protocol canonicalization (http -> https)
        if (ctx.Url.Scheme == "http" && location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", location)
                .Set("StatusCode", statusCode)
                .Set("Issue", "HTTP to HTTPS Redirect (Good)")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for www canonicalization
        var fromHost = ctx.Url.Host;
        if (Uri.TryCreate(location, UriKind.Absolute, out var toUri))
        {
            var toHost = toUri.Host;
            
            if (fromHost.StartsWith("www.") && !toHost.StartsWith("www."))
            {
                var row = ReportRow.Create()
                    .Set("Source", ctx.Url.ToString())
                    .Set("Target", location)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "WWW to Non-WWW Redirect")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
            else if (!fromHost.StartsWith("www.") && toHost.StartsWith("www."))
            {
                var row = ReportRow.Create()
                    .Set("Source", ctx.Url.ToString())
                    .Set("Target", location)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "Non-WWW to WWW Redirect")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }

            // Check for trailing slash inconsistencies
            var fromPath = ctx.Url.AbsolutePath;
            var toPath = toUri.AbsolutePath;
            
            if (fromPath.TrimEnd('/') == toPath.TrimEnd('/') && 
                fromPath.EndsWith("/") != toPath.EndsWith("/"))
            {
                var row = ReportRow.Create()
                    .Set("Source", ctx.Url.ToString())
                    .Set("Target", location)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "Trailing Slash Redirect")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task ReportMetaRefreshAsync(UrlContext ctx)
    {
        // Use crawler-parsed meta refresh data
        var delay = ctx.Metadata.MetaRefreshDelay ?? 0;
        var targetUrl = ctx.Metadata.MetaRefreshTarget ?? "unknown";

        var row = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", targetUrl)
            .Set("StatusCode", 0)
            .Set("Issue", $"Meta Refresh Redirect ({delay}s delay)")
            .Set("Severity", "Warning");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task CheckJavaScriptRedirectAsync(UrlContext ctx)
    {
        try
        {
            // Extract only script tag content to avoid false positives from comments, strings, or other HTML
            var scriptContentPattern = @"<script[^>]*>(.*?)</script>";
            var scriptMatches = Regex.Matches(ctx.RenderedHtml!, scriptContentPattern, 
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            if (scriptMatches.Count == 0)
            {
                return; // No script tags found
            }
            
            foreach (Match scriptMatch in scriptMatches)
            {
                var scriptContent = scriptMatch.Groups[1].Value;
                
                // Remove single-line comments (//...)
                scriptContent = Regex.Replace(scriptContent, @"//.*?$", "", RegexOptions.Multiline);
                
                // Remove multi-line comments (/* ... */)
                scriptContent = Regex.Replace(scriptContent, @"/\*.*?\*/", "", RegexOptions.Singleline);
                
                // Look for actual redirect statements (not just mentions of location properties)
                // Pattern explanation:
                // - Looks for assignment or function call patterns
                // - window.location.href = "url" or location.href = "url"
                // - window.location.replace("url") or location.replace("url")
                // - window.location.assign("url") or location.assign("url")
                // - window.location = "url" (direct assignment)
                var redirectPattern = @"(?:window\.)?location(?:\.(?:href|replace|assign))?\s*[=(]\s*[""'`]([^""'`\s]+)[""'`]";
                var redirectMatches = Regex.Matches(scriptContent, redirectPattern, RegexOptions.IgnoreCase);
                
                if (redirectMatches.Count > 0)
                {
                    // Found potential redirect - extract the URL
                    var targetUrl = redirectMatches[0].Groups[1].Value;
                    
                    // Filter out obvious false positives
                    if (IsLikelyRedirectUrl(targetUrl))
                    {
                        var row = ReportRow.Create()
                            .Set("Source", ctx.Url.ToString())
                            .Set("Target", targetUrl)
                            .Set("StatusCode", 0)
                            .Set("Issue", "JavaScript Redirect (Not SEO-Friendly)")
                            .Set("Severity", "Warning");
                        
                        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                        return; // Report only once per page
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for JavaScript redirects on {Url}", ctx.Url);
        }
    }
    
    /// <summary>
    /// Check if the extracted string looks like an actual redirect URL (not a placeholder or variable)
    /// </summary>
    private bool IsLikelyRedirectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length < 3)
            return false;
            
        // Filter out obvious placeholders and variable references
        var lowerUrl = url.ToLowerInvariant();
        
        // Exclude JavaScript-like patterns
        if (lowerUrl.Contains("{{") || lowerUrl.Contains("}}") || // Template placeholders
            lowerUrl.Contains("${") || // Template literals
            lowerUrl.StartsWith("javascript:") || // JavaScript protocol
            lowerUrl == "about:blank" || // Blank page
            lowerUrl.Contains("undefined") || // JavaScript undefined
            lowerUrl.Contains("null") || // JavaScript null
            lowerUrl == "#" || lowerUrl.StartsWith("#/")) // Hash-only or hash routes (SPAs)
        {
            return false;
        }
        
        // Accept URLs that look real: absolute URLs or relative paths
        return url.StartsWith("http://") || 
               url.StartsWith("https://") || 
               url.StartsWith("//") || // Protocol-relative
               url.StartsWith("/") || // Absolute path
               url.StartsWith("./") || // Relative path
               url.Contains(".");  // Contains domain or file extension
    }

    private string GetRedirectType(int statusCode)
    {
        return statusCode switch
        {
            301 => "Permanent",
            302 => "Temporary (Found)",
            303 => "See Other",
            307 => "Temporary (Preserve Method)",
            308 => "Permanent (Preserve Method)",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Check for redirect chains and loops
    /// </summary>
    private async Task CheckRedirectChainsAsync(UrlContext ctx, string? initialTarget)
    {
        if (string.IsNullOrEmpty(initialTarget))
        {
            return;
        }
        
        var projectId = ctx.Project.ProjectId;
        
        if (!RedirectsByProject.TryGetValue(projectId, out var projectRedirects))
        {
            return;
        }
        
        // Build the complete redirect chain
        var chain = new List<RedirectInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ctx.Url.ToString();
        
        chain.Add(new RedirectInfo
        {
            FromUrl = current,
            ToUrl = initialTarget,
            StatusCode = ctx.Metadata.StatusCode
        });
        visited.Add(current.ToLowerInvariant());
        
        // Follow the chain up to MAX_CHAIN_HOPS
        current = initialTarget;
        for (int i = 0; i < MAX_CHAIN_HOPS; i++)
        {
            if (projectRedirects.TryGetValue(current, out var redirectInfo))
            {
                // Check for loop
                if (visited.Contains(current.ToLowerInvariant()))
                {
                    chain.Add(redirectInfo);
                    
                    var chainString = string.Join(" → ", chain.Select(r => $"{r.FromUrl} ({r.StatusCode})"));
                    var row = ReportRow.Create()
                        .Set("Source", ctx.Url.ToString())
                        .Set("Target", chainString)
                        .Set("StatusCode", 0)
                        .Set("Issue", $"Redirect Loop ({chain.Count} hops)")
                        .Set("Severity", "Error");
                    
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                    return;
                }
                
                chain.Add(redirectInfo);
                visited.Add(current.ToLowerInvariant());
                current = redirectInfo.ToUrl;
            }
            else
            {
                // End of chain
                break;
            }
        }
        
        // Report chain if longer than 1 hop
        if (chain.Count > 1)
        {
            var severityStr = chain.Count >= 3 ? "Error" : "Warning";
            var finalTarget = chain[^1].ToUrl;
            
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", finalTarget)
                .Set("StatusCode", ctx.Metadata.StatusCode)
                .Set("Issue", $"Redirect Chain ({chain.Count} hops)")
                .Set("Severity", severityStr);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }
    
    /// <summary>
    /// Validate redirect target status
    /// </summary>
    private Task ValidateRedirectTargetAsync(UrlContext ctx, string? targetUrl)
    {
        if (string.IsNullOrEmpty(targetUrl))
        {
            return Task.CompletedTask;
        }
        
        var projectId = ctx.Project.ProjectId;
        
        if (!RedirectsByProject.TryGetValue(projectId, out var projectRedirects))
        {
            return Task.CompletedTask;
        }
        
        // Check if target URL has been crawled and what its status is
        // Look for it in our redirect tracking or mark for future validation
        if (projectRedirects.TryGetValue(targetUrl, out var targetRedirect))
        {
            // Target is also a redirect - will be caught by chain detection
            return Task.CompletedTask;
        }
        
        // Note: Full validation would require querying the URL repository
        // For now, we log that we should check this
        _logger.LogDebug("Redirect target {Target} should be validated for status", targetUrl);
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Check redirect caching headers
    /// </summary>
    private async Task CheckRedirectCachingAsync(UrlContext ctx, int statusCode)
    {
        // Check Cache-Control header
        if (ctx.Headers.TryGetValue("cache-control", out var cacheControl))
        {
            var maxAgeMatch = Regex.Match(cacheControl, @"max-age=(\d+)", RegexOptions.IgnoreCase);
            
            if (maxAgeMatch.Success && int.TryParse(maxAgeMatch.Groups[1].Value, out var maxAge))
            {
                // Warn about long-cached temporary redirects
                if ((statusCode == 302 || statusCode == 307) && maxAge > 86400) // > 1 day
                {
                    var days = maxAge / 86400;
                    var location = ctx.Headers.TryGetValue("location", out var loc) ? loc : "";
                    
                    var row = ReportRow.Create()
                        .Set("Source", ctx.Url.ToString())
                        .Set("Target", location)
                        .Set("StatusCode", statusCode)
                        .Set("Issue", $"Temporary Redirect with Long Cache ({days} days)")
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                }
            }
        }
    }
    
    /// <summary>
    /// Cleanup per-project data when project is closed
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        RedirectsByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up redirect data for project {ProjectId}", projectId);
    }
    
    private class RedirectInfo
    {
        public string FromUrl { get; set; } = string.Empty;
        public string ToUrl { get; set; } = string.Empty;
        public int StatusCode { get; set; }
    }
}

