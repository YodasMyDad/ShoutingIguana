using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;

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

    public override string Key => "Canonical";
    public override string DisplayName => "Canonical";
    public override string Description => "Validates canonical URLs and detects chains, conflicts, and cross-domain issues";
    public override int Priority => 25; // Run after basic analysis but before advanced

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
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
        var projectCanonicals = CanonicalsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, string>());
        projectCanonicals[url] = canonical;
    }

    private async Task CheckMissingCanonicalAsync(UrlContext ctx, string? canonical)
    {
        // Only report missing canonical on important pages (depth <= 2)
        if (string.IsNullOrEmpty(canonical) && ctx.Metadata.Depth <= 2)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Page depth: {ctx.Metadata.Depth} (important page)")
                .AddItem("No canonical tag found")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add canonical tag to help search engines understand the preferred version")
                    .AddItem("Self-referencing canonicals are a best practice")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MISSING_CANONICAL",
                "Important page is missing canonical tag",
                details);
        }
    }

    private async Task CheckMultipleCanonicalsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasMultipleCanonicals)
        {
            return;
        }

        var details = FindingDetailsBuilder.Create()
            .AddItem("Multiple canonical tags detected:")
            .AddItem($"  • HTML: {ctx.Metadata.CanonicalHtml ?? "none"}")
            .AddItem($"  • HTTP: {ctx.Metadata.CanonicalHttp ?? "none"}")
            .BeginNested("⚠️ Issue")
                .AddItem("Multiple canonicals confuse search engines")
                .AddItem("Search engines may ignore all canonicals")
            .BeginNested("💡 Recommendations")
                .AddItem("Remove duplicate canonical tags")
                .AddItem("Keep only one canonical declaration")
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("canonicalHtml", ctx.Metadata.CanonicalHtml)
            .WithTechnicalMetadata("canonicalHttp", ctx.Metadata.CanonicalHttp)
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Error,
            "MULTIPLE_CANONICALS",
            "Page has multiple canonical tags",
            details);
    }

    private async Task CheckCrossDomainCanonicalAsync(UrlContext ctx, string? canonical)
    {
        if (!ctx.Metadata.HasCrossDomainCanonical || string.IsNullOrEmpty(canonical))
        {
            return;
        }

        var details = FindingDetailsBuilder.Create()
            .AddItem($"Canonical URL: {canonical}")
            .AddItem("🌐 Points to different domain")
            .BeginNested("ℹ️ Note")
                .AddItem("Cross-domain canonicals are valid for syndicated content")
                .AddItem("Use carefully - this gives ranking credit to another domain")
            .WithTechnicalMetadata("url", ctx.Url.ToString())
            .WithTechnicalMetadata("canonical", canonical)
            .Build();
        
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "CROSS_DOMAIN_CANONICAL",
            $"Canonical points to different domain: {canonical}",
            details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Current URL: {ctx.Url}")
                .AddItem($"Canonical URL: {canonical}")
                .BeginNested("ℹ️ Impact")
                    .AddItem("This page will not be indexed")
                    .AddItem("Canonical page will be indexed instead")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("canonical", canonical)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "CANONICAL_TO_OTHER_PAGE",
                $"Canonical points to different URL: {canonical}",
                details);
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
            if (projectCanonicals.TryGetValue(canonical, out var canonicalOfCanonical))
            {
                var normalizedCanonical = NormalizeUrl(canonical);
                var normalizedCanonicalOfCanonical = NormalizeUrl(canonicalOfCanonical);

                if (normalizedCanonical != normalizedCanonicalOfCanonical)
                {
                    var details = FindingDetailsBuilder.Create()
                        .AddItem("Canonical chain detected:")
                        .AddItem($"  • This page: {ctx.Url}")
                        .AddItem($"  • Points to: {canonical}")
                        .AddItem($"  • Which points to: {canonicalOfCanonical}")
                        .BeginNested("⚠️ Issue")
                            .AddItem("Canonical chains are confusing for search engines")
                            .AddItem("May not be fully respected")
                        .BeginNested("💡 Recommendations")
                            .AddItem("Update canonical to point directly to the final URL")
                            .AddItem($"Change to: {canonicalOfCanonical}")
                        .WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("canonical", canonical)
                        .WithTechnicalMetadata("canonicalOfCanonical", canonicalOfCanonical)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "CANONICAL_CHAIN",
                        $"Canonical chain detected: {ctx.Url} → {canonical} → {canonicalOfCanonical}",
                        details);
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

    private async Task ValidateCanonicalTargetAsync(UrlContext ctx, string canonicalUrl)
    {
        try
        {
            // Check if canonical target was crawled using repository accessor
            var canonicalUrlInfo = await _repositoryAccessor.GetUrlByAddressAsync(
                ctx.Project.ProjectId,
                canonicalUrl);

            if (canonicalUrlInfo != null)
            {
                // Canonical URL was crawled - check its status
                if (canonicalUrlInfo.Status != 200)
                {
                    // Canonical target returns non-200 status - this is a problem
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"Current URL: {ctx.Url}")
                        .AddItem($"Canonical URL: {canonicalUrl}")
                        .AddItem($"Canonical HTTP status: {canonicalUrlInfo.Status}")
                        .BeginNested("❌ Issue")
                            .AddItem("Canonical should point to a page that returns 200 OK")
                            .AddItem($"This canonical returns {canonicalUrlInfo.Status}")
                        .BeginNested("💡 Recommendations")
                            .AddItem("Update canonical to point to a working page")
                            .AddItem("Or fix the canonical target page")
                        .WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("canonicalUrl", canonicalUrl)
                        .WithTechnicalMetadata("canonicalHttpStatus", canonicalUrlInfo.Status)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Error,
                        "CANONICAL_TARGET_ERROR",
                        $"Canonical URL returns HTTP {canonicalUrlInfo.Status}. The canonical target should return 200 OK.",
                        details);
                    
                    _logger.LogWarning("Canonical target error: {Url} → {Canonical} (HTTP {Status})",
                        ctx.Url, canonicalUrl, canonicalUrlInfo.Status);
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
            var source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag";
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Canonical: {canonical}")
                .AddItem($"Noindex source: {source}")
                .BeginNested("⚠️ Conflicting Signals")
                    .AddItem("Canonical says: consolidate to this URL")
                    .AddItem("Noindex says: don't index this page")
                    .AddItem("Google may ignore the canonical when noindex is present")
                .BeginNested("💡 Recommendations")
                    .AddItem("If you want to consolidate: Remove noindex, keep canonical")
                    .AddItem("If you want to de-index: Remove canonical, keep noindex")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("canonical", canonical)
                .WithTechnicalMetadata("robotsNoindex", true)
                .WithTechnicalMetadata("source", source)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CANONICAL_NOINDEX_CONFLICT",
                "Page has both canonical tag AND noindex directive - conflicting signals",
                details);
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Paginated URL: {currentUrl}")
                    .AddItem($"Canonical: {canonical}")
                    .BeginNested("⚠️ Pagination Issue")
                        .AddItem("Paginated pages canonicalize to page 1 or base URL")
                        .AddItem("This removes pagination from search results")
                    .BeginNested("💡 Recommendations")
                        .AddItem("Option 1: Self-referential canonical + rel=prev/next links")
                        .AddItem("Option 2: Canonical to view-all page (if available)")
                        .AddItem("Option 3: No canonical on pagination pages")
                    .WithTechnicalMetadata("url", currentUrl)
                    .WithTechnicalMetadata("canonical", canonical)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "PAGINATION_CANONICAL_ISSUE",
                    $"Paginated URL canonicalizes to page 1 or non-paginated version: {canonical}",
                    details);
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
            if (!projectCanonicals.TryGetValue(current, out var nextCanonical))
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
                
                var builder = FindingDetailsBuilder.Create()
                    .AddItem("❌ Canonical loop detected");
                
                builder.BeginNested("🔄 Loop Chain");
                foreach (var url in chain)
                {
                    builder.AddItem(url);
                }
                
                builder.BeginNested("⚠️ Issue")
                    .AddItem("Circular canonical references confuse search engines")
                    .AddItem("May prevent proper indexing of all pages in loop");
                
                builder.BeginNested("💡 Recommendations")
                    .AddItem("Fix canonical tags to point to a single final URL")
                    .AddItem("Ensure no circular references");
                
                builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("loop", chain.ToArray());
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "CANONICAL_LOOP",
                    $"Canonical loop detected: {string.Join(" → ", chain)}",
                    builder.Build());
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem("Canonical mismatch detected:")
                    .AddItem($"  • HTML: {canonicalHtml}")
                    .AddItem($"  • HTTP: {canonicalHttp}")
                    .BeginNested("⚠️ Issue")
                        .AddItem("Conflicting canonical signals")
                        .AddItem("HTTP Link header typically takes precedence")
                    .EndNested()
                    .BeginNested("💡 Recommendations")
                        .AddItem("Ensure both canonicals point to the same URL")
                        .AddItem("Or remove one (prefer HTTP header for consistency)")
                    .EndNested()
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("canonicalHtml", canonicalHtml)
                    .WithTechnicalMetadata("canonicalHttp", canonicalHttp)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "CANONICAL_HEADER_HTML_MISMATCH",
                    "Canonical tag in HTML differs from Link header",
                    details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem($"HTTP Status: {statusCode} (redirect)")
                .AddItem($"Canonical: {canonical}")
                .BeginNested("⚠️ Issue")
                    .AddItem("Canonical tags on redirected URLs are ignored")
                    .AddItem("Search engines follow the redirect, not the canonical")
                .EndNested()
                .BeginNested("💡 Recommendations")
                    .AddItem("Remove canonical tag from this redirect")
                    .AddItem("The redirect itself is sufficient")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("statusCode", statusCode)
                .WithTechnicalMetadata("canonical", canonical)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CANONICAL_ON_REDIRECT",
                $"Page returns {statusCode} redirect but also has canonical tag",
                details);
        }
    }

    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        CanonicalsByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up canonical data for project {ProjectId}", projectId);
    }
}


