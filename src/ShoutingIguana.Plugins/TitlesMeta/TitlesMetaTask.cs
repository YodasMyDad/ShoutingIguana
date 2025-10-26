using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.TitlesMeta;

/// <summary>
/// Title, meta, Open Graph, Twitter Cards, and heading structure validation.
/// </summary>
public class TitlesMetaTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MIN_TITLE_LENGTH = 30;
    private const int MAX_TITLE_LENGTH = 60;
    private const int MAX_TITLE_WARNING_LENGTH = 70;
    private const int MIN_DESCRIPTION_LENGTH = 50;
    private const int MAX_DESCRIPTION_LENGTH = 160;
    private const int MAX_DESCRIPTION_WARNING_LENGTH = 200;
    
    // For duplicate detection across pages
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> _titlesByProject = new();
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> _descriptionsByProject = new();

    public override string Key => "TitlesMeta";
    public override string DisplayName => "Titles & Meta";
    public override int Priority => 30;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract basic meta elements (crawler already parsed canonical and robots)
            var title = ExtractTitle(doc);
            var description = ExtractMetaDescription(doc);
            var viewport = ExtractViewport(doc);
            var charset = ExtractCharset(doc);
            var language = ExtractLanguage(doc);

            // Analyze title
            await AnalyzeTitleAsync(ctx, title);
            
            // Track for duplicate detection
            TrackTitleAndDescription(ctx.Project.ProjectId, title, description, ctx.Url.ToString());
            
            // Analyze description
            await AnalyzeDescriptionAsync(ctx, description);
            
            // Analyze canonical (using crawler-parsed data)
            await AnalyzeCanonicalAsync(ctx);
            
            // Analyze robots meta (using crawler-parsed data)
            await AnalyzeRobotsAsync(ctx);
            
            // Analyze viewport
            await AnalyzeViewportAsync(ctx, viewport);
            
            // Analyze charset
            await AnalyzeCharsetAsync(ctx, doc, charset);
            
            // Analyze language
            await AnalyzeLanguageAsync(ctx, language);
            
            // Analyze Open Graph tags
            await AnalyzeOpenGraphAsync(ctx, doc);
            
            // Analyze Twitter Cards
            await AnalyzeTwitterCardsAsync(ctx, doc);
            
            // Analyze heading structure
            await AnalyzeHeadingStructureAsync(ctx, doc, title);
            
            // Check for outdated meta keywords
            await CheckMetaKeywordsAsync(ctx, doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing titles/meta for {Url}", ctx.Url);
        }
    }

    private string ExtractTitle(HtmlDocument doc)
    {
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        return titleNode?.InnerText?.Trim() ?? "";
    }

    private string ExtractMetaDescription(HtmlDocument doc)
    {
        var descNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
        return descNode?.GetAttributeValue("content", "")?.Trim() ?? "";
    }


    private string ExtractViewport(HtmlDocument doc)
    {
        var viewportNode = doc.DocumentNode.SelectSingleNode("//meta[@name='viewport']");
        return viewportNode?.GetAttributeValue("content", "")?.Trim() ?? "";
    }

    private string ExtractCharset(HtmlDocument doc)
    {
        var charsetNode = doc.DocumentNode.SelectSingleNode("//meta[@charset]");
        if (charsetNode != null)
        {
            return charsetNode.GetAttributeValue("charset", "");
        }
        
        var httpEquivNode = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='Content-Type']");
        if (httpEquivNode != null)
        {
            var content = httpEquivNode.GetAttributeValue("content", "");
            var match = Regex.Match(content, @"charset=([^;\s]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        
        return "";
    }

    private string ExtractLanguage(HtmlDocument doc)
    {
        var htmlNode = doc.DocumentNode.SelectSingleNode("//html");
        return htmlNode?.GetAttributeValue("lang", "")?.Trim() ?? "";
    }

    private void TrackTitleAndDescription(int projectId, string title, string description, string url)
    {
        if (!string.IsNullOrEmpty(title))
        {
            var projectTitles = _titlesByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<string>>());
            projectTitles.AddOrUpdate(
                title,
                _ => new List<string> { url },
                (_, list) => { lock (list) { list.Add(url); } return list; });
        }

        if (!string.IsNullOrEmpty(description))
        {
            var projectDescriptions = _descriptionsByProject.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, List<string>>());
            projectDescriptions.AddOrUpdate(
                description,
                _ => new List<string> { url },
                (_, list) => { lock (list) { list.Add(url); } return list; });
        }
    }

    private async Task AnalyzeTitleAsync(UrlContext ctx, string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MISSING_TITLE",
                "Page is missing a title tag",
                new { url = ctx.Url.ToString(), recommendation = "Every page must have a unique title tag" });
            return;
        }

        if (title.Length < MIN_TITLE_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TITLE_TOO_SHORT",
                $"Title is too short ({title.Length} chars, recommended: {MIN_TITLE_LENGTH}+)",
                new { url = ctx.Url.ToString(), title, length = title.Length });
        }
        else if (title.Length > MAX_TITLE_WARNING_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "TITLE_TOO_LONG",
                $"Title is too long ({title.Length} chars, will be truncated in search results)",
                new { url = ctx.Url.ToString(), title, length = title.Length, recommendation = $"Keep titles under {MAX_TITLE_LENGTH} characters" });
        }
        else if (title.Length > MAX_TITLE_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TITLE_LONG",
                $"Title is long ({title.Length} chars, may be truncated: recommended: <{MAX_TITLE_LENGTH})",
                new { url = ctx.Url.ToString(), title, length = title.Length });
        }

        // Check for duplicate titles (will be reported later in batch)
        if (_titlesByProject.TryGetValue(ctx.Project.ProjectId, out var projectTitles))
        {
            if (projectTitles.TryGetValue(title, out var urls))
            {
                int duplicateCount;
                string[] otherUrls;
                
                lock (urls)
                {
                    duplicateCount = urls.Count;
                    otherUrls = urls.Where(u => u != ctx.Url.ToString()).Take(5).ToArray();
                }
                
                if (duplicateCount > 1)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Error,
                        "DUPLICATE_TITLE",
                        $"Title \"{title}\" is used on multiple pages",
                        new
                        {
                            url = ctx.Url.ToString(),
                            title,
                            duplicateCount,
                            otherUrls
                        });
                }
            }
        }
    }

    private async Task AnalyzeDescriptionAsync(UrlContext ctx, string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_DESCRIPTION",
                "Page is missing a meta description",
                new { url = ctx.Url.ToString(), recommendation = "Meta descriptions improve click-through rates in search results" });
            return;
        }

        if (description.Length < MIN_DESCRIPTION_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "DESCRIPTION_TOO_SHORT",
                $"Meta description is too short ({description.Length} chars, recommended: {MIN_DESCRIPTION_LENGTH}+)",
                new { url = ctx.Url.ToString(), description, length = description.Length });
        }
        else if (description.Length > MAX_DESCRIPTION_WARNING_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "DESCRIPTION_TOO_LONG",
                $"Meta description is too long ({description.Length} chars, will be truncated)",
                new { url = ctx.Url.ToString(), description = description.Substring(0, 100) + "...", length = description.Length, recommendation = $"Keep descriptions under {MAX_DESCRIPTION_LENGTH} characters" });
        }
        else if (description.Length > MAX_DESCRIPTION_LENGTH)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "DESCRIPTION_LONG",
                $"Meta description is long ({description.Length} chars, may be truncated: recommended: <{MAX_DESCRIPTION_LENGTH})",
                new { url = ctx.Url.ToString(), description = description.Substring(0, 100) + "...", length = description.Length });
        }

        // Check for duplicate descriptions
        if (_descriptionsByProject.TryGetValue(ctx.Project.ProjectId, out var projectDescriptions))
        {
            if (projectDescriptions.TryGetValue(description, out var urls))
            {
                int duplicateCount;
                string[] otherUrls;
                
                lock (urls)
                {
                    duplicateCount = urls.Count;
                    otherUrls = urls.Where(u => u != ctx.Url.ToString()).Take(5).ToArray();
                }
                
                if (duplicateCount > 1)
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DUPLICATE_DESCRIPTION",
                        $"Meta description is used on multiple pages",
                        new
                        {
                            url = ctx.Url.ToString(),
                            description = description.Substring(0, Math.Min(description.Length, 100)) + "...",
                            duplicateCount,
                            otherUrls
                        });
                }
            }
        }
    }

    private async Task AnalyzeCanonicalAsync(UrlContext ctx)
    {
        // Use crawler-parsed canonical data
        var canonical = ctx.Metadata.CanonicalHtml ?? ctx.Metadata.CanonicalHttp;
        
        // Report multiple canonicals (crawler detected this)
        if (ctx.Metadata.HasMultipleCanonicals)
        {
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
        
        // Report cross-domain canonical
        if (ctx.Metadata.HasCrossDomainCanonical)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CROSS_DOMAIN_CANONICAL",
                $"Page canonical points to different domain: {canonical}",
                new
                {
                    url = ctx.Url.ToString(),
                    canonical,
                    note = "Cross-domain canonicals are valid for syndicated content but should be used carefully"
                });
        }
        
        if (!string.IsNullOrEmpty(canonical))
        {
            // Check if canonical points to a different URL (same domain)
            var normalizedCurrent = NormalizeUrl(ctx.Url.ToString());
            var normalizedCanonical = NormalizeUrl(canonical);

            if (normalizedCurrent != normalizedCanonical && !ctx.Metadata.HasCrossDomainCanonical)
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "CANONICAL_TO_OTHER_PAGE",
                    $"Page canonical points to different URL: {canonical}",
                    new
                    {
                        url = ctx.Url.ToString(),
                        canonical,
                        note = "This page will not be indexed; canonical page will be indexed instead"
                    });
            }
        }
    }

    private async Task AnalyzeRobotsAsync(UrlContext ctx)
    {
        // Use crawler-parsed robots data
        if (ctx.Metadata.RobotsNoindex == true)
        {
            var source = ctx.Metadata.HasRobotsConflict ? "conflicting directives (most restrictive applied)" : 
                         !string.IsNullOrEmpty(ctx.Metadata.XRobotsTag) ? "X-Robots-Tag header" : "meta robots tag";
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NOINDEX_DETECTED",
                $"Page has noindex directive (will not be indexed by search engines) - Source: {source}",
                new 
                { 
                    url = ctx.Url.ToString(), 
                    xRobotsTag = ctx.Metadata.XRobotsTag,
                    hasConflict = ctx.Metadata.HasRobotsConflict
                });
        }

        if (ctx.Metadata.RobotsNofollow == true)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOFOLLOW_DETECTED",
                "Page has nofollow directive (links will not pass equity)",
                new 
                { 
                    url = ctx.Url.ToString(),
                    xRobotsTag = ctx.Metadata.XRobotsTag,
                    hasConflict = ctx.Metadata.HasRobotsConflict
                });
        }
        
        // Report robots conflicts if detected
        if (ctx.Metadata.HasRobotsConflict)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "ROBOTS_CONFLICT",
                "Meta robots and X-Robots-Tag header have conflicting directives",
                new 
                { 
                    url = ctx.Url.ToString(),
                    xRobotsTag = ctx.Metadata.XRobotsTag,
                    note = "Most restrictive directive takes precedence"
                });
        }
    }

    private async Task AnalyzeViewportAsync(UrlContext ctx, string viewport)
    {
        if (string.IsNullOrEmpty(viewport))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_VIEWPORT",
                "No viewport meta tag (mobile compatibility issue)",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
                });
        }
    }

    private async Task AnalyzeCharsetAsync(UrlContext ctx, HtmlDocument doc, string charset)
    {
        if (string.IsNullOrEmpty(charset))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_CHARSET",
                "No charset declaration found",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add <meta charset=\"utf-8\"> as first element in <head>"
                });
            return;
        }

        // Check if charset is UTF-8 (recommended)
        if (!charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NON_UTF8_CHARSET",
                $"Charset is '{charset}' (UTF-8 is recommended)",
                new { url = ctx.Url.ToString(), charset });
        }

        // Check for multiple charset declarations
        var charsetNodes = doc.DocumentNode.SelectNodes("//meta[@charset]");
        var httpEquivCharsetNodes = doc.DocumentNode.SelectNodes("//meta[@http-equiv='Content-Type']");
        var totalCharsetDeclarations = (charsetNodes?.Count ?? 0) + (httpEquivCharsetNodes?.Count ?? 0);

        if (totalCharsetDeclarations > 1)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MULTIPLE_CHARSET_DECLARATIONS",
                $"Multiple charset declarations found ({totalCharsetDeclarations})",
                new { url = ctx.Url.ToString(), count = totalCharsetDeclarations });
        }
    }

    private async Task AnalyzeLanguageAsync(UrlContext ctx, string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_LANGUAGE",
                "No lang attribute on <html> element",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add lang attribute for accessibility and SEO (e.g., <html lang=\"en\">)"
                });
        }
    }

    private async Task AnalyzeOpenGraphAsync(UrlContext ctx, HtmlDocument doc)
    {
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "");
        var ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", "");
        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
        var ogUrl = doc.DocumentNode.SelectSingleNode("//meta[@property='og:url']")?.GetAttributeValue("content", "");
        var ogType = doc.DocumentNode.SelectSingleNode("//meta[@property='og:type']")?.GetAttributeValue("content", "");

        var ogTagCount = new[] { ogTitle, ogDescription, ogImage, ogUrl, ogType }.Count(s => !string.IsNullOrEmpty(s));

        if (ogTagCount == 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_OPEN_GRAPH",
                "No Open Graph tags found (important for social sharing)",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add og:title, og:description, og:image, og:url for better social media previews"
                });
        }
        else if (ogTagCount < 4)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(ogTitle)) missing.Add("og:title");
            if (string.IsNullOrEmpty(ogDescription)) missing.Add("og:description");
            if (string.IsNullOrEmpty(ogImage)) missing.Add("og:image");
            if (string.IsNullOrEmpty(ogUrl)) missing.Add("og:url");

            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "INCOMPLETE_OPEN_GRAPH",
                $"Open Graph tags incomplete (missing: {string.Join(", ", missing)})",
                new
                {
                    url = ctx.Url.ToString(),
                    missing = missing.ToArray(),
                    present = ogTagCount
                });
        }
    }

    private async Task AnalyzeTwitterCardsAsync(UrlContext ctx, HtmlDocument doc)
    {
        var twitterCard = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:card']")?.GetAttributeValue("content", "");
        var twitterTitle = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "");
        var twitterDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", "");
        var twitterImage = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", "");

        var twitterTagCount = new[] { twitterCard, twitterTitle, twitterDescription, twitterImage }.Count(s => !string.IsNullOrEmpty(s));

        if (twitterTagCount == 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MISSING_TWITTER_CARDS",
                "No Twitter Card tags found",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Add Twitter Card tags for better Twitter sharing (or Twitter will fall back to Open Graph)"
                });
        }
    }

    private async Task AnalyzeHeadingStructureAsync(UrlContext ctx, HtmlDocument doc, string title)
    {
        var h1Nodes = doc.DocumentNode.SelectNodes("//h1");
        var h2Nodes = doc.DocumentNode.SelectNodes("//h2");
        var h3Nodes = doc.DocumentNode.SelectNodes("//h3");
        var h4Nodes = doc.DocumentNode.SelectNodes("//h4");
        var h5Nodes = doc.DocumentNode.SelectNodes("//h5");
        var h6Nodes = doc.DocumentNode.SelectNodes("//h6");

        var h1Count = h1Nodes?.Count ?? 0;

        if (h1Count == 0)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MISSING_H1",
                "Page has no H1 tag",
                new { url = ctx.Url.ToString(), recommendation = "Every page should have exactly one H1 tag" });
        }
        else if (h1Count > 1)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MULTIPLE_H1",
                $"Page has {h1Count} H1 tags (should have exactly 1)",
                new
                {
                    url = ctx.Url.ToString(),
                    h1Count,
                    h1Texts = h1Nodes!.Take(5).Select(n => n.InnerText?.Trim()).ToArray()
                });
        }
        else
        {
            // Analyze H1 content
            var h1Text = h1Nodes![0].InnerText?.Trim() ?? "";

            if (string.IsNullOrEmpty(h1Text))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "EMPTY_H1",
                    "H1 tag is empty",
                    new { url = ctx.Url.ToString() });
            }
            else if (!string.IsNullOrEmpty(title))
            {
                // Check H1/Title alignment (should be similar for SEO)
                var similarity = CalculateSimilarity(h1Text, title);
                if (similarity < 0.3) // Less than 30% similar
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "H1_TITLE_MISMATCH",
                        "H1 and title are very different",
                        new
                        {
                            url = ctx.Url.ToString(),
                            h1 = h1Text,
                            title,
                            note = "H1 and title should be similar for better SEO"
                        });
                }
            }
        }

        // Check heading hierarchy
        var hasH2 = (h2Nodes?.Count ?? 0) > 0;
        var hasH3 = (h3Nodes?.Count ?? 0) > 0;
        var hasH4 = (h4Nodes?.Count ?? 0) > 0;
        var hasH5 = (h5Nodes?.Count ?? 0) > 0;
        var hasH6 = (h6Nodes?.Count ?? 0) > 0;

        if (!hasH2 && (hasH3 || hasH4 || hasH5 || hasH6))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SKIPPED_HEADING_LEVEL",
                "Heading hierarchy skips H2 (has H3+ but no H2)",
                new { url = ctx.Url.ToString(), recommendation = "Use headings in order: H1 -> H2 -> H3, etc." });
        }
        else if (!hasH3 && (hasH4 || hasH5 || hasH6))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SKIPPED_HEADING_LEVEL",
                "Heading hierarchy skips H3 (has H4+ but no H3)",
                new { url = ctx.Url.ToString(), recommendation = "Use headings in order: H1 -> H2 -> H3, etc." });
        }
    }

    private async Task CheckMetaKeywordsAsync(UrlContext ctx, HtmlDocument doc)
    {
        var keywordsNode = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
        if (keywordsNode != null)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "META_KEYWORDS_FOUND",
                "Meta keywords tag found (outdated, not used by search engines)",
                new
                {
                    url = ctx.Url.ToString(),
                    recommendation = "Remove meta keywords tag - it's not used by modern search engines and wastes HTML bytes"
                });
        }
    }

    private string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        }
        catch
        {
            return url.TrimEnd('/').ToLowerInvariant();
        }
    }

    private double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        // Simple word-based similarity
        var words1 = Regex.Split(text1.ToLowerInvariant(), @"\W+").Where(w => w.Length > 2).ToHashSet();
        var words2 = Regex.Split(text2.ToLowerInvariant(), @"\W+").Where(w => w.Length > 2).ToHashSet();

        if (words1.Count == 0 || words2.Count == 0)
            return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return (double)intersection / union;
    }
}

