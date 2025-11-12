using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.TitlesMeta;

/// <summary>
/// Title, meta, Open Graph, Twitter Cards, and heading structure validation.
/// </summary>
public class TitlesMetaTask(ILogger logger, IRepositoryAccessor repositoryAccessor) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IRepositoryAccessor _repositoryAccessor = repositoryAccessor;
    private const int MIN_TITLE_LENGTH = 30;
    private const int MAX_TITLE_LENGTH = 60;
    private const int MAX_TITLE_WARNING_LENGTH = 70;
    private const int MIN_DESCRIPTION_LENGTH = 50;
    private const int MAX_DESCRIPTION_LENGTH = 160;
    private const int MAX_DESCRIPTION_WARNING_LENGTH = 200;
    
    // For duplicate detection across pages
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> _titlesByProject = new();
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, List<string>>> _descriptionsByProject = new();
    
    // Cache redirects per project to avoid loading them repeatedly (critical for performance)
    private static readonly ConcurrentDictionary<int, Dictionary<string, List<RedirectInfo>>> RedirectCacheByProject = new();
    
    // Semaphore to ensure only one thread loads redirects per project (prevents race condition)
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> RedirectLoadingSemaphores = new();

    public override string Key => "TitlesMeta";
    public override string DisplayName => "Titles & Meta";
    public override string Description => "Validates title tags, meta descriptions, Open Graph, and heading structure";
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
            var doc = new HtmlDocument();
            doc.LoadHtml(ctx.RenderedHtml);

            // Extract basic meta elements (crawler already parsed canonical and robots)
            var title = ExtractTitle(doc);
            var description = ExtractMetaDescription(doc);
            var viewport = ExtractViewport(doc);
            var charset = ExtractCharset(doc);
            var language = ExtractLanguage(doc);

            // Track for duplicate detection before analysis so current page participates
            TrackTitleAndDescription(ctx.Project.ProjectId, title, description, ctx.Url.ToString());

            // Analyze title (runs after tracking so duplicate detection sees this page)
            await AnalyzeTitleAsync(ctx, title, description);
            
            // Analyze description
            await AnalyzeDescriptionAsync(ctx, description, title);
            
            // CTR optimization checks
            await CheckDescriptionCTRAsync(ctx, description, title);
            await CheckTitleCTRAsync(ctx, title, description);
            
            // Analyze canonical (using crawler-parsed data)
            await AnalyzeCanonicalAsync(ctx);
            
            // Analyze robots meta (using crawler-parsed data)
            await AnalyzeRobotsAsync(ctx);
            
            // Analyze viewport
            await AnalyzeViewportAsync(ctx, viewport, doc, title, description);
            
            // Analyze charset
            await AnalyzeCharsetAsync(ctx, doc, charset, title, description);
            
            // Analyze language
            await AnalyzeLanguageAsync(ctx, language, title, description);
            
            // Analyze Open Graph tags
            await AnalyzeOpenGraphAsync(ctx, doc, title, description);
            
            // Analyze Twitter Cards
            await AnalyzeTwitterCardsAsync(ctx, doc, title, description);
            
            // Analyze heading structure
            await AnalyzeHeadingStructureAsync(ctx, doc, title, description);
            
            // Check for outdated meta keywords
            await CheckMetaKeywordsAsync(ctx, doc, title, description);
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

    private async Task AnalyzeTitleAsync(UrlContext ctx, string title, string description)
    {
        if (string.IsNullOrEmpty(title))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Title")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
            return;
        }

        if (title.Length < MIN_TITLE_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Title Too Short")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        else if (title.Length > MAX_TITLE_WARNING_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Title Too Long (Will Truncate)")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        else if (title.Length > MAX_TITLE_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Title Long (May Truncate)")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        
        // Check pixel width (more accurate than character count for SERP appearance)
        await CheckTitlePixelWidthAsync(ctx, title, description);

        // Check for duplicate titles (will be reported later in batch)
        if (_titlesByProject.TryGetValue(ctx.Project.ProjectId, out var projectTitles))
        {
            if (projectTitles.TryGetValue(title, out var urls))
            {
                int duplicateCount;
                List<string> otherUrls;
                
                lock (urls)
                {
                    duplicateCount = urls.Count;
                    otherUrls = urls.Where(u => u != ctx.Url.ToString()).Take(5).ToList();
                }
                
                if (duplicateCount > 1)
                {
                    var currentUrl = ctx.Url.ToString();
                    
                    // Check if any of these duplicates are actually redirect relationships
                    var redirectInfo = await CheckRedirectRelationshipsAsync(ctx.Project.ProjectId, currentUrl, otherUrls);
                    
                    if (redirectInfo.HasRedirects)
                    {
                        // Filter out URLs that have proper permanent redirects
                        var nonRedirectDuplicates = otherUrls
                            .Where(url => !redirectInfo.PermanentRedirects.Contains(url))
                            .ToArray();
                        
                        // If we have temporary redirects, report them as warnings
                        if (redirectInfo.TemporaryRedirects.Any())
                        {
                            var row = ReportRow.Create()
                                .Set("Page", currentUrl)
                                .Set("Issue", "Duplicate Title (Temporary Redirects)")
                                .Set("Title", title)
                                .Set("MetaDescription", "")
                                .Set("Length", title.Length)
                                .Set("Severity", "Warning");
                            
                            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
                        }
                        
                        // Only report true duplicates (no redirect relationship)
                        if (nonRedirectDuplicates.Length > 0)
                        {
                            var row = ReportRow.Create()
                                .Set("Page", currentUrl)
                                .Set("Issue", $"Duplicate Title ({nonRedirectDuplicates.Length + 1} pages)")
                                .Set("Title", title)
                                .Set("MetaDescription", "")
                                .Set("Length", title.Length)
                                .Set("Severity", "Error");
                            
                            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
                        }
                    }
                    else
                    {
                        // No redirects found, report as regular duplicate
                        var row = ReportRow.Create()
                            .Set("Page", currentUrl)
                            .Set("Issue", $"Duplicate Title ({duplicateCount} pages)")
                            .Set("Title", title)
                            .Set("MetaDescription", "")
                            .Set("Length", title.Length)
                            .Set("Severity", "Error");
                        
                        await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check title pixel width for more accurate SERP appearance prediction.
    /// Google displays ~600px of title, not a fixed character count.
    /// </summary>
    private async Task CheckTitlePixelWidthAsync(UrlContext ctx, string title, string description)
    {
        // Calculate approximate pixel width
        // Different characters have different widths in Google's SERP display
        // Approximate widths: uppercase/wide chars ~12px, lowercase ~8px, narrow chars like 'i' ~4px
        double pixelWidth = 0;
        
        foreach (char c in title)
        {
            if (char.IsUpper(c) || c == 'W' || c == 'M')
            {
                pixelWidth += 12; // Wide characters
            }
            else if (c == 'i' || c == 'l' || c == 'I' || c == '!' || c == '|' || c == '.' || c == ',')
            {
                pixelWidth += 4; // Narrow characters
            }
            else if (char.IsWhiteSpace(c))
            {
                pixelWidth += 4; // Spaces
            }
            else
            {
                pixelWidth += 8; // Average characters
            }
        }
        
        // Google displays approximately 600px on desktop, 580px on mobile
        // Use 580px as the safe threshold
        const int SAFE_PIXEL_WIDTH = 580;
        const int MAX_PIXEL_WIDTH = 600;
        
        if (pixelWidth > MAX_PIXEL_WIDTH)
        {
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Title Pixel Width Exceeded (~{(int)pixelWidth}px)")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row1, title, description), ctx.Metadata.UrlId, default);
        }
        else if (pixelWidth > SAFE_PIXEL_WIDTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Title May Truncate on Mobile (~{(int)pixelWidth}px)")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeDescriptionAsync(UrlContext ctx, string description, string title)
    {
        if (string.IsNullOrEmpty(description))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Meta Description")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
            return;
        }

        if (description.Length < MIN_DESCRIPTION_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Meta Description Too Short")
                .Set("Title", "")
                .Set("MetaDescription", description)
                .Set("Length", description.Length)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        else if (description.Length > MAX_DESCRIPTION_WARNING_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Meta Description Too Long (Will Truncate)")
                .Set("Title", "")
                .Set("MetaDescription", description)
                .Set("Length", description.Length)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        else if (description.Length > MAX_DESCRIPTION_LENGTH)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Meta Description Long (May Truncate)")
                .Set("Title", "")
                .Set("MetaDescription", description)
                .Set("Length", description.Length)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
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
                    var preview = description.Length > 80 ? description.Substring(0, 80) + "..." : description;
                    var row = ReportRow.Create()
                        .Set("Page", ctx.Url.ToString())
                        .Set("Issue", $"Duplicate Meta Description ({duplicateCount} pages)")
                        .Set("Title", "")
                        .Set("MetaDescription", preview)
                        .Set("Length", description.Length)
                        .Set("Severity", "Warning");
                    
                    await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
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
            // NOTE: These canonical checks are duplicates of Canonical plugin
            // Skipping to avoid duplicate reporting - handled by Canonical plugin
        }
    }

    private async Task AnalyzeRobotsAsync(UrlContext ctx)
    {
        // NOTE: Robots checks are duplicates of Robots plugin
        // Skipping to avoid duplicate reporting - handled by Robots plugin
    }

    private async Task AnalyzeViewportAsync(UrlContext ctx, string viewport, HtmlDocument doc, string title, string description)
    {
        // Check for multiple viewport declarations
        var viewportNodes = doc.DocumentNode.SelectNodes("//meta[@name='viewport']");
        var viewportCount = viewportNodes?.Count ?? 0;

        if (viewportCount > 1)
        {
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Multiple Viewport Declarations ({viewportCount})")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row1, title, description), ctx.Metadata.UrlId, default);
        }
        
        if (string.IsNullOrEmpty(viewport))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Viewport Meta Tag")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeCharsetAsync(UrlContext ctx, HtmlDocument doc, string charset, string title, string description)
    {
        if (string.IsNullOrEmpty(charset))
        {
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Charset Declaration")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row1, title, description), ctx.Metadata.UrlId, default);
            return;
        }

        // Check if charset is UTF-8 (recommended)
        if (!charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            var row2 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Non-UTF8 Charset ({charset})")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row2, title, description), ctx.Metadata.UrlId, default);
        }

        // Check for multiple charset declarations
        var charsetNodes = doc.DocumentNode.SelectNodes("//meta[@charset]");
        var httpEquivCharsetNodes = doc.DocumentNode.SelectNodes("//meta[@http-equiv='Content-Type']");
        var totalCharsetDeclarations = (charsetNodes?.Count ?? 0) + (httpEquivCharsetNodes?.Count ?? 0);

        if (totalCharsetDeclarations > 1)
        {
            var row3 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Multiple Charset Declarations ({totalCharsetDeclarations})")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row3, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeLanguageAsync(UrlContext ctx, string language, string title, string description)
    {
        if (string.IsNullOrEmpty(language))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Lang Attribute")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeOpenGraphAsync(UrlContext ctx, HtmlDocument doc, string title, string description)
    {
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "");
        var ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", "");
        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
        var ogUrl = doc.DocumentNode.SelectSingleNode("//meta[@property='og:url']")?.GetAttributeValue("content", "");
        var ogType = doc.DocumentNode.SelectSingleNode("//meta[@property='og:type']")?.GetAttributeValue("content", "");

        var ogTagCount = new[] { ogTitle, ogDescription, ogImage, ogUrl, ogType }.Count(s => !string.IsNullOrEmpty(s));

        if (ogTagCount == 0)
        {
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Open Graph Tags")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row1, title, description), ctx.Metadata.UrlId, default);
        }
        else if (ogTagCount < 4)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(ogTitle)) missing.Add("og:title");
            if (string.IsNullOrEmpty(ogDescription)) missing.Add("og:description");
            if (string.IsNullOrEmpty(ogImage)) missing.Add("og:image");
            if (string.IsNullOrEmpty(ogUrl)) missing.Add("og:url");

            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Incomplete Open Graph ({ogTagCount}/4)")
                .Set("Title", "")
                .Set("MetaDescription", string.Join(", ", missing))
                .Set("Length", ogTagCount)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeTwitterCardsAsync(UrlContext ctx, HtmlDocument doc, string title, string description)
    {
        var twitterCard = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:card']")?.GetAttributeValue("content", "");
        var twitterTitle = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "");
        var twitterDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", "");
        var twitterImage = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", "");

        var twitterTagCount = new[] { twitterCard, twitterTitle, twitterDescription, twitterImage }.Count(s => !string.IsNullOrEmpty(s));

        if (twitterTagCount == 0)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing Twitter Card Tags")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task AnalyzeHeadingStructureAsync(UrlContext ctx, HtmlDocument doc, string title, string description)
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
            var row1 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing H1 Tag")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Error");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row1, title, description), ctx.Metadata.UrlId, default);
        }
        else if (h1Count > 1)
        {
            var h1Texts = h1Nodes!.Take(5).Select(n => n.InnerText?.Trim()).ToArray();
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Multiple H1 Tags ({h1Count})")
                .Set("Title", title)
                .Set("MetaDescription", string.Join(", ", h1Texts))
                .Set("Length", h1Count)
                .Set("Severity", "Warning");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
        else
        {
            // Analyze H1 content
            var h1Text = h1Nodes![0].InnerText?.Trim() ?? "";

            if (string.IsNullOrEmpty(h1Text))
            {
                var row2 = ReportRow.Create()
                    .Set("Page", ctx.Url.ToString())
                    .Set("Issue", "Empty H1 Tag")
                    .Set("Title", title)
                    .Set("MetaDescription", "")
                    .Set("Length", 0)
                    .Set("Severity", "Warning");
                
                await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row2, title, description), ctx.Metadata.UrlId, default);
            }
            else if (!string.IsNullOrEmpty(title))
            {
                // Check for exact H1/Title duplication (keyword variation opportunity)
                var normalizedH1 = h1Text.Trim().ToLowerInvariant();
                var normalizedTitle = title.Trim().ToLowerInvariant();
                
                if (normalizedH1 == normalizedTitle)
                {
                    var row3 = ReportRow.Create()
                        .Set("Page", ctx.Url.ToString())
                        .Set("Issue", "Identical H1 and Title")
                        .Set("Title", title)
                        .Set("MetaDescription", h1Text)
                        .Set("Length", title.Length)
                        .Set("Severity", "Info");
                    
                    await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row3, title, description), ctx.Metadata.UrlId, default);
                }
                
                // Check H1/Title alignment (should be complementary, not unrelated)
                var similarity = CalculateSimilarity(h1Text, title);
                if (similarity < 0.3) // Less than 30% similar
                {
                    var row4 = ReportRow.Create()
                        .Set("Page", ctx.Url.ToString())
                        .Set("Issue", "H1 and Title Unrelated")
                        .Set("Title", title)
                        .Set("MetaDescription", h1Text)
                        .Set("Length", (int)(similarity * 100))
                        .Set("Severity", "Info");
                    
                    await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row4, title, description), ctx.Metadata.UrlId, default);
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
            var row5 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Heading Hierarchy Skips H2")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row5, title, description), ctx.Metadata.UrlId, default);
        }
        else if (!hasH3 && (hasH4 || hasH5 || hasH6))
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Heading Hierarchy Skips H3")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private async Task CheckMetaKeywordsAsync(UrlContext ctx, HtmlDocument doc, string title, string description)
    {
        var keywordsNode = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
        if (keywordsNode != null)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Meta Keywords Tag Found (Outdated)")
                .Set("Title", "")
                .Set("MetaDescription", "")
                .Set("Length", 0)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
    }

    private string NormalizeUrl(string url)
    {
        return UrlHelper.Normalize(url);
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

    private static string NormalizeMetaValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static ReportRow WithNormalizedMetadata(ReportRow row, string title, string description)
    {
        return row
            .Set("Title", NormalizeMetaValue(title, "(missing title)"))
            .Set("MetaDescription", NormalizeMetaValue(description, "(missing meta description)"));
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
            _logger.LogWarning(ex, "Error checking redirect relationships for duplicate title detection");
        }
        
        return info;
    }
    
    /// <summary>
    /// Check meta description for CTR-driving action words
    /// </summary>
    private async Task CheckDescriptionCTRAsync(UrlContext ctx, string description, string title)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return; // Already reported as missing
        }
        
        // Action words that drive clicks
        var actionWords = new[]
        {
            "learn", "discover", "get", "find", "buy", "download", "try", "save", 
            "see", "compare", "check", "explore", "browse", "shop", "read", 
            "watch", "start", "join", "access", "unlock", "grab", "claim"
        };
        
        var descriptionLower = description.ToLowerInvariant();
        var hasActionWord = actionWords.Any(word => 
            Regex.IsMatch(descriptionLower, $@"\b{word}\b"));
        
        if (!hasActionWord)
        {
            var row6 = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Description Lacks Action Words")
                .Set("Title", "")
                .Set("MetaDescription", description)
                .Set("Length", description.Length)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row6, title, description), ctx.Metadata.UrlId, default);
        }
    }
    
    /// <summary>
    /// Check title for special characters that improve SERP visibility
    /// </summary>
    private async Task CheckTitleCTRAsync(UrlContext ctx, string title, string description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return; // Already reported as missing
        }
        
        // Special characters that help titles stand out in SERPs
        var specialChars = new[] { "|", "•", "→", "–", "—", "[", "]", "(", ")" };
        var hasSpecialChar = specialChars.Any(ch => title.Contains(ch));
        
        // Check for purely alphanumeric titles (no separators at all)
        var hasSeparators = title.Contains("|") || title.Contains("-") || 
                           title.Contains("•") || title.Contains("→") ||
                           title.Contains("–") || title.Contains("—") ||
                           title.Contains("[") || title.Contains("]") ||
                           title.Contains("(") || title.Contains(")") ||
                           title.Contains(":") || title.Contains("–");
        
        if (!hasSeparators)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Title Lacks Visual Separators")
                .Set("Title", title)
                .Set("MetaDescription", "")
                .Set("Length", title.Length)
                .Set("Severity", "Info");
            
            await ctx.Reports.ReportAsync(Key, WithNormalizedMetadata(row, title, description), ctx.Metadata.UrlId, default);
        }
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
        _titlesByProject.TryRemove(projectId, out _);
        _descriptionsByProject.TryRemove(projectId, out _);
        RedirectCacheByProject.TryRemove(projectId, out _);
        
        // Cleanup and dispose semaphore
        if (RedirectLoadingSemaphores.TryRemove(projectId, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        _logger.LogDebug("Cleaned up titles/meta data for project {ProjectId}", projectId);
    }
}


