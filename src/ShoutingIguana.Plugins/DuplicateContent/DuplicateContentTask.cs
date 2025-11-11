using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.DuplicateContent;

/// <summary>
/// Exact and near-duplicate content detection using SHA-256 and SimHash algorithms.
/// Also checks domain/protocol variants to ensure proper 301 redirects are in place.
/// </summary>
public class DuplicateContentTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    private static readonly HttpClient HttpClient = new(new HttpClientHandler 
    { 
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true // Accept all SSL certificates for testing
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    // Track content hashes per project for duplicate detection
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> ContentHashesByProject = new();
    
    // Track SimHashes per project for near-duplicate detection
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<ulong, List<string>>> SimHashesByProject = new();
    
    // Track which projects have had their domain variants checked
    private static readonly ConcurrentDictionary<int, bool> DomainVariantsCheckedByProject = new();
    
    // Cache redirects per project to avoid loading them repeatedly (critical for performance)
    private static readonly ConcurrentDictionary<int, Dictionary<string, List<RedirectInfo>>> RedirectCacheByProject = new();
    
    // Semaphore to ensure only one thread loads redirects per project (prevents race condition)
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> RedirectLoadingSemaphores = new();

    public override string Key => "DuplicateContent";
    public override string DisplayName => "Duplicate Content";
    public override string Description => "Identifies exact and near-duplicate content across your pages, and validates domain/protocol variants redirect properly";
    public override int Priority => 50; // Run after basic analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        try
        {
            // Check domain variants once per project (on first URL processed)
            // TryAdd returns true only if the key was added (thread-safe)
            if (DomainVariantsCheckedByProject.TryAdd(ctx.Project.ProjectId, true))
            {
                await CheckDomainVariantsAsync(ctx, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking domain variants for project {ProjectId}", ctx.Project.ProjectId);
        }
        
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        if (string.IsNullOrEmpty(ctx.RenderedHtml))
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
            // Extract and clean content
            var cleanedContent = CleanHtmlContent(ctx.RenderedHtml);

            if (string.IsNullOrWhiteSpace(cleanedContent))
            {
                _logger.LogDebug("No meaningful content to analyze for {Url}", ctx.Url);
                return;
            }

            // Compute SHA-256 hash for exact duplicate detection
            var contentHash = ComputeSha256Hash(cleanedContent);

            // Compute SimHash for near-duplicate detection
            var simHash = SimHashCalculator.ComputeSimHash(cleanedContent);

            // Store hashes for this URL
            // Note: Will also be stored in Urls.ContentHash and Urls.SimHash columns via migration
            TrackContentHash(ctx.Project.ProjectId, contentHash, ctx.Url.ToString());
            TrackSimHash(ctx.Project.ProjectId, simHash, ctx.Url.ToString());

            // Check for exact duplicates
            await CheckExactDuplicatesAsync(ctx, contentHash, cleanedContent);

            // Check for near-duplicates
            await CheckNearDuplicatesAsync(ctx, simHash);
            
            // Check boilerplate ratio (important pages only)
            await CheckBoilerplateRatioAsync(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing duplicate content for {Url}", ctx.Url);
        }
    }

    private string CleanHtmlContent(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove script, style, and other non-content elements
            var nodesToRemove = doc.DocumentNode.SelectNodes("//script | //style | //noscript | //svg | //iframe");
            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove)
                {
                    node.Remove();
                }
            }

            // Get text content
            var text = doc.DocumentNode.InnerText;

            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ");
            text = text.Trim();

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning HTML content");
            return string.Empty;
        }
    }

    private string ComputeSha256Hash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }

    private void TrackContentHash(int projectId, string hash, string url)
    {
        var projectHashes = ContentHashesByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<string>>());
        
        // Use GetOrAdd to create the list if it doesn't exist, then lock separately to add the URL
        var list = projectHashes.GetOrAdd(hash, _ => new List<string>());
        
        lock (list)
        {
            // Only add if not already in the list (prevent duplicate entries for same URL)
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(url);
            }
        }
    }

    private void TrackSimHash(int projectId, ulong simHash, string url)
    {
        var projectSimHashes = SimHashesByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<ulong, List<string>>());
        
        // Use GetOrAdd to create the list if it doesn't exist, then lock separately to add the URL
        var list = projectSimHashes.GetOrAdd(simHash, _ => new List<string>());
        
        lock (list)
        {
            // Only add if not already in the list (prevent duplicate entries for same URL)
            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(url);
            }
        }
    }

    private async Task CheckExactDuplicatesAsync(UrlContext ctx, string contentHash, string cleanedContent)
    {
        if (!ContentHashesByProject.TryGetValue(ctx.Project.ProjectId, out var projectHashes))
        {
            return;
        }

        if (!projectHashes.TryGetValue(contentHash, out var duplicateUrls))
        {
            return;
        }

        List<string> urlsCopy;
        lock (duplicateUrls)
        {
            urlsCopy = duplicateUrls.ToList();
        }

        // If multiple URLs have the same hash, they're exact duplicates
        if (urlsCopy.Count > 1)
        {
            var currentUrl = ctx.Url.ToString();
            var otherUrls = urlsCopy.Where(u => u != currentUrl).Take(10).ToList();

            // Check if any of these duplicates are actually redirect relationships
            var redirectInfo = await CheckRedirectRelationshipsAsync(ctx.Project.ProjectId, currentUrl, otherUrls);
            
            if (redirectInfo.HasRedirects)
            {
                // Filter out URLs that have proper permanent redirects
                var nonRedirectDuplicates = otherUrls
                    .Where(url => !redirectInfo.PermanentRedirects.Contains(url))
                    .ToArray();
                
                // If we have temporary redirects, report them separately
                if (redirectInfo.TemporaryRedirects.Any())
                {
                    var row1 = ReportRow.Create()
                        .Set("Page", currentUrl)
                        .Set("Issue", "Duplicate (Temporary Redirects)")
                        .Set("DuplicateOf", redirectInfo.TemporaryRedirects.First())
                        .Set("ContentHash", contentHash.Length > 12 ? contentHash[..12] : contentHash)
                        .Set("Similarity", 100)
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, row1, ctx.Metadata.UrlId, default);
                }
                
                // Only report true duplicates (no redirect relationship)
                if (nonRedirectDuplicates.Length > 0)
                {
                    var row = ReportRow.Create()
                        .Set("Page", currentUrl)
                        .Set("Issue", $"Exact Duplicate ({nonRedirectDuplicates.Length + 1} pages)")
                        .Set("DuplicateOf", nonRedirectDuplicates.First())
                        .Set("ContentHash", contentHash.Length > 12 ? contentHash[..12] : contentHash)
                        .Set("Similarity", 100)
                        .Set("Severity", "Error");
                    
                    await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                }
            }
            else
            {
                // No redirects found, report as regular duplicate
                var row = ReportRow.Create()
                    .Set("Page", currentUrl)
                    .Set("Issue", $"Exact Duplicate ({urlsCopy.Count} pages)")
                    .Set("DuplicateOf", otherUrls.First())
                    .Set("ContentHash", contentHash.Length > 12 ? contentHash[..12] : contentHash)
                    .Set("Similarity", 100)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task CheckNearDuplicatesAsync(UrlContext ctx, ulong simHash)
    {
        if (!SimHashesByProject.TryGetValue(ctx.Project.ProjectId, out var projectSimHashes))
        {
            return;
        }

        // Check this SimHash against all others in the project
        var nearDuplicates = new List<(string Url, double Similarity)>(); // Cannot use [] for tuple lists yet

        foreach (var kvp in projectSimHashes)
        {
            var otherSimHash = kvp.Key;
            var otherUrls = kvp.Value;

            // Skip if it's the exact same SimHash (already detected as exact duplicate)
            if (otherSimHash == simHash)
            {
                continue;
            }

            // Calculate Hamming distance
            var distance = SimHashCalculator.HammingDistance(simHash, otherSimHash);

            // Threshold: distance < 3 means very similar (>95% similar)
            if (distance < 3)
            {
                var similarity = SimHashCalculator.SimilarityPercentage(simHash, otherSimHash);

                lock (otherUrls)
                {
                    foreach (var otherUrl in otherUrls.Where(u => u != ctx.Url.ToString()))
                    {
                        nearDuplicates.Add((otherUrl, similarity));
                    }
                }
            }
        }

        // Report near-duplicates
        if (nearDuplicates.Any())
        {
            var topDuplicates = nearDuplicates
                .OrderByDescending(d => d.Similarity)
                .Take(5)
                .ToArray();

            var topDup = topDuplicates.First();
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Near Duplicate ({nearDuplicates.Count} pages, {topDup.Similarity:F0}% similar)")
                .Set("DuplicateOf", topDup.Url)
                .Set("ContentHash", simHash.ToString("X16")[..12])
                .Set("Similarity", (int)topDup.Similarity)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }
    
    /// <summary>
    /// Check content-to-boilerplate ratio on important pages.
    /// Pages with mostly navigation/footer/boilerplate are thin content signals.
    /// </summary>
    private async Task CheckBoilerplateRatioAsync(UrlContext ctx)
    {
        // Only check important pages (depth <= 2)
        if (ctx.Metadata.Depth > 2)
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Get total body content
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode == null)
            {
                return;
            }

            var totalBodyText = bodyNode.InnerText ?? "";
            var totalBodyLength = Regex.Replace(totalBodyText, @"\s+", " ").Trim().Length;

            if (totalBodyLength < 100) // Skip very small pages
            {
                return;
            }

            // Try to identify main content area by removing common boilerplate elements
            // Remove header, footer, nav, sidebar, aside
            var boilerplateSelectors = new[] { "//header", "//footer", "//nav", "//aside", "//sidebar", "//*[@role='navigation']", "//*[@role='banner']", "//*[@role='contentinfo']" };
            
            var docCopy = new HtmlDocument();
            docCopy.LoadHtml(ctx.RenderedHtml);
            
            foreach (var selector in boilerplateSelectors)
            {
                var nodesToRemove = docCopy.DocumentNode.SelectNodes(selector);
                if (nodesToRemove != null)
                {
                    foreach (var node in nodesToRemove)
                    {
                        node.Remove();
                    }
                }
            }

            // Get main content after removing boilerplate
            var bodyAfterRemoval = docCopy.DocumentNode.SelectSingleNode("//body");
            var mainContentText = bodyAfterRemoval?.InnerText ?? "";
            var mainContentLength = Regex.Replace(mainContentText, @"\s+", " ").Trim().Length;

            // Calculate ratio: main_content / total_body
            double contentRatio = totalBodyLength > 0 ? (double)mainContentLength / totalBodyLength : 0;

            // Report if ratio is poor (less than 40% main content)
            if (contentRatio < 0.4)
            {
                var boilerplatePercentage = (int)((1 - contentRatio) * 100);

                var row = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", $"High Boilerplate Ratio ({boilerplatePercentage}%)")
                    .Set("DuplicateOf", "")
                    .Set("ContentHash", "")
                    .Set("Similarity", boilerplatePercentage)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
                    
                _logger.LogDebug("High boilerplate ratio on {Url}: {Ratio:P0} main content", ctx.Url, contentRatio);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking boilerplate ratio for {Url}", ctx.Url);
        }
    }
    
    /// <summary>
    /// Check domain and protocol variants to ensure proper 301 redirects are in place.
    /// This prevents duplicate content being served from multiple domain/protocol combinations.
    /// </summary>
    private async Task CheckDomainVariantsAsync(UrlContext ctx, CancellationToken ct)
    {
        try
        {
            // Use the project's base URL (homepage) as the canonical URL for variant checking
            // Domain variants should redirect to the homepage variant, not to whatever page
            // happened to be processed first during the crawl
            var canonicalUrl = new Uri(ctx.Project.BaseUrl);
            
            _logger.LogInformation("Checking domain variants for canonical URL: {CanonicalUrl}", canonicalUrl);
            
            // Generate variants to test
            var variants = GenerateDomainVariants(canonicalUrl);
            
            // Test each variant
            foreach (var variant in variants)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }
                
                await TestDomainVariantAsync(ctx, variant, canonicalUrl.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckDomainVariantsAsync for {Url}", ctx.Url);
        }
    }
    
    /// <summary>
    /// Generate domain/protocol variants to test.
    /// For example, if canonical is https://lee.uk, generates:
    /// - http://lee.uk
    /// - https://www.lee.uk
    /// - http://www.lee.uk
    /// </summary>
    private List<string> GenerateDomainVariants(Uri canonicalUrl)
    {
        var variants = new List<string>();
        var scheme = canonicalUrl.Scheme;
        var host = canonicalUrl.Host;
        var isWww = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        
        // Determine the alternate protocol
        var alternateScheme = scheme == "https" ? "http" : "https";
        
        // Determine the alternate host (www vs non-www)
        string alternateHost;
        if (isWww)
        {
            // Remove www.
            alternateHost = host.Substring(4);
        }
        else
        {
            // Add www.
            alternateHost = "www." + host;
        }
        
        // Generate variants:
        // 1. Alternate protocol with same host
        variants.Add($"{alternateScheme}://{host}/");
        
        // 2. Same protocol with alternate host
        variants.Add($"{scheme}://{alternateHost}/");
        
        // 3. Alternate protocol with alternate host
        variants.Add($"{alternateScheme}://{alternateHost}/");
        
        return variants;
    }
    
    /// <summary>
    /// Test a domain variant and report findings.
    /// </summary>
    private async Task TestDomainVariantAsync(UrlContext ctx, string variantUrl, string canonicalUrl, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Testing domain variant: {VariantUrl}", variantUrl);
            
            using var request = new HttpRequestMessage(HttpMethod.Get, variantUrl);
            // Use the project's configured User-Agent (respects Chrome/Firefox/Edge/Safari/Random setting)
            request.Headers.Add("User-Agent", ctx.Project.UserAgent);
            
            HttpResponseMessage response;
            
            try
            {
                response = await HttpClient.SendAsync(request, ct);
            }
            catch (HttpRequestException)
            {
                // Unable to connect or DNS error
                var row1 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Domain Variant Unreachable")
                    .Set("DuplicateOf", variantUrl)
                    .Set("ContentHash", "")
                    .Set("Similarity", 0)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row1, ctx.Metadata.UrlId, default);
                return;
            }
            catch (TaskCanceledException)
            {
                var row2 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Domain Variant Timeout")
                    .Set("DuplicateOf", variantUrl)
                    .Set("ContentHash", "")
                    .Set("Similarity", 0)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row2, ctx.Metadata.UrlId, default);
                return;
            }
            
            // Use response and dispose it properly
            using (response)
            {
                var statusCode = (int)response.StatusCode;
            
            // Check if it's a permanent redirect (301 or 308)
            if (statusCode == 301 || statusCode == 308)
            {
                var locationHeader = response.Headers.Location?.ToString();
                
                // Normalize URLs for comparison
                var normalizedLocation = NormalizeUrlForComparison(locationHeader);
                var normalizedCanonical = NormalizeUrlForComparison(canonicalUrl);
                
                if (normalizedLocation == normalizedCanonical)
                {
                    // Correct! Permanent redirect to canonical URL
                    var row3 = ReportRow.Create()
                        .Set("Page", ctx.Url.ToString())
                        .Set("Issue", $"Domain Variant Correct ({statusCode})")
                        .Set("DuplicateOf", variantUrl)
                        .Set("ContentHash", "")
                        .Set("Similarity", 100)
                        .Set("Severity", "Info");
                    
                    await ctx.Reports.ReportAsync(Key, row3, ctx.Metadata.UrlId, default);
                }
                else
                {
                    var row4 = ReportRow.Create()
                        .Set("Page", ctx.Url.ToString())
                        .Set("Issue", "Domain Variant Wrong Target")
                        .Set("DuplicateOf", variantUrl)
                        .Set("ContentHash", "")
                        .Set("Similarity", 100)
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, row4, ctx.Metadata.UrlId, default);
                }
            }
            // Check if it's a temporary redirect (should be permanent)
            else if (statusCode >= 300 && statusCode < 400)
            {
                var row5 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", $"Domain Variant Temporary Redirect ({statusCode})")
                    .Set("DuplicateOf", variantUrl)
                    .Set("ContentHash", "")
                    .Set("Similarity", 100)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, row5, ctx.Metadata.UrlId, default);
            }
            // Check if it returns 200 OK (duplicate content!)
            else if (statusCode == 200)
            {
                var row6 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Domain Variant Duplicate Content")
                    .Set("DuplicateOf", variantUrl)
                    .Set("ContentHash", "")
                    .Set("Similarity", 100)
                    .Set("Severity", "Error");
                
                await ctx.Reports.ReportAsync(Key, row6, ctx.Metadata.UrlId, default);
            }
            else
            {
                var row7 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", $"Domain Variant Error (HTTP {statusCode})")
                    .Set("DuplicateOf", variantUrl)
                    .Set("ContentHash", "")
                    .Set("Similarity", 0)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row7, ctx.Metadata.UrlId, default);
            }
            } // End of using block for response
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error testing domain variant {VariantUrl}", variantUrl);
        }
    }
    
    /// <summary>
    /// Normalize URL for comparison (remove trailing slashes, lowercase)
    /// </summary>
    private string NormalizeUrlForComparison(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }
        
        try
        {
            var uri = new Uri(url);
            // Normalize: lowercase scheme and host, remove trailing slash from path
            var normalized = $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.TrimEnd('/')}";
            
            // Include query string if present
            if (!string.IsNullOrEmpty(uri.Query))
            {
                normalized += uri.Query;
            }
            
            return normalized;
        }
        catch
        {
            return url.TrimEnd('/').ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Get human-readable redirect type name
    /// </summary>
    private string GetRedirectTypeName(int statusCode)
    {
        return statusCode switch
        {
            301 => "301 Permanent",
            302 => "302 Found (Temporary)",
            303 => "303 See Other",
            307 => "307 Temporary Redirect",
            308 => "308 Permanent Redirect",
            _ => $"{statusCode}"
        };
    }
    
    /// <summary>
    /// Gets cached redirects for a project, loading them once if not cached.
    /// This prevents loading all redirects repeatedly for every URL (critical for performance).
    /// Thread-safe: uses semaphore to ensure only one thread loads data per project.
    /// </summary>
    private async Task<Dictionary<string, List<RedirectInfo>>> GetOrLoadRedirectCacheAsync(int projectId)
    {
        // Fast path: check if already cached
        if (RedirectCacheByProject.TryGetValue(projectId, out var cachedRedirects))
        {
            return cachedRedirects;
        }
        
        // Get or create semaphore for this project
        var semaphore = RedirectLoadingSemaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        
        // Wait for exclusive access to load redirects
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check: another thread might have loaded while we waited
            if (RedirectCacheByProject.TryGetValue(projectId, out cachedRedirects))
            {
                return cachedRedirects;
            }
            
            // Load and cache redirects (only one thread gets here)
            _logger.LogDebug("Loading redirects for project {ProjectId} (first time)", projectId);
            var redirectLookup = new Dictionary<string, List<RedirectInfo>>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var redirect in _repositoryAccessor.GetRedirectsAsync(projectId))
            {
                if (!redirectLookup.ContainsKey(redirect.SourceUrl))
                {
                    redirectLookup[redirect.SourceUrl] = new List<RedirectInfo>();
                }
                redirectLookup[redirect.SourceUrl].Add(redirect);
            }
            
            // Cache for future use
            RedirectCacheByProject[projectId] = redirectLookup;
            _logger.LogInformation("Cached {Count} redirect entries for project {ProjectId}", redirectLookup.Count, projectId);
            
            return redirectLookup;
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// Check if URLs are in redirect relationships and categorize by redirect type.
    /// </summary>
    private async Task<RedirectRelationshipInfo> CheckRedirectRelationshipsAsync(int projectId, string currentUrl, List<string> otherUrls)
    {
        var info = new RedirectRelationshipInfo();
        
        try
        {
            // Use cached redirects - load once per project instead of per URL (massive performance improvement)
            var redirectLookup = await GetOrLoadRedirectCacheAsync(projectId);
            
            // For each other URL, check if there's a redirect relationship
            foreach (var otherUrl in otherUrls)
            {
                // Check if currentUrl redirects to otherUrl
                if (redirectLookup.TryGetValue(currentUrl, out var redirectsFromCurrent))
                {
                    var finalRedirect = redirectsFromCurrent.OrderBy(r => r.Position).LastOrDefault();
                    if (finalRedirect != null)
                    {
                        var normalizedFinal = UrlHelper.Normalize(finalRedirect.ToUrl);
                        var normalizedOther = UrlHelper.Normalize(otherUrl);
                        
                        if (normalizedFinal == normalizedOther)
                        {
                            // currentUrl redirects to otherUrl
                            if (finalRedirect.IsPermanent)
                            {
                                info.PermanentRedirects.Add(otherUrl);
                            }
                            else
                            {
                                info.TemporaryRedirects.Add(otherUrl);
                            }
                            continue;
                        }
                    }
                }
                
                // Check if otherUrl redirects to currentUrl
                if (redirectLookup.TryGetValue(otherUrl, out var redirectsFromOther))
                {
                    var finalRedirect = redirectsFromOther.OrderBy(r => r.Position).LastOrDefault();
                    if (finalRedirect != null)
                    {
                        var normalizedFinal = UrlHelper.Normalize(finalRedirect.ToUrl);
                        var normalizedCurrent = UrlHelper.Normalize(currentUrl);
                        
                        if (normalizedFinal == normalizedCurrent)
                        {
                            // otherUrl redirects to currentUrl
                            if (finalRedirect.IsPermanent)
                            {
                                info.PermanentRedirects.Add(otherUrl);
                            }
                            else
                            {
                                info.TemporaryRedirects.Add(otherUrl);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking redirect relationships for duplicate content detection");
        }
        
        return info;
    }
    
    /// <summary>
    /// Helper class to track redirect relationships.
    /// </summary>
    private class RedirectRelationshipInfo
    {
        public HashSet<string> PermanentRedirects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TemporaryRedirects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasRedirects => PermanentRedirects.Any() || TemporaryRedirects.Any();
    }
    
    /// <summary>
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        ContentHashesByProject.TryRemove(projectId, out _);
        SimHashesByProject.TryRemove(projectId, out _);
        DomainVariantsCheckedByProject.TryRemove(projectId, out _);
        RedirectCacheByProject.TryRemove(projectId, out _);
        
        // Cleanup and dispose semaphore
        if (RedirectLoadingSemaphores.TryRemove(projectId, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up duplicate content data for project {ProjectId}", projectId);
    }
}


