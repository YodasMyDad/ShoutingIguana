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
                    var builder = FindingDetailsBuilder.Create()
                        .AddItem("Duplicate content with temporary redirects");
                    
                    builder.BeginNested("📄 Temporary redirect URLs");
                    foreach (var url in redirectInfo.TemporaryRedirects.Take(5))
                    {
                        builder.AddItem(url);
                    }
                    
                    builder.BeginNested("⚠️ Issue")
                        .AddItem("Temporary redirects (302/307) don't consolidate content")
                        .AddItem("Search engines may index both versions");
                    
                    builder.BeginNested("💡 Recommendations")
                        .AddItem("Change to 301 (Permanent) redirects")
                        .AddItem("Properly consolidates duplicate content for SEO");
                    
                    builder.WithTechnicalMetadata("url", currentUrl)
                        .WithTechnicalMetadata("temporaryRedirects", redirectInfo.TemporaryRedirects.ToArray())
                        .WithTechnicalMetadata("contentHash", contentHash);
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DUPLICATE_CONTENT_TEMPORARY_REDIRECT",
                        $"Page has duplicate content served via temporary redirect(s) which can cause duplicate content issues",
                        builder.Build());
                }
                
                // Only report true duplicates (no redirect relationship)
                if (nonRedirectDuplicates.Length > 0)
                {
                    var preview = cleanedContent.Length > 200 ? cleanedContent[..200] + "..." : cleanedContent;
                    var builder = FindingDetailsBuilder.Create()
                        .AddItem($"Found {nonRedirectDuplicates.Length} pages with exact duplicate content");
                    
                    builder.BeginNested("📄 Duplicate pages");
                    foreach (var url in nonRedirectDuplicates.Take(5))
                    {
                        builder.AddItem(url);
                    }
                    if (nonRedirectDuplicates.Length > 5)
                    {
                        builder.AddItem($"... and {nonRedirectDuplicates.Length - 5} more");
                    }
                    
                    builder.BeginNested("⚠️ Duplicate Content Issue")
                        .AddItem("Search engines may not know which version to rank")
                        .AddItem("May split ranking signals between pages")
                        .AddItem("Can lead to lower rankings for all versions");
                    
                    builder.BeginNested("💡 Recommendations")
                        .AddItem("Make each page's content unique")
                        .AddItem("Or consolidate pages with 301 redirects")
                        .AddItem("Or use canonical tags to indicate preferred version");
                    
                    builder.WithTechnicalMetadata("url", currentUrl)
                        .WithTechnicalMetadata("duplicateCount", nonRedirectDuplicates.Length)
                        .WithTechnicalMetadata("otherUrls", nonRedirectDuplicates)
                        .WithTechnicalMetadata("contentHash", contentHash)
                        .WithTechnicalMetadata("contentPreview", preview);
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Error,
                        "EXACT_DUPLICATE",
                        $"Page content is an exact duplicate of {nonRedirectDuplicates.Length} other page(s)",
                        builder.Build());
                }
            }
            else
            {
                // No redirects found, report as regular duplicate
                var preview = cleanedContent.Length > 200 ? cleanedContent[..200] + "..." : cleanedContent;
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {urlsCopy.Count - 1} pages with exact duplicate content");
                
                builder.BeginNested("📄 Duplicate pages");
                foreach (var url in otherUrls.Take(5))
                {
                    builder.AddItem(url);
                }
                if (otherUrls.Count > 5)
                {
                    builder.AddItem($"... and {otherUrls.Count - 5} more");
                }
                
                builder.BeginNested("⚠️ Duplicate Content Issue")
                    .AddItem("Search engines may not know which version to rank")
                    .AddItem("May split ranking signals between pages")
                    .AddItem("Can lead to lower rankings for all versions");
                
                builder.BeginNested("💡 Recommendations")
                    .AddItem("Make each page's content unique")
                    .AddItem("Or consolidate pages with 301 redirects")
                    .AddItem("Or use canonical tags to indicate preferred version");
                
                builder.WithTechnicalMetadata("url", currentUrl)
                    .WithTechnicalMetadata("duplicateCount", urlsCopy.Count - 1)
                    .WithTechnicalMetadata("otherUrls", otherUrls.ToArray())
                    .WithTechnicalMetadata("contentHash", contentHash)
                    .WithTechnicalMetadata("contentPreview", preview);
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "EXACT_DUPLICATE",
                    $"Page content is an exact duplicate of {urlsCopy.Count - 1} other page(s)",
                    builder.Build());
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

            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Found {nearDuplicates.Count} pages with very similar content")
                .AddItem("Threshold: >95% similar (Hamming distance < 3)");
            
            builder.BeginNested("📄 Similar pages");
            foreach (var dup in topDuplicates)
            {
                builder.AddItem($"{dup.Url} ({dup.Similarity:F1}% similar)");
            }
            
            builder.BeginNested("⚠️ Near-Duplicate Impact")
                .AddItem("Very similar content can confuse search engines")
                .AddItem("May split ranking between similar pages");
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Consider consolidating similar pages")
                .AddItem("Or differentiate content to make each page unique")
                .AddItem("Add unique sections, examples, or perspectives");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("nearDuplicateCount", nearDuplicates.Count)
                .WithTechnicalMetadata("topDuplicates", topDuplicates.Select(d => new { url = d.Url, similarity = d.Similarity }).ToArray())
                .WithTechnicalMetadata("simHash", simHash.ToString("X16"));
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NEAR_DUPLICATE",
                $"Page content is very similar to {nearDuplicates.Count} other page(s)",
                builder.Build());
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
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Variant: {variantUrl}")
                    .AddItem($"Canonical: {canonicalUrl}")
                    .AddItem($"Error: {ex.Message}")
                    .AddItem("ℹ️ This variant doesn't resolve - fine if intentional")
                    .WithTechnicalMetadata("variantUrl", variantUrl)
                    .WithTechnicalMetadata("canonicalUrl", canonicalUrl)
                    .WithTechnicalMetadata("error", ex.Message)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_UNREACHABLE",
                    $"Domain variant {variantUrl} is unreachable",
                    details);
                return;
            }
            catch (TaskCanceledException)
            {
                // Timeout
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> {
                        ["variantUrl"] = variantUrl,
                        ["canonicalUrl"] = canonicalUrl
                    },
                    $"Variant: {variantUrl}",
                    "⏱️ Timed out - did not respond");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_TIMEOUT",
                    $"Domain variant {variantUrl} timed out",
                    details);
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
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"Variant: {variantUrl}")
                        .AddItem($"Redirects to: {locationHeader}")
                        .AddItem($"Type: {(statusCode == 301 ? "301 Permanent" : "308 Permanent")}")
                        .AddItem("✅ Correct - prevents duplicate content")
                        .WithTechnicalMetadata("variantUrl", variantUrl)
                        .WithTechnicalMetadata("redirectsTo", locationHeader)
                        .WithTechnicalMetadata("statusCode", statusCode)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "DOMAIN_VARIANT_CORRECT_REDIRECT",
                        $"✓ Domain variant correctly redirects: {variantUrl} → {locationHeader} (HTTP {statusCode})",
                        details);
                }
                else
                {
                    // Redirects, but not to the canonical URL
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"Variant: {variantUrl}")
                        .AddItem($"Redirects to: {locationHeader}")
                        .AddItem($"Expected: {canonicalUrl}")
                        .BeginNested("💡 Recommendations")
                            .AddItem("Ensure variant redirects to the canonical URL")
                            .AddItem("Fix redirect target")
                        .WithTechnicalMetadata("variantUrl", variantUrl)
                        .WithTechnicalMetadata("redirectsTo", locationHeader)
                        .WithTechnicalMetadata("expectedTarget", canonicalUrl)
                        .WithTechnicalMetadata("statusCode", statusCode)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DOMAIN_VARIANT_WRONG_TARGET",
                        $"Domain variant redirects to unexpected URL: {variantUrl} → {locationHeader}",
                        details);
                }
            }
            // Check if it's a temporary redirect (should be permanent)
            else if (statusCode >= 300 && statusCode < 400)
            {
                var locationHeader = response.Headers.Location?.ToString();
                
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Variant: {variantUrl}")
                    .AddItem($"Redirects to: {locationHeader}")
                    .AddItem($"Type: {GetRedirectTypeName(statusCode)}")
                    .BeginNested("❌ Wrong Redirect Type")
                        .AddItem("Temporary redirects (302/307) don't pass SEO value")
                        .AddItem("May cause duplicate content issues");
                
                details.BeginNested("💡 Recommendations")
                        .AddItem("Change to 301 (Permanent) redirect")
                        .AddItem("Consolidates domain variants properly");
                
                details.WithTechnicalMetadata("variantUrl", variantUrl)
                    .WithTechnicalMetadata("redirectsTo", locationHeader)
                    .WithTechnicalMetadata("statusCode", statusCode)
                    .WithTechnicalMetadata("redirectType", GetRedirectTypeName(statusCode))
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "DOMAIN_VARIANT_WRONG_REDIRECT_TYPE",
                    $"✗ Domain variant uses temporary redirect instead of 301: {variantUrl} → {locationHeader} (HTTP {statusCode})",
                    details);
            }
            // Check if it returns 200 OK (duplicate content!)
            else if (statusCode == 200)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Variant: {variantUrl}")
                    .AddItem($"Canonical: {canonicalUrl}")
                    .AddItem("❌ DUPLICATE CONTENT DETECTED")
                    .BeginNested("⚠️ Critical Issue")
                        .AddItem("Same content accessible from multiple URLs")
                        .AddItem("Search engines may split ranking signals")
                        .AddItem("Reduces overall rankings for all variants");
                
                details.BeginNested("💡 Recommendations")
                        .AddItem($"Add 301 redirect from {variantUrl} to {canonicalUrl}")
                        .AddItem("Configure server to redirect all variants")
                        .AddItem("Test all combinations (http/https, www/non-www)");
                
                details.WithTechnicalMetadata("variantUrl", variantUrl)
                    .WithTechnicalMetadata("canonicalUrl", canonicalUrl)
                    .WithTechnicalMetadata("statusCode", statusCode)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "DOMAIN_VARIANT_DUPLICATE_CONTENT",
                    $"✗ DUPLICATE CONTENT: Domain variant serves content without redirecting: {variantUrl}",
                    details);
            }
            else
            {
                // Other status codes (4xx, 5xx)
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> {
                        ["variantUrl"] = variantUrl,
                        ["statusCode"] = statusCode
                    },
                    $"Variant: {variantUrl}",
                    $"HTTP {statusCode}",
                    "ℹ️ Variant returns an error - ensure this is intentional");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "DOMAIN_VARIANT_ERROR",
                    $"Domain variant returns error: {variantUrl} (HTTP {statusCode})",
                    details);
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
    /// Check if URLs are in redirect relationships and categorize by redirect type.
    /// </summary>
    private async Task<RedirectRelationshipInfo> CheckRedirectRelationshipsAsync(int projectId, string currentUrl, List<string> otherUrls)
    {
        var info = new RedirectRelationshipInfo();
        
        try
        {
            // Build lookup of all redirects for efficient checking
            var redirectLookup = new Dictionary<string, List<RedirectInfo>>(StringComparer.OrdinalIgnoreCase);
            
            await foreach (var redirect in _repositoryAccessor.GetRedirectsAsync(projectId))
            {
                if (!redirectLookup.ContainsKey(redirect.SourceUrl))
                {
                    redirectLookup[redirect.SourceUrl] = new List<RedirectInfo>();
                }
                redirectLookup[redirect.SourceUrl].Add(redirect);
            }
            
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
        _logger.LogDebug("Cleaned up duplicate content data for project {ProjectId}", projectId);
    }
}


