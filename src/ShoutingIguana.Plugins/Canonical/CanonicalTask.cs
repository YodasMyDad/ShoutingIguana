using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.Canonical;

/// <summary>
/// Canonical URL extraction, validation, chain detection, and cross-domain analysis.
/// </summary>
public class CanonicalTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    
    // Track canonical chains across URLs per project
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, string>> CanonicalsByProject = new();
    
    // Cache URL statuses per project to avoid database queries (critical for performance)
    private static readonly ConcurrentDictionary<int, Dictionary<string, int>> UrlStatusCacheByProject = new();
    
    // Semaphore to ensure only one thread loads URL statuses per project
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UrlStatusLoadingSemaphores = new();

    public override string Key => "Canonical";
    public override string DisplayName => "Canonical";
    public override string Description => "Validates canonical URLs and detects chains, conflicts, and cross-domain issues";
    public override int Priority => 25; // Run after basic analysis but before advanced

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        var statusCode = ctx.Metadata.StatusCode;
        var isRedirect = statusCode >= 300 && statusCode < 400;

        // Redirects may not have HTML bodies, but we still want to inspect canonical metadata
        if (!isRedirect && ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        // Allow analysis for successful pages and redirects (skip only <200 and >=400 responses)
        if (statusCode < 200 || statusCode >= 400)
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
            // Get canonical URL (HTML meta or HTTP header)
            var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;

            // Track this URL's canonical for chain detection
            if (!string.IsNullOrEmpty(canonical))
            {
                TrackCanonical(ctx.Project.ProjectId, ctx.Url.ToString(), canonical);
            }

            // Check for missing canonical
            await CheckMissingCanonicalAsync(ctx, canonical);

            // Check for multiple canonicals
            await CheckMultipleCanonicalsAsync(ctx);

            // Check for cross-domain canonical
            await CheckCrossDomainCanonicalAsync(ctx, canonical);

            // Check for self-referencing canonical
            await CheckSelfReferencingCanonicalAsync(ctx, canonical);

            // Check for canonical chains
            await CheckCanonicalChainsAsync(ctx, canonical);

            // Check for broken canonical (would need to be crawled separately)
            await CheckCanonicalStatusAsync(ctx, canonical);
            
            // NEW: Check for canonical/robots conflicts (noindex + canonical)
            await CheckCanonicalRobotsConflictAsync(ctx, canonical);
            
            // NEW: Check canonical pagination patterns
            await CheckCanonicalPaginationAsync(ctx, canonical);
            
            // NEW: Check for canonical loops (A→B, B→A)
            await CheckCanonicalLoopsAsync(ctx, canonical);
            
            // NEW: Validate HTTP header vs HTML consistency
            await CheckCanonicalConsistencyAsync(ctx);
            
            // NEW: Check for canonicals on redirected URLs
            await CheckCanonicalOnRedirectAsync(ctx, canonical);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing canonical for {Url}", ctx.Url);
        }
    }

    private void TrackCanonical(int projectId, string url, string canonical)
    {
        var projectCanonicals = CanonicalsByProject.GetOrAdd(
            projectId,
            _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var normalizedSource = NormalizeUrl(url);
        projectCanonicals[normalizedSource] = canonical;
    }

    private async Task CheckMissingCanonicalAsync(UrlContext ctx, string? canonical)
    {
        // Only report missing canonical on important pages (depth <= 2)
        if (string.IsNullOrEmpty(canonical) && ctx.Metadata.Depth <= 2)
        {
            var row = ReportRow.Create()
                .SetPage(ctx.Url)
                .Set("Issue", "Missing Canonical")
                .Set("CanonicalURL", "(missing)")
                .Set("Status", "Info")
                .SetSeverity(Severity.Info);
            row.SetExplanation( "Add a canonical tag (HTML meta or HTTP header) on this important page so search engines know which URL to index and to avoid duplicate content.");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task CheckMultipleCanonicalsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasMultipleCanonicals)
        {
            return;
        }

        var canonicalUrl = $"HTML: {ctx.Metadata.CanonicalHtml ?? "none"} | HTTP: {ctx.Metadata.CanonicalHttp ?? "none"}";
        
        var row = ReportRow.Create()
            .Set("Page", ctx.Url.ToString())
            .Set("Issue", "Multiple Canonicals")
            .Set("CanonicalURL", canonicalUrl)
            .Set("Status", "Conflict")
            .SetSeverity(Severity.Error);
        row.SetExplanation( "Multiple canonical hints are present (HTML meta vs. HTTP header). Keep only one consistent canonical reference to avoid confusing crawlers about the preferred URL.");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task CheckCrossDomainCanonicalAsync(UrlContext ctx, string? canonical)
    {
        if (!ctx.Metadata.HasCrossDomainCanonical || string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var row = ReportRow.Create()
            .Set("Page", ctx.Url.ToString())
            .Set("Issue", "Cross-Domain Canonical")
            .Set("CanonicalURL", canonical)
            .Set("Status", "External Domain")
            .SetSeverity(Severity.Warning);
        row.SetExplanation( "This page canonicals to a different domain; confirm that is intentional since it transfers ranking signals and indexing to the external site.");
        
        await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
    }

    private async Task CheckSelfReferencingCanonicalAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        // Normalize URLs for comparison
        var normalizedCurrent = NormalizeUrl(ctx.Url.ToString());
        var normalizedCanonical = NormalizeUrl(canonical);

        if (normalizedCurrent == normalizedCanonical)
        {
            // Self-referencing canonical is good practice
            _logger.LogDebug("URL {Url} has self-referencing canonical (best practice)", ctx.Url);
        }
        else if (!ctx.Metadata.HasCrossDomainCanonical)
        {
            // Canonical points to a different page on the same domain
            var row = ReportRow.Create()
                .SetPage(ctx.Url)
                .Set("Issue", "Canonical to Other Page")
                .Set("CanonicalURL", canonical)
                .Set("Status", "OK")
                .SetSeverity(Severity.Info);
            row.SetExplanation( "This URL points to another page on the same domain as its canonical; confirm that is intentional or update it to self-reference to avoid scope confusion.");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task CheckCanonicalChainsAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        // Check if the canonical URL itself has a different canonical (chain)
        if (CanonicalsByProject.TryGetValue(ctx.Project.ProjectId, out var projectCanonicals))
        {
            var normalizedCanonical = NormalizeUrl(canonical);

            if (projectCanonicals.TryGetValue(normalizedCanonical, out var canonicalOfCanonical))
            {
                var normalizedCanonicalOfCanonical = NormalizeUrl(canonicalOfCanonical);

                if (normalizedCanonical != normalizedCanonicalOfCanonical)
                {
                    var row = ReportRow.Create()
                        .SetPage(ctx.Url)
                        .Set("Issue", $"Canonical Chain: {canonical} → {canonicalOfCanonical}")
                        .Set("CanonicalURL", canonical)
                        .Set("Status", "Chain Detected")
                        .SetSeverity(Severity.Warning);
                    var description = $"This page canonicalizes to {canonical}, but that URL canonicalizes to {canonicalOfCanonical}; point the original straight to {canonicalOfCanonical} to avoid multi-hop chains.";
                    row.SetExplanation( description);
                    
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                }
            }
        }
    }

    private async Task CheckCanonicalStatusAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        // Check if canonical URL was crawled and validate its status
        await ValidateCanonicalTargetAsync(ctx, canonical);
    }

    /// <summary>
    /// Ensures URL status cache is loaded for the project (loads once, thread-safe).
    /// CRITICAL for performance: prevents database query for every canonical checked.
    /// </summary>
    private async Task EnsureUrlStatusCacheLoadedAsync(int projectId, CancellationToken ct)
    {
        // Fast path: check if already cached
        if (UrlStatusCacheByProject.ContainsKey(projectId))
        {
            return;
        }
        
        // Get or create semaphore for this project
        var semaphore = UrlStatusLoadingSemaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        
        // Wait for exclusive access to load URL statuses
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check: another thread might have loaded while we waited
            if (UrlStatusCacheByProject.ContainsKey(projectId))
            {
                return;
            }
            
            // Load all URLs and build status cache
            _logger.LogInformation("Loading URL status cache for project {ProjectId} (CanonicalTask)", projectId);
            var statusCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var url in _repositoryAccessor.GetUrlsAsync(projectId, ct))
            {
                if (!string.IsNullOrEmpty(url.NormalizedUrl))
                {
                    var normalizedKey = NormalizeUrlForCache(url.NormalizedUrl);
                    statusCache[normalizedKey] = url.Status;
                }
            }
            
            // Cache for future use
            UrlStatusCacheByProject[projectId] = statusCache;
            _logger.LogInformation("Cached {Count} URL statuses for project {ProjectId} (CanonicalTask)", statusCache.Count, projectId);
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    private async Task ValidateCanonicalTargetAsync(UrlContext ctx, string canonicalUrl)
    {
        try
        {
            // CRITICAL PERFORMANCE FIX: Pre-load URL status cache
            await EnsureUrlStatusCacheLoadedAsync(ctx.Project.ProjectId, CancellationToken.None);
            
            // Check if canonical target was crawled using cache (FAST!)
            var statusCache = UrlStatusCacheByProject.GetValueOrDefault(ctx.Project.ProjectId);
            // Normalize canonical URL before lookup to ensure consistency with database
            var status = statusCache?.GetValueOrDefault(NormalizeUrlForCache(canonicalUrl));

            if (status.HasValue)
            {
                // Canonical URL was crawled - check its status
                if (status.Value != 200)
                {
                    // Canonical target returns non-200 status - this is a problem
                    var row = ReportRow.Create()
                        .SetPage(ctx.Url)
                        .Set("Issue", $"Canonical Target Error (HTTP {status.Value})")
                        .Set("CanonicalURL", canonicalUrl)
                        .Set("Status", $"HTTP {status.Value}")
                        .SetSeverity(Severity.Error);
                    row.SetExplanation( $"The canonical target returns HTTP {status.Value}; fix that endpoint or update the canonical so crawlers point to a healthy 200 OK URL.");
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                    
                    _logger.LogWarning("Canonical target error: {Url} → {Canonical} (HTTP {Status})",
                        ctx.Url, canonicalUrl, status.Value);
                }
                else
                {
                    _logger.LogDebug("Canonical target validated: {Canonical} returns 200 OK", canonicalUrl);
                }
            }
            else
            {
                // Canonical URL not found in database (external or out of scope)
                _logger.LogDebug("Canonical URL not found in crawled URLs: {Canonical} (likely external or out of scope)", canonicalUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error validating canonical target status");
            // Don't fail the whole task if validation fails
        }
    }

    private string NormalizeUrl(string url)
    {
        try
        {
            var normalized = UrlHelper.Normalize(url);
            
            // Also normalize query string if present
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
            {
                normalized += uri.Query.ToLowerInvariant();
            }
            
            return normalized;
        }
        catch
        {
            return url.TrimEnd('/').ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Check for canonical/robots conflicts (noindex + canonical is problematic)
    /// </summary>
    private async Task CheckCanonicalRobotsConflictAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        // Check if page has noindex directive
        if (ctx.Metadata.RobotsNoindex == true)
        {
            var row = ReportRow.Create()
                .SetPage(ctx.Url)
                .Set("Issue", "Canonical + Noindex Conflict")
                .Set("CanonicalURL", canonical)
                .Set("Status", "Conflicting Directives")
                .SetSeverity(Severity.Warning);
            row.SetExplanation( "Noindex tells crawlers to drop this page while the canonical says to consolidate signals elsewhere; remove one of these directives so the intent is clear.");
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    /// <summary>
    /// Check canonical pagination patterns
    /// </summary>
    private async Task CheckCanonicalPaginationAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var currentUrl = ctx.Url.ToString();
        
        // Detect if this is a paginated URL (page=2, page=3, /page/2/, etc.)
        var isPaginated = System.Text.RegularExpressions.Regex.IsMatch(currentUrl, 
            @"[?&]page=\d+|[?&]p=\d+|/page/\d+/|/p\d+/", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (isPaginated)
        {
            var normalizedCurrent = NormalizeUrl(currentUrl);
            var normalizedCanonical = NormalizeUrl(canonical);
            
            // Check if paginated page canonicalizes to page 1 (often wrong)
            var canonicalizesToPageOne = System.Text.RegularExpressions.Regex.IsMatch(canonical, 
                @"[?&]page=1($|&)|[?&]p=1($|&)|/page/1/", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            var canonicalizesToNonPaginated = !canonical.Contains("/page/") && 
                                             !canonical.Contains("/p") &&
                                             !canonical.Contains("?page=") &&
                                             !canonical.Contains("&page=") &&
                                             !canonical.Contains("?p=") &&
                                             !canonical.Contains("&p=");

            if (normalizedCurrent != normalizedCanonical && (canonicalizesToPageOne || canonicalizesToNonPaginated))
            {
                var row = ReportRow.Create()
                    .Set("Page", currentUrl)
                    .Set("Issue", "Pagination Canonical Issue")
                    .Set("CanonicalURL", canonical)
                    .Set("Status", "Points to Page 1")
                    .SetSeverity(Severity.Warning);
                row.SetExplanation( "Pagination pages should canonicalize to themselves or follow rel=prev/next; pointing every page to page 1 makes crawlers treat them as duplicates.");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    /// <summary>
    /// Check for canonical loops (A→B, B→A)
    /// </summary>
    private async Task CheckCanonicalLoopsAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var projectId = ctx.Project.ProjectId;
        
        if (!CanonicalsByProject.TryGetValue(projectId, out var projectCanonicals))
        {
            return;
        }

        // Build the canonical chain and detect loops
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chain = new List<string>();
        var current = ctx.Url.ToString();
        
        chain.Add(current);
        visited.Add(NormalizeUrl(current));

        // Follow the chain up to 5 hops
        for (int i = 0; i < 5; i++)
        {
            if (!projectCanonicals.TryGetValue(NormalizeUrl(current), out var nextCanonical))
            {
                break;
            }

            var normalizedNext = NormalizeUrl(nextCanonical);
            var normalizedCurrent = NormalizeUrl(current);
            
            // Check if this is a self-referencing canonical (A → A)
            // This is proper termination, not a loop
            if (normalizedNext == normalizedCurrent)
            {
                _logger.LogDebug("Canonical chain for {Url} ends with self-referencing canonical at {Current} (proper termination)", 
                    ctx.Url, current);
                return; // Proper end of chain
            }
            
            // Check if we've seen this URL before (loop detected)
            if (visited.Contains(normalizedNext))
            {
                // This is a real loop - we're cycling back to a DIFFERENT URL seen earlier
                chain.Add(nextCanonical);
                
                var row = ReportRow.Create()
                    .SetPage(ctx.Url)
                    .Set("Issue", $"Canonical Loop: {string.Join(" → ", chain)}")
                    .Set("CanonicalURL", canonical)
                    .Set("Status", "Loop Detected")
                    .SetSeverity(Severity.Error);
                row.SetExplanation( $"Canonical loop detected ({string.Join(" → ", chain)}); break the cycle by pointing each URL directly to the final canonical target instead of bouncing around.");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                return;
            }

            chain.Add(nextCanonical);
            visited.Add(normalizedNext);
            current = nextCanonical;
        }
    }

    /// <summary>
    /// Validate canonical HTTP header vs HTML consistency
    /// </summary>
    private async Task CheckCanonicalConsistencyAsync(UrlContext ctx)
    {
        var canonicalHtml = ctx.Metadata.CanonicalHtml;
        var canonicalHttp = ctx.Metadata.CanonicalHttp;

        // If both are present, they should match
        if (!string.IsNullOrEmpty(canonicalHtml) && !string.IsNullOrEmpty(canonicalHttp))
        {
            var normalizedHtml = NormalizeUrl(canonicalHtml);
            var normalizedHttp = NormalizeUrl(canonicalHttp);

            if (normalizedHtml != normalizedHttp)
            {
                var canonicalUrl = $"HTML: {canonicalHtml} | HTTP: {canonicalHttp}";
                
                var row = ReportRow.Create()
                    .SetPage(ctx.Url)
                    .Set("Issue", "Canonical HTML/HTTP Mismatch")
                    .Set("CanonicalURL", canonicalUrl)
                    .Set("Status", "Mismatch")
                    .SetSeverity(Severity.Warning);
                row.SetExplanation( "The HTML meta canonical does not match the HTTP header canonical; align these values so crawlers receive the same directive via either channel.");
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    /// <summary>
    /// Check for canonicals on redirected URLs (pointless)
    /// </summary>
    private async Task CheckCanonicalOnRedirectAsync(UrlContext ctx, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var statusCode = ctx.Metadata.StatusCode;
        
        // Check if this URL is a redirect
        if (statusCode >= 300 && statusCode < 400)
        {
            var row = ReportRow.Create()
                .SetPage(ctx.Url)
                .Set("Issue", $"Canonical on Redirect (HTTP {statusCode})")
                .Set("CanonicalURL", canonical)
                .Set("Status", $"HTTP {statusCode}")
                .SetSeverity(Severity.Warning);
            row.SetExplanation( $"Redirected URLs (HTTP {statusCode}) should not declare a canonical; remove the tag so the destination page can be the canonical source.");
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    /// <summary>
    /// Normalizes a URL for cache lookups (matching database normalization).
    /// This ensures consistent lookups regardless of trailing slash variations.
    /// </summary>
    private static string NormalizeUrlForCache(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
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
    
    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        CanonicalsByProject.TryRemove(projectId, out _);
        UrlStatusCacheByProject.TryRemove(projectId, out _);
        
        // Cleanup and dispose semaphore
        if (UrlStatusLoadingSemaphores.TryRemove(projectId, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up canonical data for project {ProjectId}", projectId);
    }
}

