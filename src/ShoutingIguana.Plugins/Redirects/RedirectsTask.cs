using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Redirects;

/// <summary>
/// Analyzes redirect chains, loops, and canonicalization issues.
/// </summary>
public class RedirectsTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    private const int MAX_REDIRECT_CHAIN_LENGTH = 3;
    private const int WARNING_REDIRECT_CHAIN_LENGTH = 2;
    private const int MAX_CHAIN_HOPS = 5;
    
    // Track redirects seen during Stage 2 analysis (per project)
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, RedirectHop>> RuntimeRedirectsByProject = new();
    
    // Cache redirect graph from repository (Stage 1 crawl data)
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, RedirectHop>> RedirectCacheByProject = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> RedirectCacheLoadingSemaphores = new();
    
    // Cache URL statuses for validating redirect targets
    private static readonly ConcurrentDictionary<int, Dictionary<string, int>> UrlStatusCacheByProject = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UrlStatusLoadingSemaphores = new();

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
                var resolvedTarget = string.IsNullOrEmpty(location)
                    ? null
                    : ResolveRedirectTarget(ctx.Url, location);
                
                // Track redirect for chain/loop detection
                if (!string.IsNullOrEmpty(resolvedTarget))
                {
                    TrackRedirect(ctx.Project.ProjectId, ctx.Url.ToString(), resolvedTarget, statusCode);
                }
                
                await AnalyzeHttpRedirectAsync(ctx, resolvedTarget);
                
                // Check for redirect chains and loops using repository cache
                await CheckRedirectChainsAsync(ctx, resolvedTarget, ct);
                
                // Validate redirect target status using repository cache
                await ValidateRedirectTargetAsync(ctx, resolvedTarget, ct);
                
                // Check redirect caching headers
                await CheckRedirectCachingAsync(ctx, statusCode, resolvedTarget);
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
        var normalizedSource = NormalizeUrl(fromUrl);
        var projectRedirects = RuntimeRedirectsByProject.GetOrAdd(
            projectId,
            _ => new ConcurrentDictionary<string, RedirectHop>(StringComparer.OrdinalIgnoreCase));

        var hop = new RedirectHop(fromUrl, toUrl, statusCode);
        projectRedirects[normalizedSource] = hop;

        if (RedirectCacheByProject.TryGetValue(projectId, out var cachedRedirects))
        {
            cachedRedirects[normalizedSource] = hop;
        }
    }
    
    private async Task ReportRedirectLoopErrorAsync(UrlContext ctx)
    {
        var row = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", "(Loop)")
            .Set("StatusCode", 0)
            .Set("Issue", "Infinite redirect loop detected; browsers abort with ERR_TOO_MANY_REDIRECTS, so break the loop and point to a resolvable destination.")
            .Set("Severity", "Error");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task AnalyzeHttpRedirectAsync(UrlContext ctx, string? targetUrl)
    {
        var statusCode = ctx.Metadata.StatusCode;
        
        if (string.IsNullOrEmpty(targetUrl))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", "(missing location header)")
                .Set("StatusCode", statusCode)
                .Set("Issue", "Redirect response missing a Location header, so browsers never know where to go; add a valid Location URL.")
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            return;
        }

        // Report the redirect
        var redirectType = GetRedirectType(statusCode);
        var redirectSummary = GetRedirectSummary(statusCode);
        var redirectRow = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", targetUrl)
            .Set("StatusCode", statusCode)
            .Set("Issue", $"{redirectType} - {redirectSummary}")
            .Set("Severity", "Info");
        
        await ctx.Reports.ReportAsync(Key, redirectRow, ctx.Metadata.UrlId, default);

        // Check for temporary vs permanent redirects
        if (statusCode == 302 || statusCode == 307)
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", targetUrl)
                .Set("StatusCode", statusCode)
                .Set("Issue", "Temporary redirect detected; switch to a permanent 301 if this move is meant to be long-lived so search engines index the correct URL.")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for mixed content (HTTPS -> HTTP)
        if (ctx.Url.Scheme == "https" && targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", targetUrl)
                .Set("StatusCode", statusCode)
                .Set("Issue", "Redirecting from HTTPS to HTTP downgrades security and can trigger mixed-content warnings; keep the destination on HTTPS.")
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for protocol canonicalization (http -> https)
        if (ctx.Url.Scheme == "http" && targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", targetUrl)
                .Set("StatusCode", statusCode)
                .Set("Issue", "Redirecting to HTTPS improves privacy and search visibility; keep the secure URL as the canonical destination.")
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for www canonicalization
        var fromHost = ctx.Url.Host;
        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var toUri))
        {
            var toHost = toUri.Host;
            
            if (fromHost.StartsWith("www.") && !toHost.StartsWith("www."))
            {
                var row = ReportRow.Create()
                    .Set("Source", ctx.Url.ToString())
                    .Set("Target", targetUrl)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "Redirecting from www to the apex domain keeps a single canonical hostname and avoids duplicate content.")
                    .Set("Severity", "Info");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
            else if (!fromHost.StartsWith("www.") && toHost.StartsWith("www."))
            {
                var row = ReportRow.Create()
                    .Set("Source", ctx.Url.ToString())
                    .Set("Target", targetUrl)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "Redirecting to www canonicalizes the site to that hostname; keep the choice consistent across URLs.")
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
                    .Set("Target", targetUrl)
                    .Set("StatusCode", statusCode)
                    .Set("Issue", "Redirect to add/remove a trailing slash indicates mismatched canonicalisation; serve one preferred version without redirecting.")
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
            .Set("Issue", $"Meta refresh redirect ({delay}s delay) waits before navigating, which hurts UX and SEO; use an HTTP redirect instead.")
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
                            .Set("Issue", "JavaScript-based redirects rely on script execution, which search engines may not follow and can delay page stability; prefer HTTP redirects.")
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

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        try
        {
            return UrlHelper.Normalize(url);
        }
        catch
        {
            return url.TrimEnd('/').ToLowerInvariant();
        }
    }

    private static string GetRedirectType(int statusCode)
    {
        return statusCode switch
        {
            301 => "Permanent Redirect (301)",
            302 => "Temporary Redirect (302)",
            303 => "See Other (303)",
            307 => "Temporary Redirect (307)",
            308 => "Permanent Redirect (308)",
            _ => $"Redirect ({statusCode})"
        };
    }

    private static string GetRedirectSummary(int statusCode)
    {
        return statusCode switch
        {
            301 => "Preserves ranking signals by pointing users and crawlers to the canonical URL.",
            302 => "Best for short-lived changes; search engines typically keep the original URL indexed.",
            303 => "Tells clients to follow up with a GET request, which is handy after form submissions.",
            307 => "Temporary move that preserves the HTTP method; switch to 301 if the move is permanent.",
            308 => "Permanent move that also preserves the HTTP method, useful for APIs that must stay RESTful.",
            _ => "Uses an uncommon redirect code; verify that it matches your intended flow."
        };
    }
    
    /// <summary>
    /// Check for redirect chains and loops
    /// </summary>
    private async Task CheckRedirectChainsAsync(UrlContext ctx, string? initialTarget, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(initialTarget))
        {
            return;
        }
        
        await EnsureRedirectCacheLoadedAsync(ctx.Project.ProjectId, ct);
        
        var chain = new List<RedirectHop>
        {
            new(ctx.Url.ToString(), initialTarget, ctx.Metadata.StatusCode)
        };
        
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeUrl(ctx.Url.ToString())
        };
        
        var current = initialTarget;
        for (int i = 0; i < MAX_CHAIN_HOPS; i++)
        {
            if (!TryGetRedirectHop(ctx.Project.ProjectId, current, out var hop))
            {
                break;
            }
            
            var normalizedCurrent = NormalizeUrl(hop.FromUrl);
            if (!visited.Add(normalizedCurrent))
            {
                chain.Add(hop);
                await ReportRedirectChainAsync(ctx, chain, isLoop: true);
                return;
            }
            
            chain.Add(hop);
            current = hop.ToUrl;
        }
        
        if (chain.Count > 1)
        {
            await ReportRedirectChainAsync(ctx, chain, isLoop: false);
        }
    }

    private bool TryGetRedirectHop(int projectId, string sourceUrl, [NotNullWhen(true)] out RedirectHop? hop)
    {
        var normalized = NormalizeUrl(sourceUrl);
        
        if (RuntimeRedirectsByProject.TryGetValue(projectId, out var runtime) &&
            runtime.TryGetValue(normalized, out hop))
        {
            return true;
        }
        
        if (RedirectCacheByProject.TryGetValue(projectId, out var cached) &&
            cached.TryGetValue(normalized, out hop))
        {
            return true;
        }
        
        hop = null;
        return false;
    }

    private async Task ReportRedirectChainAsync(UrlContext ctx, List<RedirectHop> chain, bool isLoop)
    {
        var hopCount = chain.Count;
        var issueType = isLoop ? "Redirect Loop" : "Redirect Chain";
        var severity = isLoop
            ? "Error"
            : hopCount >= MAX_REDIRECT_CHAIN_LENGTH ? "Error"
            : hopCount >= WARNING_REDIRECT_CHAIN_LENGTH ? "Warning"
            : "Info";
        
        var description = BuildChainDescription(chain);
        var finalHop = chain[^1];
        var reason = BuildChainReason(isLoop);
        
        var row = ReportRow.Create()
            .Set("Source", ctx.Url.ToString())
            .Set("Target", finalHop.ToUrl)
            .Set("StatusCode", finalHop.StatusCode)
            .Set("Issue", $"{issueType} ({hopCount} hops) - {description}. {reason}")
            .Set("Severity", severity);
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private static string BuildChainDescription(IReadOnlyList<RedirectHop> chain)
    {
        if (chain.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(chain.Count + 1);
        foreach (var hop in chain)
        {
            parts.Add($"{hop.FromUrl} ({hop.StatusCode})");
        }

        parts.Add(chain[^1].ToUrl);
        return string.Join(" -> ", parts);
    }

    private static string BuildChainReason(bool isLoop)
    {
        return isLoop
            ? "Loop prevents browsers and crawlers from ever reaching the destination."
            : "Each extra hop slows load times and risks losing ranking signals; keep redirects to a single hop.";
    }

    /// <summary>
    /// Validate redirect target status
    /// </summary>
    private async Task ValidateRedirectTargetAsync(UrlContext ctx, string? targetUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(targetUrl))
        {
            return;
        }
        
        await EnsureUrlStatusCacheLoadedAsync(ctx.Project.ProjectId, ct);
        
        if (!UrlStatusCacheByProject.TryGetValue(ctx.Project.ProjectId, out var statusCache))
        {
            return;
        }
        
        var normalizedTarget = NormalizeUrl(targetUrl);
        
        if (!statusCache.TryGetValue(normalizedTarget, out var status))
        {
            _logger.LogDebug("Redirect target {Target} not found in status cache", targetUrl);
            return;
        }
        
        if (status >= 400)
        {
            var severity = status >= 500 ? "Error" : "Warning";
            
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", targetUrl)
                .Set("StatusCode", status)
                .Set("Issue", $"Redirect target returns HTTP {status}, so visitors land on a failing page; fix the endpoint or stop redirecting there.")
                .Set("Severity", severity);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
        else if (status >= 300 && status < 400)
        {
            var row = ReportRow.Create()
                .Set("Source", ctx.Url.ToString())
                .Set("Target", targetUrl)
                .Set("StatusCode", status)
                .Set("Issue", "Redirect target is itself another redirect, which adds latency and increases loop risk; point directly to the final destination.")
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }
    
    private async Task EnsureRedirectCacheLoadedAsync(int projectId, CancellationToken ct)
    {
        if (RedirectCacheByProject.ContainsKey(projectId))
        {
            return;
        }
        
        var semaphore = RedirectCacheLoadingSemaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (RedirectCacheByProject.ContainsKey(projectId))
            {
                return;
            }
            
            var cache = new ConcurrentDictionary<string, RedirectHop>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var redirect in _repositoryAccessor.GetRedirectsAsync(projectId, ct))
            {
                if (string.IsNullOrWhiteSpace(redirect.SourceUrl) || string.IsNullOrWhiteSpace(redirect.ToUrl))
                {
                    continue;
                }
                
                var normalizedSource = NormalizeUrl(redirect.SourceUrl);
                cache[normalizedSource] = new RedirectHop(redirect.SourceUrl, redirect.ToUrl, redirect.StatusCode);
            }
            
            RedirectCacheByProject[projectId] = cache;
            _logger.LogInformation("Cached {Count} redirect entries for project {ProjectId}", cache.Count, projectId);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    private async Task EnsureUrlStatusCacheLoadedAsync(int projectId, CancellationToken ct)
    {
        if (UrlStatusCacheByProject.ContainsKey(projectId))
        {
            return;
        }
        
        var semaphore = UrlStatusLoadingSemaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (UrlStatusCacheByProject.ContainsKey(projectId))
            {
                return;
            }
            
            var statusCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var url in _repositoryAccessor.GetUrlsAsync(projectId, ct))
            {
                if (!string.IsNullOrEmpty(url.NormalizedUrl))
                {
                    var normalizedKey = NormalizeUrl(url.NormalizedUrl);
                    statusCache[normalizedKey] = url.Status;
                }
            }
            
            UrlStatusCacheByProject[projectId] = statusCache;
            _logger.LogInformation("Cached {Count} URL statuses for project {ProjectId}", statusCache.Count, projectId);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// Check redirect caching headers
    /// </summary>
    private async Task CheckRedirectCachingAsync(UrlContext ctx, int statusCode, string? targetUrl)
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
                    
                    var row = ReportRow.Create()
                        .Set("Source", ctx.Url.ToString())
                        .Set("Target", targetUrl ?? string.Empty)
                        .Set("StatusCode", statusCode)
                        .Set("Issue", $"Temporary redirect cached for {days} days; browsers may keep following it even after it should change, so lower the max-age or use a permanent redirect.")
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                }
            }
        }
    }
    
    private string ResolveRedirectTarget(Uri currentUrl, string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return location;
        }
        
        try
        {
            if (Uri.TryCreate(location, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }
            
            return UrlHelper.Resolve(currentUrl, location);
        }
        catch
        {
            return location;
        }
    }
    
    /// <summary>
    /// Cleanup per-project data when project is closed
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        RuntimeRedirectsByProject.TryRemove(projectId, out _);
        RedirectCacheByProject.TryRemove(projectId, out _);
        UrlStatusCacheByProject.TryRemove(projectId, out _);
        
        if (RedirectCacheLoadingSemaphores.TryRemove(projectId, out var redirectSemaphore))
        {
            redirectSemaphore.Dispose();
        }
        
        if (UrlStatusLoadingSemaphores.TryRemove(projectId, out var statusSemaphore))
        {
            statusSemaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up redirect data for project {ProjectId}", projectId);
    }
    
    private sealed record RedirectHop(string FromUrl, string ToUrl, int StatusCode);
}

