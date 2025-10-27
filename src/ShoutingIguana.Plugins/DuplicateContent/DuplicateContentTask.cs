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
/// </summary>
public class DuplicateContentTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    
    // Track content hashes per project for duplicate detection
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> ContentHashesByProject = new();
    
    // Track SimHashes per project for near-duplicate detection
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<ulong, List<string>>> SimHashesByProject = new();

    public override string Key => "DuplicateContent";
    public override string DisplayName => "Duplicate Content";
    public override string Description => "Identifies exact and near-duplicate content across your pages";
    public override int Priority => 50; // Run after basic analysis

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
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
    /// Cleanup per-project data when project is closed.
    /// </summary>
    public override void CleanupProject(int projectId)
    {
        ContentHashesByProject.TryRemove(projectId, out _);
        SimHashesByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up duplicate content data for project {ProjectId}", projectId);
    }
}

