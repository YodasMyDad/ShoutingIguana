using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
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
        try
        {
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
            if (statusCode == 200 && ctx.Page != null && !string.IsNullOrEmpty(ctx.RenderedHtml))
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

    private async Task AnalyzeHttpRedirectAsync(UrlContext ctx)
    {
        var statusCode = ctx.Metadata.StatusCode;
        var location = ctx.Headers.TryGetValue("location", out var loc) ? loc : null;
        
        if (string.IsNullOrEmpty(location))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"HTTP Status: {statusCode}")
                .AddItem("❌ Missing Location header")
                .BeginNested("⚠️ Impact")
                    .AddItem("Browsers won't know where to redirect")
                    .AddItem("This is a server configuration error")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("statusCode", statusCode)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MISSING_LOCATION",
                $"Redirect status {statusCode} but missing Location header",
                details);
            return;
        }

        // Report the redirect
        var redirectDetails = FindingDetailsBuilder.Create()
            .AddItem($"From: {ctx.Url}")
            .AddItem($"To: {location}")
            .AddItem($"Type: {GetRedirectType(statusCode)} ({statusCode})")
            .WithTechnicalMetadata("fromUrl", ctx.Url.ToString())
            .WithTechnicalMetadata("toUrl", location)
            .WithTechnicalMetadata("statusCode", statusCode)
            .WithTechnicalMetadata("redirectType", GetRedirectType(statusCode))
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            $"REDIRECT_{statusCode}",
            $"Redirects to: {location}",
            redirectDetails);

        // Check for temporary vs permanent redirects
        if (statusCode == 302 || statusCode == 307)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Redirect type: {GetRedirectType(statusCode)}")
                .AddItem($"To: {location}")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use 301 (permanent) redirects for permanent moves")
                    .AddItem("Permanent redirects pass more link equity")
                    .AddItem("Only use 302/307 for truly temporary redirects")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("toUrl", location)
                .WithTechnicalMetadata("statusCode", statusCode)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TEMPORARY_REDIRECT",
                $"Using {statusCode} (temporary) redirect - consider 301 for permanent moves",
                details);
        }

        // Check for mixed content (HTTPS -> HTTP)
        if (ctx.Url.Scheme == "https" && location.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("🔒 HTTPS page redirects to HTTP")
                .AddItem($"From: {ctx.Url}")
                .AddItem($"To: {location}")
                .BeginNested("⚠️ Security Issue")
                    .AddItem("This breaks HTTPS and causes browser security warnings")
                    .AddItem("Users may see 'Not Secure' warnings")
                .BeginNested("💡 Recommendations")
                    .AddItem("Update redirect to point to HTTPS version")
                    .AddItem("Never downgrade from HTTPS to HTTP")
                .WithTechnicalMetadata("fromUrl", ctx.Url.ToString())
                .WithTechnicalMetadata("toUrl", location)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MIXED_CONTENT_REDIRECT",
                "HTTPS page redirects to HTTP (security issue)",
                details);
        }

        // Check for protocol canonicalization (http -> https)
        if (ctx.Url.Scheme == "http" && location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("🔒 HTTP to HTTPS redirect")
                .AddItem($"From: {ctx.Url}")
                .AddItem($"To: {location}")
                .AddItem("✅ Good practice for security")
                .WithTechnicalMetadata("fromUrl", ctx.Url.ToString())
                .WithTechnicalMetadata("toUrl", location)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "HTTPS_REDIRECT",
                "HTTP to HTTPS redirect detected",
                details);
        }

        // Check for www canonicalization
        var fromHost = ctx.Url.Host;
        if (Uri.TryCreate(location, UriKind.Absolute, out var toUri))
        {
            var toHost = toUri.Host;
            
            if (fromHost.StartsWith("www.") && !toHost.StartsWith("www."))
            {
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> {
                        ["fromUrl"] = ctx.Url.ToString(),
                        ["toUrl"] = location
                    },
                    "WWW to non-WWW redirect",
                    $"From: {ctx.Url}",
                    $"To: {location}");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "WWW_REDIRECT",
                    "WWW to non-WWW redirect detected",
                    details);
            }
            else if (!fromHost.StartsWith("www.") && toHost.StartsWith("www."))
            {
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> {
                        ["fromUrl"] = ctx.Url.ToString(),
                        ["toUrl"] = location
                    },
                    "Non-WWW to WWW redirect",
                    $"From: {ctx.Url}",
                    $"To: {location}");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "NON_WWW_REDIRECT",
                    "Non-WWW to WWW redirect detected",
                    details);
            }

            // Check for trailing slash inconsistencies
            var fromPath = ctx.Url.AbsolutePath;
            var toPath = toUri.AbsolutePath;
            
            if (fromPath.TrimEnd('/') == toPath.TrimEnd('/') && 
                fromPath.EndsWith("/") != toPath.EndsWith("/"))
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem("Redirect due to trailing slash")
                    .AddItem($"From: {ctx.Url}")
                    .AddItem($"To: {location}")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Ensure consistent trailing slash usage across the site")
                        .AddItem("Decide on a standard (with or without slash) and apply everywhere")
                    .WithTechnicalMetadata("fromUrl", ctx.Url.ToString())
                    .WithTechnicalMetadata("toUrl", location)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "TRAILING_SLASH_REDIRECT",
                    "Redirect due to trailing slash inconsistency",
                    details);
            }
        }
    }

    private async Task ReportMetaRefreshAsync(UrlContext ctx)
    {
        // Use crawler-parsed meta refresh data
        var delay = ctx.Metadata.MetaRefreshDelay ?? 0;
        var targetUrl = ctx.Metadata.MetaRefreshTarget ?? "unknown";

        var details = FindingDetailsBuilder.Create()
            .AddItem($"Target: {targetUrl}")
            .AddItem($"Delay: {delay} seconds")
            .BeginNested("⚠️ SEO Impact")
                .AddItem("Meta refresh is not recommended for SEO")
                .AddItem("Search engines may not follow these redirects")
            .BeginNested("💡 Recommendations")
                .AddItem("Use HTTP 301/302 redirects instead")
                .AddItem("Server-side redirects are faster and more reliable")
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("targetUrl", targetUrl)
            .WithTechnicalMetadata("delay", delay)
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "META_REFRESH_REDIRECT",
            $"Page uses meta refresh redirect (not SEO-friendly): {targetUrl}",
            details);
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
                        var details = FindingDetailsBuilder.Create()
                            .AddItem("JavaScript redirect detected")
                            .AddItem($"Target: {targetUrl}")
                            .BeginNested("⚠️ SEO Impact")
                                .AddItem("JavaScript redirects are not ideal for SEO")
                                .AddItem("Search engines may not follow them")
                                .AddItem("Can impact page ranking and crawlability")
                            .BeginNested("💡 Recommendations")
                                .AddItem("Use HTTP 301/302 redirects instead")
                                .AddItem("Server-side redirects are instant and SEO-friendly")
                            .WithTechnicalMetadata("url", ctx.Url.ToString())
                            .WithTechnicalMetadata("targetUrl", targetUrl)
                            .Build();
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Warning,
                            "JAVASCRIPT_REDIRECT",
                            $"Page uses JavaScript redirect (not ideal for SEO): {targetUrl}",
                            details);
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
                    
                    var builder = FindingDetailsBuilder.Create()
                        .AddItem($"❌ Infinite redirect loop detected!")
                        .AddItem($"Chain length: {chain.Count} redirects");
                    
                    builder.BeginNested("🔄 Redirect Loop");
                    foreach (var hop in chain)
                    {
                        builder.AddItem($"{hop.FromUrl} → {hop.ToUrl} ({hop.StatusCode})");
                    }
                    
                    builder.BeginNested("⚠️ Impact")
                        .AddItem("Page will never load - infinite loop")
                        .AddItem("Browsers will show an error");
                    
                    builder.BeginNested("💡 Recommendations")
                        .AddItem("Fix redirect configuration to eliminate the loop")
                        .AddItem("Ensure final destination doesn't redirect back");
                    
                    builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("loop", chain.Select(r => new { from = r.FromUrl, to = r.ToUrl, status = r.StatusCode }).ToArray());
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Error,
                        "REDIRECT_LOOP",
                        $"Redirect loop detected: {string.Join(" → ", chain.Select(r => $"{r.FromUrl} ({r.StatusCode})"))}",
                        builder.Build());
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
            var severity = chain.Count >= 3 ? Severity.Error : Severity.Warning;
            var chainString = string.Join(" → ", chain.Select(r => $"{r.FromUrl} ({r.StatusCode})"));
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Redirect chain: {chain.Count} hops")
                .AddItem($"Estimated delay: ~{chain.Count * 150}ms");
            
            builder.BeginNested("🔗 Chain");
            foreach (var hop in chain)
            {
                builder.AddItem($"{hop.FromUrl} → {hop.ToUrl} ({hop.StatusCode})");
            }
            
            builder.BeginNested("⚠️ Impact")
                .AddItem("Each redirect adds latency (slower page loads)")
                .AddItem("Consumes crawl budget unnecessarily")
                .AddItem("May dilute link equity");
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Redirect directly to the final URL in a single hop")
                .AddItem("Update all links to point to the final destination");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("chainLength", chain.Count)
                .WithTechnicalMetadata("chain", chain.Select(r => new { from = r.FromUrl, to = r.ToUrl, status = r.StatusCode }).ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                severity,
                "REDIRECT_CHAIN",
                $"Redirect chain detected ({chain.Count} hops): {chainString}",
                builder.Build());
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
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"Redirect type: Temporary ({statusCode})")
                        .AddItem($"Cache duration: {days} days ({maxAge} seconds)")
                        .AddItem($"Cache-Control: {cacheControl}")
                        .BeginNested("⚠️ Issue")
                            .AddItem("Temporary redirects shouldn't be cached for long periods")
                            .AddItem("Clients will cache the redirect and may not see updates")
                        .BeginNested("💡 Recommendations")
                            .AddItem("Use 301/308 for permanent redirects")
                            .AddItem("Or reduce cache duration for truly temporary redirects")
                        .WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("statusCode", statusCode)
                        .WithTechnicalMetadata("maxAge", maxAge)
                        .WithTechnicalMetadata("cacheControl", cacheControl)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "TEMPORARY_REDIRECT_LONG_CACHE",
                        $"Temporary redirect ({statusCode}) has long cache duration ({maxAge} seconds / {days} days)",
                        details);
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

