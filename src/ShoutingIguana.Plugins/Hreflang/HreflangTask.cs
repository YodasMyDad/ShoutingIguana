using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Hreflang;

/// <summary>
/// International SEO: hreflang validation, bidirectional links, language/region codes.
/// </summary>
public class HreflangTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    
    // Track hreflang relationships per project for bidirectional validation
    // ProjectId -> (PageUrl -> List of hreflang targets)
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<HreflangLink>>> HreflangsByProject = new();

    public override string Key => "Hreflang";
    public override string DisplayName => "International (Hreflang)";
    public override string Description => "Validates hreflang implementation for multi-language and multi-region sites";
    public override int Priority => 35;

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

        // Only analyze successful pages
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Only analyze internal URLs
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract hreflang links from HTML
            var htmlHreflangs = ExtractHtmlHreflangs(doc, ctx.Url);
            
            // Extract hreflang from HTTP headers (Link header)
            var httpHreflangs = ExtractHttpHreflangs(ctx);
            
            // Combine both sources
            var allHreflangs = htmlHreflangs.Concat(httpHreflangs).ToList();

            if (!allHreflangs.Any())
            {
                // No hreflang tags - this is fine for single-language sites
                return;
            }

            // Track hreflang relationships for bidirectional validation
            TrackHreflangs(ctx.Project.ProjectId, ctx.Url.ToString(), allHreflangs);

            // Validate hreflang implementation
            await ValidateHreflangSyntaxAsync(ctx, allHreflangs);
            await ValidateSelfReferencingAsync(ctx, allHreflangs);
            await ValidateDuplicatesAsync(ctx, allHreflangs);
            await ValidateXDefaultAsync(ctx, allHreflangs);
            await ValidateConsistencyAsync(ctx, htmlHreflangs, httpHreflangs);
            await ValidateCanonicalConflictsAsync(ctx, allHreflangs);
            await ValidateBidirectionalLinksAsync(ctx, allHreflangs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing hreflang for {Url}", ctx.Url);
        }
    }

    private List<HreflangLink> ExtractHtmlHreflangs(HtmlDocument doc, Uri currentUrl)
    {
        var hreflangs = new List<HreflangLink>();
        
        var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate'][@hreflang]");
        if (linkNodes != null)
        {
            foreach (var node in linkNodes)
            {
                var hreflang = node.GetAttributeValue("hreflang", "");
                var href = node.GetAttributeValue("href", "");
                
                if (!string.IsNullOrWhiteSpace(hreflang) && !string.IsNullOrWhiteSpace(href))
                {
                    // Resolve relative URLs
                    var absoluteHref = UrlHelper.Resolve(currentUrl, href);
                    
                    hreflangs.Add(new HreflangLink
                    {
                        Language = hreflang.ToLowerInvariant(),
                        Url = absoluteHref,
                        Source = "HTML"
                    });
                }
            }
        }
        
        return hreflangs;
    }

    private List<HreflangLink> ExtractHttpHreflangs(UrlContext ctx)
    {
        var hreflangs = new List<HreflangLink>();
        
        // Check Link header for hreflang
        if (ctx.Headers.TryGetValue("link", out var linkHeader))
        {
            // Parse Link header format: <url>; rel="alternate"; hreflang="en-US"
            var linkPattern = @"<([^>]+)>;\s*rel=""alternate"";\s*hreflang=""([^""]+)""";
            var matches = Regex.Matches(linkHeader, linkPattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var url = match.Groups[1].Value;
                    var hreflang = match.Groups[2].Value;
                    
                    hreflangs.Add(new HreflangLink
                    {
                        Language = hreflang.ToLowerInvariant(),
                        Url = url,
                        Source = "HTTP Header"
                    });
                }
            }
        }
        
        return hreflangs;
    }

    private void TrackHreflangs(int projectId, string pageUrl, List<HreflangLink> hreflangs)
    {
        var projectHreflangs = HreflangsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<HreflangLink>>());
        projectHreflangs[pageUrl] = hreflangs;
    }

    private async Task ValidateHreflangSyntaxAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        var invalidSyntax = new List<(HreflangLink Link, string Issue)>();
        
        // ISO 639-1 language codes (2 letters)
        var validLanguageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "en", "es", "fr", "de", "it", "pt", "nl", "pl", "ru", "ja", "zh", "ko", "ar", "hi", "tr", "sv", "da", "fi", "no", "cs", "hu", "ro", "th", "vi", "id", "ms", "el", "he", "uk", "bg", "hr", "sk", "sl", "sr", "lt", "lv", "et", "is", "ga", "mt", "cy", "eu", "ca", "gl", "af", "sq", "am", "hy", "az", "be", "bn", "bs", "my", "km", "ka", "ka", "gu", "ha", "ig", "kn", "kk", "rw", "ky", "lo", "lb", "mk", "mg", "ml", "mr", "mn", "ne", "or", "ps", "pa", "fa", "sm", "gd", "sn", "sd", "si", "so", "su", "sw", "tg", "ta", "tt", "te", "ti", "ts", "tk", "ur", "ug", "uz", "xh", "yi", "yo", "zu"
        };
        
        // ISO 3166-1 Alpha-2 country codes (2 letters, uppercase)
        var validRegionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "US", "GB", "CA", "AU", "NZ", "IE", "ZA", "IN", "SG", "PH", "MY", "HK", "AE", "SA", "QA", "KW", "BH", "OM", "JO", "LB", "EG", "MA", "DZ", "TN", "LY", "SD", "MX", "AR", "CL", "CO", "PE", "VE", "EC", "BO", "PY", "UY", "CR", "PA", "GT", "HN", "SV", "NI", "CU", "DO", "PR", "JM", "TT", "BS", "BB", "GY", "SR", "BZ", "FR", "DE", "IT", "ES", "PT", "NL", "BE", "CH", "AT", "SE", "NO", "DK", "FI", "PL", "CZ", "HU", "RO", "BG", "GR", "HR", "SI", "SK", "LT", "LV", "EE", "IS", "IE", "MT", "CY", "LU", "AD", "MC", "SM", "VA", "LI", "RU", "UA", "BY", "MD", "GE", "AM", "AZ", "KZ", "UZ", "TM", "KG", "TJ", "CN", "JP", "KR", "TW", "HK", "MO", "MN", "TH", "VN", "ID", "MY", "SG", "PH", "BN", "KH", "LA", "MM", "BD", "PK", "LK", "NP", "BT", "MV", "AF", "IR", "IQ", "TR", "IL", "PS", "SY", "JO", "LB", "YE", "KW", "BH", "QA", "AE", "OM", "SA"
        };

        foreach (var link in hreflangs)
        {
            var lang = link.Language;
            
            // Skip x-default
            if (lang == "x-default")
            {
                continue;
            }

            // Parse language and region
            var parts = lang.Split('-');
            var languageCode = parts[0];
            string? regionCode = parts.Length > 1 ? parts[1] : null;

            // Validate language code
            if (!validLanguageCodes.Contains(languageCode))
            {
                invalidSyntax.Add((link, $"Invalid language code '{languageCode}' (not ISO 639-1)"));
                continue;
            }

            // Validate region code if present
            if (regionCode != null)
            {
                if (regionCode.Length != 2)
                {
                invalidSyntax.Add((link, $"Region code '{regionCode}' should be 2 letters (ISO 3166-1 Alpha-2)"));
                }
                else if (!validRegionCodes.Contains(regionCode))
                {
                    invalidSyntax.Add((link, $"Invalid region code '{regionCode}' (not ISO 3166-1 Alpha-2)"));
                }
                else if (regionCode != regionCode.ToUpperInvariant())
                {
                    invalidSyntax.Add((link, $"Region code should be uppercase ('{regionCode}' should be '{regionCode.ToUpperInvariant()}')"));
                }
            }

            // Check for invalid format (more than 2 parts)
            if (parts.Length > 2)
            {
                invalidSyntax.Add((link, $"Invalid format - should be 'language' or 'language-REGION'"));
            }
        }

        if (invalidSyntax.Any())
        {
            var errorSummary = string.Join(", ", invalidSyntax.Take(3).Select(e => e.Link.Language));
            var exampleTarget = invalidSyntax.First().Link.Url ?? ctx.Url.ToString();
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Invalid Hreflang Syntax ({invalidSyntax.Count} tags)")
                .Set("HreflangTag", errorSummary)
                .Set("TargetURL", exampleTarget)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateSelfReferencingAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        var currentUrl = UrlHelper.Normalize(ctx.Url.ToString());
        var selfReference = hreflangs.FirstOrDefault(h => UrlHelper.Normalize(h.Url) == currentUrl);

        if (selfReference == null)
        {
            var languages = string.Join(", ", hreflangs.Select(h => h.Language));
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Self-Referencing Hreflang")
                .Set("HreflangTag", string.IsNullOrEmpty(languages) ? "(no hreflang)" : languages)
                .Set("TargetURL", ctx.Url.ToString())
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateDuplicatesAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        var duplicates = hreflangs
            .GroupBy(h => h.Language)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Any())
        {
            var duplicateTags = string.Join(", ", duplicates.Select(d => $"{d.Key} ({d.Count()}x)"));
            var exampleTarget = duplicates.First().FirstOrDefault()?.Url ?? ctx.Url.ToString();
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Duplicate Hreflang Codes")
                .Set("HreflangTag", duplicateTags)
                .Set("TargetURL", exampleTarget)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateXDefaultAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        var hasXDefault = hreflangs.Any(h => h.Language == "x-default");
        var hasMultipleLanguages = hreflangs.Select(h => h.Language).Distinct().Count() > 2; // More than just self-reference

        if (!hasXDefault && hasMultipleLanguages)
        {
            var targetUrl = hreflangs.FirstOrDefault()?.Url ?? ctx.Url.ToString();
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing x-default Hreflang")
                .Set("HreflangTag", "x-default")
                .Set("TargetURL", targetUrl)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task ValidateConsistencyAsync(UrlContext ctx, List<HreflangLink> htmlHreflangs, List<HreflangLink> httpHreflangs)
    {
        if (htmlHreflangs.Any() && httpHreflangs.Any())
        {
            // Both HTML and HTTP headers present - check for consistency
            var htmlSet = htmlHreflangs.Select(h => $"{h.Language}:{h.Url}").ToHashSet();
            var httpSet = httpHreflangs.Select(h => $"{h.Language}:{h.Url}").ToHashSet();

            if (!htmlSet.SetEquals(httpSet))
            {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "HTML/HTTP Hreflang Mismatch")
                .Set("HreflangTag", $"HTML: {htmlHreflangs.Count}, HTTP: {httpHreflangs.Count}")
                .Set("TargetURL", ctx.Url.ToString())
                .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task ValidateCanonicalConflictsAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;
        
        if (!string.IsNullOrEmpty(canonical))
        {
            var normalizedCanonical = UrlHelper.Normalize(canonical);
            var normalizedCurrent = UrlHelper.Normalize(ctx.Url.ToString());
            
            // If canonical points elsewhere and this page has hreflang
            if (normalizedCanonical != normalizedCurrent && hreflangs.Any())
            {
                var row = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Hreflang/Canonical Conflict")
                    .Set("HreflangTag", $"{hreflangs.Count} tags")
                    .Set("TargetURL", canonical)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
            }
        }
    }

    private async Task ValidateBidirectionalLinksAsync(UrlContext ctx, List<HreflangLink> hreflangs)
    {
        if (!HreflangsByProject.TryGetValue(ctx.Project.ProjectId, out var projectHreflangs))
        {
            return;
        }

        var missingReturnLinks = new List<(string TargetUrl, string Language)>();
        var currentUrl = UrlHelper.Normalize(ctx.Url.ToString());

        foreach (var link in hreflangs)
        {
            // Skip x-default and self-references
            if (link.Language == "x-default" || UrlHelper.Normalize(link.Url) == currentUrl)
            {
                continue;
            }

            // Check if target URL has been crawled and has hreflang back to us
            var normalizedTarget = UrlHelper.Normalize(link.Url);
            
            if (projectHreflangs.TryGetValue(normalizedTarget, out var targetHreflangs))
            {
                // Check if target links back to current page
                var hasReturnLink = targetHreflangs.Any(h => UrlHelper.Normalize(h.Url) == currentUrl);
                
                if (!hasReturnLink)
                {
                    missingReturnLinks.Add((link.Url, link.Language));
                }
            }
            // Note: If target URL not crawled yet, we can't validate (skip silently)
        }

        if (missingReturnLinks.Any())
        {
            var targetSummary = string.Join(", ", missingReturnLinks.Take(3).Select(m => m.Language));
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Missing Bidirectional Links ({missingReturnLinks.Count} targets)")
                .Set("HreflangTag", targetSummary)
                .Set("TargetURL", missingReturnLinks.First().TargetUrl)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    public override void CleanupProject(int projectId)
    {
        HreflangsByProject.TryRemove(projectId, out _);
        _logger.LogDebug("Cleaned up hreflang data for project {ProjectId}", projectId);
    }

    private class HreflangLink
    {
        public string Language { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // "HTML" or "HTTP Header"
    }
}

