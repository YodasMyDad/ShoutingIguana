using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.DuplicateContent;

/// <summary>
/// Exact and near-duplicate content detection using SHA-256 and SimHash algorithms.
/// Also checks domain/protocol variants to ensure proper 301 redirects are in place.
/// </summary>
public class DuplicateContentTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
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
        projectHashes.AddOrUpdate(
            hash,
            _ => new List<string> { url },
            (_, list) =>
            {
                lock (list)
                {
                    // Only add if not already in the list (prevent duplicate entries for same URL)
                    if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(url);
                    }
                }
                return list;
            });
    }

    private void TrackSimHash(int projectId, ulong simHash, string url)
    {
        var projectSimHashes = SimHashesByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<ulong, List<string>>());
        projectSimHashes.AddOrUpdate(
            simHash,
            _ => new List<string> { url },
            (_, list) =>
            {
                lock (list)
                {
                    // Only add if not already in the list (prevent duplicate entries for same URL)
                    if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(url);
                    }
                }
                return list;
            });
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
            var otherUrls = urlsCopy.Where(u => u != ctx.Url.ToString()).Take(10).ToArray();

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "EXACT_DUPLICATE",
                $"Page content is an exact duplicate of {urlsCopy.Count - 1} other page(s)",
                new
                {
                    url = ctx.Url.ToString(),
                    duplicateCount = urlsCopy.Count - 1,
                    otherUrls,
                    contentHash,
                    contentPreview = cleanedContent.Length > 200 ? cleanedContent[..200] + "..." : cleanedContent,
                    recommendation = "Each page should have unique content to avoid duplicate content issues"
                });
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

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NEAR_DUPLICATE",
                $"Page content is very similar to {nearDuplicates.Count} other page(s)",
                new
                {
                    url = ctx.Url.ToString(),
                    nearDuplicateCount = nearDuplicates.Count,
                    topDuplicates = topDuplicates.Select(d => new
                    {
                        url = d.Url,
                        similarity = $"{d.Similarity:F1}%"
                    }).ToArray(),
                    simHash = simHash.ToString("X16"),
                    threshold = "Hamming distance < 3 (>95% similar)",
                    recommendation = "Consider consolidating similar pages or making content more unique"
                });
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
            // Get the canonical URL from the first URL being processed
            var canonicalUrl = ctx.Url;
            
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
            
            var request = new HttpRequestMessage(HttpMethod.Get, variantUrl);
            request.Headers.Add("User-Agent", "ShoutingIguana/1.0 (SEO Crawler)");
            
            HttpResponseMessage response;
            
            try
            {
                response = await HttpClient.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                // Unable to connect or DNS error
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_UNREACHABLE",
                    $"Domain variant {variantUrl} is unreachable",
                    new
                    {
                        variantUrl,
                        canonicalUrl,
                        error = ex.Message,
                        note = "This variant doesn't resolve - this is fine if intentional"
                    });
                return;
            }
            catch (TaskCanceledException)
            {
                // Timeout
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_TIMEOUT",
                    $"Domain variant {variantUrl} timed out",
                    new
                    {
                        variantUrl,
                        canonicalUrl,
                        note = "Variant did not respond within timeout period"
                    });
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
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "DOMAIN_VARIANT_CORRECT_REDIRECT",
                        $"✓ Domain variant correctly redirects: {variantUrl} → {locationHeader} (HTTP {statusCode})",
                        new
                        {
                            variantUrl,
                            redirectsTo = locationHeader,
                            statusCode,
                            redirectType = statusCode == 301 ? "301 Permanent" : "308 Permanent Redirect",
                            status = "Correct - prevents duplicate content"
                        });
                }
                else
                {
                    // Redirects, but not to the canonical URL
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DOMAIN_VARIANT_WRONG_TARGET",
                        $"Domain variant redirects to unexpected URL: {variantUrl} → {locationHeader}",
                        new
                        {
                            variantUrl,
                            redirectsTo = locationHeader,
                            expectedTarget = canonicalUrl,
                            statusCode,
                            recommendation = "Ensure variant redirects to the canonical URL"
                        });
                }
            }
            // Check if it's a temporary redirect (should be permanent)
            else if (statusCode >= 300 && statusCode < 400)
            {
                var locationHeader = response.Headers.Location?.ToString();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "DOMAIN_VARIANT_WRONG_REDIRECT_TYPE",
                    $"✗ Domain variant uses temporary redirect instead of 301: {variantUrl} → {locationHeader} (HTTP {statusCode})",
                    new
                    {
                        variantUrl,
                        redirectsTo = locationHeader,
                        statusCode,
                        redirectType = GetRedirectTypeName(statusCode),
                        issue = "Temporary redirects (302/307) don't pass SEO value and may cause duplicate content issues",
                        recommendation = "Change to 301 (Permanent) redirect to properly consolidate domain variants"
                    });
            }
            // Check if it returns 200 OK (duplicate content!)
            else if (statusCode == 200)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "DOMAIN_VARIANT_DUPLICATE_CONTENT",
                    $"✗ DUPLICATE CONTENT: Domain variant serves content without redirecting: {variantUrl}",
                    new
                    {
                        variantUrl,
                        canonicalUrl,
                        statusCode,
                        issue = "Same content is accessible from multiple URLs, creating duplicate content",
                        seoImpact = "Search engines may split ranking signals between variants, reducing overall rankings",
                        recommendation = $"Add 301 redirect from {variantUrl} to {canonicalUrl}"
                    });
            }
            else
            {
                // Other status codes (4xx, 5xx)
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_ERROR",
                    $"Domain variant returns error: {variantUrl} (HTTP {statusCode})",
                    new
                    {
                        variantUrl,
                        statusCode,
                        note = "Variant returns an error - ensure this is intentional"
                    });
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
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        ContentHashesByProject.TryRemove(projectId, out _);
        SimHashesByProject.TryRemove(projectId, out _);
        DomainVariantsCheckedByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up duplicate content data for project {ProjectId}", projectId);
    }
}

