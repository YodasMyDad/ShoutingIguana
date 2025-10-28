using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.Plugins.Shared;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.TitlesMeta;

/// <summary>
/// Title, meta, Open Graph, Twitter Cards, and heading structure validation.
/// </summary>
public class TitlesMetaTask(ILogger logger, IServiceProvider serviceProvider) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
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
            await AnalyzeViewportAsync(ctx, viewport, doc);
            
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("Page URL: " + ctx.Url)
                .BeginNested("💡 Recommendations")
                    .AddItem("Every page must have a unique, descriptive title tag")
                    .AddItem("Add a <title> tag in the <head> section")
                    .AddItem("Title should describe the page content and include relevant keywords")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MISSING_TITLE",
                "Page is missing a title tag",
                details);
            return;
        }

        if (title.Length < MIN_TITLE_LENGTH)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Title: \"{title}\"")
                .AddItem($"Length: {title.Length} characters")
                .AddItem($"Recommended: At least {MIN_TITLE_LENGTH} characters")
                .BeginNested("💡 Recommendations")
                    .AddItem("Expand the title to be more descriptive")
                    .AddItem("Include relevant keywords that describe the page content")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("title", title)
                .WithTechnicalMetadata("length", title.Length)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TITLE_TOO_SHORT",
                $"Title is too short ({title.Length} chars, recommended: {MIN_TITLE_LENGTH}+)",
                details);
        }
        else if (title.Length > MAX_TITLE_WARNING_LENGTH)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Title: \"{title}\"")
                .AddItem($"Length: {title.Length} characters")
                .AddItem($"Maximum: {MAX_TITLE_LENGTH} characters (recommended)")
                .AddItem($"⚠️ Will be truncated in search results after ~{MAX_TITLE_LENGTH} characters")
                .BeginNested("💡 Recommendations")
                    .AddItem($"Shorten the title to under {MAX_TITLE_LENGTH} characters")
                    .AddItem("Put the most important keywords at the beginning")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("title", title)
                .WithTechnicalMetadata("length", title.Length)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TITLE_TOO_LONG",
                $"Title is too long ({title.Length} chars, will be truncated in search results)",
                details);
        }
        else if (title.Length > MAX_TITLE_LENGTH)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Title: \"{title}\"")
                .AddItem($"Length: {title.Length} characters")
                .AddItem($"Recommended: Under {MAX_TITLE_LENGTH} characters")
                .AddItem("ℹ️ May be truncated in some search results")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("title", title)
                .WithTechnicalMetadata("length", title.Length)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TITLE_LONG",
                $"Title is long ({title.Length} chars, may be truncated: recommended: <{MAX_TITLE_LENGTH})",
                details);
        }

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
                            var builder = FindingDetailsBuilder.Create()
                                .AddItem($"Title: \"{title}\"")
                                .AddItem($"Found on pages with temporary redirect relationships");
                            
                            builder.BeginNested("📄 Pages with temporary redirects");
                            foreach (var url in redirectInfo.TemporaryRedirects.Take(5))
                            {
                                builder.AddItem(url);
                            }
                            builder.EndNested();
                            
                            builder.BeginNested("⚠️ Issue")
                                .AddItem("Temporary redirects (302/307) don't consolidate duplicate titles")
                                .AddItem("Search engines may index both pages")
                            .EndNested();
                            
                            builder.BeginNested("💡 Recommendations")
                                .AddItem("Change to 301 (Permanent) redirects to properly consolidate pages")
                                .AddItem("Or make titles unique if both pages should be indexed")
                            .EndNested();
                            
                            builder.WithTechnicalMetadata("url", currentUrl)
                                .WithTechnicalMetadata("title", title)
                                .WithTechnicalMetadata("temporaryRedirects", redirectInfo.TemporaryRedirects.ToArray());
                            
                            await ctx.Findings.ReportAsync(
                                Key,
                                Severity.Warning,
                                "DUPLICATE_TITLE_TEMPORARY_REDIRECT",
                                $"Title \"{title}\" appears on pages with temporary redirect relationship",
                                builder.Build());
                        }
                        
                        // Only report true duplicates (no redirect relationship)
                        if (nonRedirectDuplicates.Length > 0)
                        {
                            var builder = FindingDetailsBuilder.Create()
                                .AddItem($"Title: \"{title}\"")
                                .AddItem($"Found on {nonRedirectDuplicates.Length + 1} different pages");
                            
                            builder.BeginNested("📄 Other pages with same title");
                            foreach (var url in nonRedirectDuplicates.Take(5))
                            {
                                builder.AddItem(url);
                            }
                            if (nonRedirectDuplicates.Length > 5)
                            {
                                builder.AddItem($"... and {nonRedirectDuplicates.Length - 5} more");
                            }
                            builder.EndNested();
                            
                            builder.BeginNested("💡 Recommendations")
                                .AddItem("Make each title unique to help search engines and users distinguish pages")
                                .AddItem("Include page-specific keywords in each title")
                            .EndNested();
                            
                            builder.WithTechnicalMetadata("url", currentUrl)
                                .WithTechnicalMetadata("title", title)
                                .WithTechnicalMetadata("duplicateCount", nonRedirectDuplicates.Length)
                                .WithTechnicalMetadata("otherUrls", nonRedirectDuplicates);
                            
                            await ctx.Findings.ReportAsync(
                                Key,
                                Severity.Error,
                                "DUPLICATE_TITLE",
                                $"Title \"{title}\" is used on multiple pages",
                                builder.Build());
                        }
                    }
                    else
                    {
                        // No redirects found, report as regular duplicate
                        var builder = FindingDetailsBuilder.Create()
                            .AddItem($"Title: \"{title}\"")
                            .AddItem($"Found on {duplicateCount} different pages");
                        
                        builder.BeginNested("📄 Other pages with same title");
                        foreach (var url in otherUrls.Take(5))
                        {
                            builder.AddItem(url);
                        }
                        if (otherUrls.Count > 5)
                        {
                            builder.AddItem($"... and {otherUrls.Count - 5} more");
                        }
                        builder.EndNested();
                        
                        builder.BeginNested("💡 Recommendations")
                            .AddItem("Make each title unique to help search engines and users distinguish pages")
                            .AddItem("Include page-specific keywords in each title")
                        .EndNested();
                        
                        builder.WithTechnicalMetadata("url", currentUrl)
                            .WithTechnicalMetadata("title", title)
                            .WithTechnicalMetadata("duplicateCount", duplicateCount)
                            .WithTechnicalMetadata("otherUrls", otherUrls.ToArray());
                        
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Error,
                            "DUPLICATE_TITLE",
                            $"Title \"{title}\" is used on multiple pages",
                            builder.Build());
                    }
                }
            }
        }
    }

    private async Task AnalyzeDescriptionAsync(UrlContext ctx, string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Page URL: " + ctx.Url)
                .BeginNested("💡 Recommendations")
                    .AddItem("Meta descriptions improve click-through rates in search results")
                    .AddItem("Add a <meta name=\"description\" content=\"...\"> tag")
                    .AddItem("Write unique, compelling descriptions for each page")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_DESCRIPTION",
                "Page is missing a meta description",
                details);
            return;
        }

        if (description.Length < MIN_DESCRIPTION_LENGTH)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Description: \"{description}\"")
                .AddItem($"Length: {description.Length} characters")
                .AddItem($"Recommended: At least {MIN_DESCRIPTION_LENGTH} characters")
                .BeginNested("💡 Recommendations")
                    .AddItem("Expand the description to be more informative")
                    .AddItem("Include key benefits or features of the page")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("description", description)
                .WithTechnicalMetadata("length", description.Length)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "DESCRIPTION_TOO_SHORT",
                $"Meta description is too short ({description.Length} chars, recommended: {MIN_DESCRIPTION_LENGTH}+)",
                details);
        }
        else if (description.Length > MAX_DESCRIPTION_WARNING_LENGTH)
        {
            var preview = description.Length > 100 ? description.Substring(0, 100) + "..." : description;
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Description: \"{preview}\"")
                .AddItem($"Length: {description.Length} characters")
                .AddItem($"Maximum: {MAX_DESCRIPTION_LENGTH} characters (recommended)")
                .AddItem("⚠️ Will be truncated in search results")
                .BeginNested("💡 Recommendations")
                    .AddItem($"Shorten to under {MAX_DESCRIPTION_LENGTH} characters")
                    .AddItem("Front-load the most important information")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("description", description)
                .WithTechnicalMetadata("length", description.Length)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "DESCRIPTION_TOO_LONG",
                $"Meta description is too long ({description.Length} chars, will be truncated)",
                details);
        }
        else if (description.Length > MAX_DESCRIPTION_LENGTH)
        {
            var preview = description.Length > 100 ? description.Substring(0, 100) + "..." : description;
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> {
                    ["url"] = ctx.Url.ToString(),
                    ["description"] = description,
                    ["length"] = description.Length
                },
                $"Description: \"{preview}\"",
                $"Length: {description.Length} characters",
                $"Recommended: Under {MAX_DESCRIPTION_LENGTH} characters");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "DESCRIPTION_LONG",
                $"Meta description is long ({description.Length} chars, may be truncated: recommended: <{MAX_DESCRIPTION_LENGTH})",
                details);
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
                    var builder = FindingDetailsBuilder.Create()
                        .AddItem($"Description: \"{preview}\"")
                        .AddItem($"Found on {duplicateCount} pages");
                    
                    builder.BeginNested("📄 Other pages");
                    foreach (var url in otherUrls)
                    {
                        builder.AddItem(url);
                    }
                    builder.EndNested();
                    
                    builder.BeginNested("💡 Recommendations")
                        .AddItem("Make each meta description unique")
                        .AddItem("Tailor descriptions to each page's specific content")
                    .EndNested();
                    
                    builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("description", description)
                        .WithTechnicalMetadata("duplicateCount", duplicateCount)
                        .WithTechnicalMetadata("otherUrls", otherUrls);
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "DUPLICATE_DESCRIPTION",
                        $"Meta description is used on multiple pages",
                        builder.Build());
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("HTML canonical: " + (ctx.Metadata.CanonicalHtml ?? "none"))
                .AddItem("HTTP canonical: " + (ctx.Metadata.CanonicalHttp ?? "none"))
                .BeginNested("💡 Recommendations")
                    .AddItem("Remove duplicate canonical tags - only one should be present")
                    .AddItem("Multiple canonicals confuse search engines")
                .EndNested()
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
        
        // Report cross-domain canonical
        if (ctx.Metadata.HasCrossDomainCanonical)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Canonical URL: {canonical}")
                .AddItem("🌐 Points to different domain")
                .BeginNested("ℹ️ Note")
                    .AddItem("Cross-domain canonicals are valid for syndicated content")
                    .AddItem("Use carefully - this gives ranking credit to another domain")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("canonical", canonical)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "CROSS_DOMAIN_CANONICAL",
                $"Page canonical points to different domain: {canonical}",
                details);
        }
        
        if (!string.IsNullOrEmpty(canonical))
        {
            // Check if canonical points to a different URL (same domain)
            var normalizedCurrent = NormalizeUrl(ctx.Url.ToString());
            var normalizedCanonical = NormalizeUrl(canonical);

            if (normalizedCurrent != normalizedCanonical && !ctx.Metadata.HasCrossDomainCanonical)
            {
                var details = FindingDetailsBuilder.Create()
                    .AddItem($"Current URL: {ctx.Url}")
                    .AddItem($"Canonical URL: {canonical}")
                    .BeginNested("ℹ️ Impact")
                        .AddItem("This page will not be indexed by search engines")
                        .AddItem("The canonical URL will be indexed instead")
                    .EndNested()
                    .WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("canonical", canonical)
                    .Build();
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "CANONICAL_TO_OTHER_PAGE",
                    $"Page canonical points to different URL: {canonical}",
                    details);
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
            
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Source: {source}")
                .AddItem("⚠️ This page will not appear in search results")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .WithTechnicalMetadata("hasConflict", ctx.Metadata.HasRobotsConflict)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "NOINDEX_DETECTED",
                $"Page has noindex directive (will not be indexed by search engines) - Source: {source}",
                details);
        }

        if (ctx.Metadata.RobotsNofollow == true)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Page has nofollow directive")
                .AddItem("🔗 Links on this page will not pass link equity")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .WithTechnicalMetadata("hasConflict", ctx.Metadata.HasRobotsConflict)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NOFOLLOW_DETECTED",
                "Page has nofollow directive (links will not pass equity)",
                details);
        }
        
        // Report robots conflicts if detected
        if (ctx.Metadata.HasRobotsConflict)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Conflicting directives detected:")
                .AddItem("  • Meta robots tag")
                .AddItem("  • X-Robots-Tag HTTP header")
                .AddItem("ℹ️ Most restrictive directive takes precedence")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("xRobotsTag", ctx.Metadata.XRobotsTag)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "ROBOTS_CONFLICT",
                "Meta robots and X-Robots-Tag header have conflicting directives",
                details);
        }
    }

    private async Task AnalyzeViewportAsync(UrlContext ctx, string viewport, HtmlDocument doc)
    {
        // Check for multiple viewport declarations
        var viewportNodes = doc.DocumentNode.SelectNodes("//meta[@name='viewport']");
        var viewportCount = viewportNodes?.Count ?? 0;

        if (viewportCount > 1)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Found {viewportCount} viewport declarations")
                .BeginNested("💡 Recommendations")
                    .AddItem("Only one viewport meta tag should be present")
                    .AddItem("Remove duplicate viewport tags")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("count", viewportCount)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MULTIPLE_VIEWPORT_DECLARATIONS",
                $"Multiple viewport declarations found ({viewportCount})",
                details);
        }
        
        if (string.IsNullOrEmpty(viewport))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("📱 No viewport meta tag found")
                .AddItem("This may cause mobile display issues")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                    .AddItem("Place it in the <head> section")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_VIEWPORT",
                "No viewport meta tag (mobile compatibility issue)",
                details);
        }
    }

    private async Task AnalyzeCharsetAsync(UrlContext ctx, HtmlDocument doc, string charset)
    {
        if (string.IsNullOrEmpty(charset))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("No charset declaration found")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add <meta charset=\"utf-8\"> as first element in <head>")
                    .AddItem("This ensures proper character encoding")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_CHARSET",
                "No charset declaration found",
                details);
            return;
        }

        // Check if charset is UTF-8 (recommended)
        if (!charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> {
                    ["url"] = ctx.Url.ToString(),
                    ["charset"] = charset
                },
                $"Charset: {charset}",
                "ℹ️ UTF-8 is recommended for universal character support");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "NON_UTF8_CHARSET",
                $"Charset is '{charset}' (UTF-8 is recommended)",
                details);
        }

        // Check for multiple charset declarations
        var charsetNodes = doc.DocumentNode.SelectNodes("//meta[@charset]");
        var httpEquivCharsetNodes = doc.DocumentNode.SelectNodes("//meta[@http-equiv='Content-Type']");
        var totalCharsetDeclarations = (charsetNodes?.Count ?? 0) + (httpEquivCharsetNodes?.Count ?? 0);

        if (totalCharsetDeclarations > 1)
        {
            var details = FindingDetailsBuilder.WithMetadata(
                new Dictionary<string, object?> {
                    ["url"] = ctx.Url.ToString(),
                    ["count"] = totalCharsetDeclarations
                },
                $"Found {totalCharsetDeclarations} charset declarations",
                "⚠️ Only one charset declaration should be present");
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MULTIPLE_CHARSET_DECLARATIONS",
                $"Multiple charset declarations found ({totalCharsetDeclarations})",
                details);
        }
    }

    private async Task AnalyzeLanguageAsync(UrlContext ctx, string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("No lang attribute on <html> element")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add lang attribute for accessibility and SEO")
                    .AddItem("Example: <html lang=\"en\">")
                    .AddItem("Helps screen readers and search engines")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_LANGUAGE",
                "No lang attribute on <html> element",
                details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("No Open Graph tags found")
                .AddItem("📱 Important for social media sharing")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add og:title, og:description, og:image, og:url tags")
                    .AddItem("This controls how your page appears when shared on social media")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_OPEN_GRAPH",
                "No Open Graph tags found (important for social sharing)",
                details);
        }
        else if (ogTagCount < 4)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(ogTitle)) missing.Add("og:title");
            if (string.IsNullOrEmpty(ogDescription)) missing.Add("og:description");
            if (string.IsNullOrEmpty(ogImage)) missing.Add("og:image");
            if (string.IsNullOrEmpty(ogUrl)) missing.Add("og:url");

            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Present: {ogTagCount} of 4 recommended tags");
            
            builder.BeginNested("❌ Missing tags");
            foreach (var tag in missing)
            {
                builder.AddItem(tag);
            }
            builder.EndNested();
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("missing", missing.ToArray())
                .WithTechnicalMetadata("present", ogTagCount);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "INCOMPLETE_OPEN_GRAPH",
                $"Open Graph tags incomplete (missing: {string.Join(", ", missing)})",
                builder.Build());
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("No Twitter Card tags found")
                .AddItem("ℹ️ Twitter will fall back to Open Graph tags")
                .BeginNested("💡 Recommendations")
                    .AddItem("Add twitter:card, twitter:title, twitter:description tags")
                    .AddItem("Or rely on Open Graph tags (which Twitter also uses)")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "MISSING_TWITTER_CARDS",
                "No Twitter Card tags found",
                details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("No H1 tag found")
                .BeginNested("💡 Recommendations")
                    .AddItem("Every page should have exactly one H1 tag")
                    .AddItem("The H1 should describe the main topic of the page")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MISSING_H1",
                "Page has no H1 tag",
                details);
        }
        else if (h1Count > 1)
        {
            var h1Texts = h1Nodes!.Take(5).Select(n => n.InnerText?.Trim()).ToArray();
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Found {h1Count} H1 tags")
                .AddItem("Recommended: Exactly 1 H1 per page");
            
            builder.BeginNested("📝 H1 texts found");
            foreach (var text in h1Texts)
            {
                builder.AddItem($"\"{text}\"");
            }
            if (h1Count > 5)
            {
                builder.AddItem($"... and {h1Count - 5} more");
            }
            builder.EndNested();
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("h1Count", h1Count)
                .WithTechnicalMetadata("h1Texts", h1Texts);
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MULTIPLE_H1",
                $"Page has {h1Count} H1 tags (should have exactly 1)",
                builder.Build());
        }
        else
        {
            // Analyze H1 content
            var h1Text = h1Nodes![0].InnerText?.Trim() ?? "";

            if (string.IsNullOrEmpty(h1Text))
            {
                var details = FindingDetailsBuilder.WithMetadata(
                    new Dictionary<string, object?> { ["url"] = ctx.Url.ToString() },
                    "H1 tag exists but is empty",
                    "⚠️ H1 should contain descriptive text");
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "EMPTY_H1",
                    "H1 tag is empty",
                    details);
            }
            else if (!string.IsNullOrEmpty(title))
            {
                // Check H1/Title alignment (should be similar for SEO)
                var similarity = CalculateSimilarity(h1Text, title);
                if (similarity < 0.3) // Less than 30% similar
                {
                    var details = FindingDetailsBuilder.Create()
                        .AddItem($"H1: \"{h1Text}\"")
                        .AddItem($"Title: \"{title}\"")
                        .AddItem($"Similarity: {similarity:P0}")
                        .BeginNested("ℹ️ Note")
                            .AddItem("H1 and title should be similar for better SEO")
                            .AddItem("They work together to signal page topic to search engines")
                        .EndNested()
                        .WithTechnicalMetadata("url", ctx.Url.ToString())
                        .WithTechnicalMetadata("h1", h1Text)
                        .WithTechnicalMetadata("title", title)
                        .Build();
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "H1_TITLE_MISMATCH",
                        "H1 and title are very different",
                        details);
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
            var details = FindingDetailsBuilder.Create()
                .AddItem("Heading hierarchy skips H2")
                .AddItem("Found: H3+ headings without H2")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use headings in order: H1 → H2 → H3, etc.")
                    .AddItem("Proper hierarchy helps accessibility and SEO")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SKIPPED_HEADING_LEVEL",
                "Heading hierarchy skips H2 (has H3+ but no H2)",
                details);
        }
        else if (!hasH3 && (hasH4 || hasH5 || hasH6))
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Heading hierarchy skips H3")
                .AddItem("Found: H4+ headings without H3")
                .BeginNested("💡 Recommendations")
                    .AddItem("Use headings in order: H1 → H2 → H3, etc.")
                    .AddItem("Proper hierarchy helps accessibility and SEO")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "SKIPPED_HEADING_LEVEL",
                "Heading hierarchy skips H3 (has H4+ but no H3)",
                details);
        }
    }

    private async Task CheckMetaKeywordsAsync(UrlContext ctx, HtmlDocument doc)
    {
        var keywordsNode = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
        if (keywordsNode != null)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem("Meta keywords tag detected")
                .AddItem("⚠️ This tag is outdated and ignored by modern search engines")
                .BeginNested("💡 Recommendations")
                    .AddItem("Remove the meta keywords tag")
                    .AddItem("It wastes HTML bytes and provides no SEO value")
                .EndNested()
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Info,
                "META_KEYWORDS_FOUND",
                "Meta keywords tag found (outdated, not used by search engines)",
                details);
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
    
    /// <summary>
    /// Check if URLs are in redirect relationships and categorize by redirect type.
    /// </summary>
    private async Task<RedirectRelationshipInfo> CheckRedirectRelationshipsAsync(int projectId, string currentUrl, List<string> otherUrls)
    {
        var info = new RedirectRelationshipInfo();
        
        try
        {
            // Get repository types through reflection to avoid direct Core dependency
            var urlRepoType = Type.GetType("ShoutingIguana.Core.Repositories.IUrlRepository, ShoutingIguana.Core");
            var redirectRepoType = Type.GetType("ShoutingIguana.Core.Repositories.IRedirectRepository, ShoutingIguana.Core");
            var urlModelType = Type.GetType("ShoutingIguana.Core.Models.Url, ShoutingIguana.Core");
            var redirectModelType = Type.GetType("ShoutingIguana.Core.Models.Redirect, ShoutingIguana.Core");
            
            if (urlRepoType == null || redirectRepoType == null || urlModelType == null || redirectModelType == null)
            {
                _logger.LogDebug("Unable to load repository types for redirect relationship checking");
                return info;
            }
            
            using var scope = _serviceProvider.CreateScope();
            var urlRepo = scope.ServiceProvider.GetService(urlRepoType);
            var redirectRepo = scope.ServiceProvider.GetService(redirectRepoType);
            
            if (urlRepo == null || redirectRepo == null)
            {
                _logger.LogDebug("Unable to resolve repositories from service provider");
                return info;
            }
            
            // Get all URLs via reflection
            var getUrlsMethod = urlRepoType.GetMethod("GetByProjectIdAsync");
            if (getUrlsMethod == null) return info;
            
            var urlsTask = getUrlsMethod.Invoke(urlRepo, new object[] { projectId }) as Task;
            if (urlsTask == null) return info;
            
            await urlsTask.ConfigureAwait(false);
            var urlsResult = urlsTask.GetType().GetProperty("Result")?.GetValue(urlsTask);
            if (urlsResult == null) return info;
            
            // Build dictionary via reflection (Address property)
            var urlDict = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            var addressProp = urlModelType.GetProperty("Address");
            
            if (urlsResult is System.Collections.IEnumerable urlsEnumerable)
            {
                foreach (var url in urlsEnumerable)
                {
                    var address = addressProp?.GetValue(url) as string;
                    if (address != null)
                    {
                        urlDict[address] = url;
                    }
                }
            }
            
            // Get all redirects via reflection
            var getRedirectsMethod = redirectRepoType.GetMethod("GetByProjectIdAsync");
            if (getRedirectsMethod == null) return info;
            
            var redirectsTask = getRedirectsMethod.Invoke(redirectRepo, new object[] { projectId }) as Task;
            if (redirectsTask == null) return info;
            
            await redirectsTask.ConfigureAwait(false);
            var redirectsResult = redirectsTask.GetType().GetProperty("Result")?.GetValue(redirectsTask);
            if (redirectsResult == null) return info;
            
            var allRedirects = new List<dynamic>();
            if (redirectsResult is System.Collections.IEnumerable redirectsEnumerable)
            {
                foreach (var redirect in redirectsEnumerable)
                {
                    allRedirects.Add(redirect);
                }
            }
            
            // Get property accessors via reflection
            var urlIdProp = urlModelType.GetProperty("Id");
            var redirectUrlIdProp = redirectModelType.GetProperty("UrlId");
            var redirectToUrlProp = redirectModelType.GetProperty("ToUrl");
            var redirectStatusCodeProp = redirectModelType.GetProperty("StatusCode");
            var redirectPositionProp = redirectModelType.GetProperty("Position");
            
            // For each other URL, check if there's a redirect relationship
            foreach (var otherUrl in otherUrls)
            {
                // Check if currentUrl redirects to otherUrl
                if (urlDict.TryGetValue(currentUrl, out var currentUrlEntity))
                {
                    var currentUrlId = (int?)urlIdProp?.GetValue(currentUrlEntity);
                    if (currentUrlId.HasValue)
                    {
                        var redirectsFromCurrent = allRedirects
                            .Where(r => {
                                var rUrlId = (int?)redirectUrlIdProp?.GetValue(r);
                                return rUrlId == currentUrlId.Value;
                            })
                            .OrderBy(r => (int?)redirectPositionProp?.GetValue(r) ?? 0)
                            .ToList();
                        
                        if (redirectsFromCurrent.Any())
                        {
                            var finalRedirect = redirectsFromCurrent.Last();
                            var toUrl = redirectToUrlProp?.GetValue(finalRedirect) as string;
                            var statusCode = (int?)redirectStatusCodeProp?.GetValue(finalRedirect);
                            
                            if (toUrl != null && statusCode.HasValue)
                            {
                                var normalizedFinal = NormalizeUrl(toUrl);
                                var normalizedOther = NormalizeUrl(otherUrl);
                                
                                if (normalizedFinal == normalizedOther)
                                {
                                    // currentUrl redirects to otherUrl
                                    if (statusCode == 301 || statusCode == 308)
                                    {
                                        info.PermanentRedirects.Add(otherUrl);
                                    }
                                    else if (statusCode == 302 || statusCode == 307)
                                    {
                                        info.TemporaryRedirects.Add(otherUrl);
                                    }
                                    continue;
                                }
                            }
                        }
                    }
                }
                
                // Check if otherUrl redirects to currentUrl
                if (urlDict.TryGetValue(otherUrl, out var otherUrlEntity))
                {
                    var otherUrlId = (int?)urlIdProp?.GetValue(otherUrlEntity);
                    if (otherUrlId.HasValue)
                    {
                        var redirectsFromOther = allRedirects
                            .Where(r => {
                                var rUrlId = (int?)redirectUrlIdProp?.GetValue(r);
                                return rUrlId == otherUrlId.Value;
                            })
                            .OrderBy(r => (int?)redirectPositionProp?.GetValue(r) ?? 0)
                            .ToList();
                        
                        if (redirectsFromOther.Any())
                        {
                            var finalRedirect = redirectsFromOther.Last();
                            var toUrl = redirectToUrlProp?.GetValue(finalRedirect) as string;
                            var statusCode = (int?)redirectStatusCodeProp?.GetValue(finalRedirect);
                            
                            if (toUrl != null && statusCode.HasValue)
                            {
                                var normalizedFinal = NormalizeUrl(toUrl);
                                var normalizedCurrent = NormalizeUrl(currentUrl);
                                
                                if (normalizedFinal == normalizedCurrent)
                                {
                                    // otherUrl redirects to currentUrl
                                    if (statusCode == 301 || statusCode == 308)
                                    {
                                        info.PermanentRedirects.Add(otherUrl);
                                    }
                                    else if (statusCode == 302 || statusCode == 307)
                                    {
                                        info.TemporaryRedirects.Add(otherUrl);
                                    }
                                }
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
        _logger.LogDebug("Cleaned up titles/meta data for project {ProjectId}", projectId);
    }
}

