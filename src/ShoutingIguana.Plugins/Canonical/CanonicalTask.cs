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
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        CanonicalsByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up canonical data for project {ProjectId}", projectId);
    }
}

