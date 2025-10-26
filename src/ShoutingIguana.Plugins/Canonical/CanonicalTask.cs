using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;

namespace ShoutingIguana.Plugins.Canonical;

/// <summary>
/// Canonical URL extraction, validation, chain detection, and cross-domain analysis.
/// </summary>
public class CanonicalTask(ILogger logger, IServiceProvider serviceProvider) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    
    // Track canonical chains across URLs per project
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, string>> CanonicalsByProject = new();

    public override string Key => "Canonical";
    public override string DisplayName => "Canonical Validation";
    public override int Priority => 25; // Run after basic analysis but before advanced

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MISSING_CANONICAL",
                "Important page is missing canonical tag",
                new
                {
                    url = ctx.Url.ToString(),
                    depth = ctx.Metadata.Depth,
                    recommendation = "Add canonical tag to help search engines understand the preferred version"
                });
        }
    }

    private async Task CheckMultipleCanonicalsAsync(UrlContext ctx)
    {
        if (!ctx.Metadata.HasMultipleCanonicals)
        {
            return;
        }

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Error,
            "MULTIPLE_CANONICALS",
            "Page has multiple canonical tags",
            new
            {
                url = ctx.Url.ToString(),
                canonicalHtml = ctx.Metadata.CanonicalHtml,
                canonicalHttp = ctx.Metadata.CanonicalHttp,
                recommendation = "Remove duplicate canonical tags - only one should be present"
            });
    }

    private async Task CheckCrossDomainCanonicalAsync(UrlContext ctx, string? canonical)
    {
        if (!ctx.Metadata.HasCrossDomainCanonical || string.IsNullOrEmpty(canonical))
        {
            return;
        }

        await ctx.Findings.ReportAsync(
            Key,
            Severity.Warning,
            "CROSS_DOMAIN_CANONICAL",
            $"Canonical points to different domain: {canonical}",
            new
            {
                url = ctx.Url.ToString(),
                canonical,
                note = "Cross-domain canonicals are valid for syndicated content but should be used carefully"
            });
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "CANONICAL_TO_OTHER_PAGE",
                $"Canonical points to different URL: {canonical}",
                new
                {
                    url = ctx.Url.ToString(),
                    canonical,
                    note = "This page will not be indexed; canonical page will be indexed instead"
                });
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
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "CANONICAL_CHAIN",
                        $"Canonical chain detected: {ctx.Url} → {canonical} → {canonicalOfCanonical}",
                        new
                        {
                            url = ctx.Url.ToString(),
                            canonical,
                            canonicalOfCanonical,
                            recommendation = "Remove canonical chains - canonical should point directly to the final URL"
                        });
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
            // Get URL repository through reflection to check if canonical target was crawled
            var urlRepoType = Type.GetType("ShoutingIguana.Core.Repositories.IUrlRepository, ShoutingIguana.Core");
            if (urlRepoType == null)
            {
                _logger.LogDebug("Unable to load URL repository for canonical validation");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var urlRepo = scope.ServiceProvider.GetService(urlRepoType);
            if (urlRepo == null)
            {
                return;
            }

            // Get the canonical target URL from database
            var getByAddressMethod = urlRepoType.GetMethod("GetByAddressAsync");
            if (getByAddressMethod == null)
            {
                return;
            }

            var taskObj = getByAddressMethod.Invoke(urlRepo, new object[] { ctx.Project.ProjectId, canonicalUrl });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                
                var resultProperty = task.GetType().GetProperty("Result");
                var canonicalUrlEntity = resultProperty?.GetValue(task);

                if (canonicalUrlEntity != null)
                {
                    // Canonical URL was crawled - check its status
                    var urlType = canonicalUrlEntity.GetType();
                    var httpStatus = urlType.GetProperty("HttpStatus")?.GetValue(canonicalUrlEntity) as int?;
                    
                    if (httpStatus.HasValue && httpStatus.Value != 200)
                    {
                        // Canonical target returns non-200 status - this is a problem
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Error,
                            "CANONICAL_TARGET_ERROR",
                            $"Canonical URL returns HTTP {httpStatus.Value}. The canonical target should return 200 OK.",
                            new
                            {
                                CanonicalUrl = canonicalUrl,
                                CanonicalHttpStatus = httpStatus.Value,
                                Issue = "Canonical points to non-200 URL"
                            });
                        
                        _logger.LogWarning("Canonical target error: {Url} → {Canonical} (HTTP {Status})",
                            ctx.Url, canonicalUrl, httpStatus.Value);
                    }
                    else if (httpStatus == 200)
                    {
                        _logger.LogDebug("Canonical target validated: {Canonical} returns 200 OK", canonicalUrl);
                    }
                }
                else
                {
                    // Canonical URL not yet crawled or external
                    _logger.LogDebug("Canonical URL not found in crawled URLs: {Canonical} (may be external or not yet crawled)", canonicalUrl);
                }
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
            var uri = new Uri(url);
            // Normalize: remove fragment, trailing slash, and convert to lowercase
            var normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
            
            // Also normalize query string order if present
            if (!string.IsNullOrEmpty(uri.Query))
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "CANONICAL_NOINDEX_CONFLICT",
                "Page has both canonical tag AND noindex directive - conflicting signals",
                new
                {
                    url = ctx.Url.ToString(),
                    canonical,
                    robotsNoindex = true,
                    source = !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag",
                    issue = "Google may ignore the canonical when noindex is present",
                    recommendation = "Remove either canonical or noindex - if you want to consolidate, use canonical only; if you want to de-index, use noindex only"
                });
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
                
            var removedPageParam = System.Text.RegularExpressions.Regex.Replace(canonical, 
                @"[?&]page=\d+|[?&]p=\d+", 
                "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            var canonicalizesToNonPaginated = !canonical.Contains("/page/") && 
                                             !canonical.Contains("/p") &&
                                             !canonical.Contains("?page=") &&
                                             !canonical.Contains("&page=") &&
                                             !canonical.Contains("?p=") &&
                                             !canonical.Contains("&p=");

            if (normalizedCurrent != normalizedCanonical && (canonicalizesToPageOne || canonicalizesToNonPaginated))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "PAGINATION_CANONICAL_ISSUE",
                    $"Paginated URL canonicalizes to page 1 or non-paginated version: {canonical}",
                    new
                    {
                        url = currentUrl,
                        canonical,
                        issue = "Paginated pages should typically be self-referential or use rel=prev/next",
                        recommendation = "For pagination: use self-referential canonicals + rel=prev/next, OR use view-all canonical if available"
                    });
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
            
            // Check if we've seen this URL before (loop detected)
            if (visited.Contains(normalizedNext))
            {
                chain.Add(nextCanonical);
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "CANONICAL_LOOP",
                    $"Canonical loop detected: {string.Join(" → ", chain)}",
                    new
                    {
                        url = ctx.Url.ToString(),
                        loop = chain.ToArray(),
                        issue = "Circular canonical references confuse search engines",
                        recommendation = "Fix canonical tags to point to a single final URL without loops"
                    });
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
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "CANONICAL_HEADER_HTML_MISMATCH",
                    "Canonical tag in HTML differs from Link header",
                    new
                    {
                        url = ctx.Url.ToString(),
                        canonicalHtml,
                        canonicalHttp,
                        issue = "Conflicting canonical signals",
                        note = "HTTP Link header typically takes precedence over HTML link tag",
                        recommendation = "Ensure both canonicals point to the same URL, or remove one"
                    });
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
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CANONICAL_ON_REDIRECT",
                $"Page returns {statusCode} redirect but also has canonical tag",
                new
                {
                    url = ctx.Url.ToString(),
                    statusCode,
                    canonical,
                    issue = "Canonical tags on redirected URLs are ignored by search engines",
                    recommendation = "Remove canonical tag from redirected URLs - the redirect itself is sufficient"
                });
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

