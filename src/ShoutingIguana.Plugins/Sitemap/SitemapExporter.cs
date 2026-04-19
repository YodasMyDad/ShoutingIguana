using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Sitemap;

/// <summary>
/// Exports sitemap.xml from crawled indexable URLs.
/// </summary>
public class SitemapExporter : IExportProvider
{
    private readonly ILogger _logger;
    private readonly IRepositoryAccessor _repositoryAccessor;

    public string Key => "SitemapXml";
    public string DisplayName => "Sitemap XML";
    public string FileExtension => ".xml";

    public SitemapExporter(ILogger logger, IRepositoryAccessor repositoryAccessor)
    {
        _logger = logger;
        _repositoryAccessor = repositoryAccessor;
    }

    public async Task<ExportResult> ExportAsync(ExportContext ctx, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Generating sitemap XML for project {ProjectId}", ctx.ProjectId);

            // Build redirect-source set so 3xx originators are excluded from the sitemap.
            var redirectSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var redirect in _repositoryAccessor.GetRedirectsAsync(ctx.ProjectId, ct))
            {
                if (!string.IsNullOrEmpty(redirect.SourceUrl))
                {
                    redirectSources.Add(redirect.SourceUrl);
                }
            }

            // Filter for sitemap-eligible URLs: 200 only, indexable (rolls up noindex + robots),
            // and not the source of a redirect. Cross-URL canonical filtering is not applied here
            // because UrlInfo does not expose canonical data without adding a new repository dependency.
            var indexableUrls = new List<SitemapUrl>();

            await foreach (var url in _repositoryAccessor.GetUrlsAsync(ctx.ProjectId, ct))
            {
                if (string.IsNullOrEmpty(url.Address))
                {
                    continue;
                }

                if (url.Status != 200)
                {
                    continue;
                }

                if (!url.IsIndexable)
                {
                    continue;
                }

                if (redirectSources.Contains(url.Address))
                {
                    continue;
                }

                indexableUrls.Add(new SitemapUrl
                {
                    Address = url.Address,
                    LastCrawled = null, // Not available in UrlInfo - can be added if needed
                    Depth = url.Depth
                });
            }

            indexableUrls = indexableUrls.OrderBy(u => u.Address).ToList();
            _logger.LogInformation("Found {Count} indexable URLs for sitemap", indexableUrls.Count);

            if (indexableUrls.Count == 0)
            {
                return new ExportResult(false, "No indexable URLs found. Run a crawl first.");
            }

            // Generate sitemap XML
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urlset = new XElement(ns + "urlset");

            foreach (var url in indexableUrls)
            {
                var urlElement = new XElement(ns + "url",
                    new XElement(ns + "loc", url.Address));

                // Add lastmod if available. Emit with numeric offset (zzz) so the format is an unambiguous
                // W3C Datetime; the prior literal "Z" format is not spec-compliant as a format specifier.
                if (url.LastCrawled.HasValue)
                {
                    var lastmod = url.LastCrawled.Value.Kind == DateTimeKind.Utc
                        ? url.LastCrawled.Value
                        : url.LastCrawled.Value.ToUniversalTime();
                    urlElement.Add(new XElement(ns + "lastmod",
                        lastmod.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)));
                }

                // Add priority based on depth (shallower = higher priority)
                var priority = CalculatePriority(url.Depth);
                urlElement.Add(new XElement(ns + "priority", priority.ToString("0.0")));

                // Add changefreq (simplified heuristic)
                var changefreq = DetermineChangeFrequency(url.Depth);
                urlElement.Add(new XElement(ns + "changefreq", changefreq));

                urlset.Add(urlElement);
            }

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                urlset);

            // Save to file
            doc.Save(ctx.FilePath);

            _logger.LogInformation("Sitemap XML exported successfully to {FilePath}", ctx.FilePath);
            return new ExportResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting sitemap XML");
            return new ExportResult(false, ex.Message);
        }
    }

    // Helper class to avoid direct Core dependency
    private class SitemapUrl
    {
        public string Address { get; set; } = string.Empty;
        public DateTime? LastCrawled { get; set; }
        public int Depth { get; set; }
    }

    private static double CalculatePriority(int depth)
    {
        // Depth 0 (homepage) = 1.0
        // Depth 1 = 0.8
        // Depth 2 = 0.6
        // Depth 3+ = 0.4
        return depth switch
        {
            0 => 1.0,
            1 => 0.8,
            2 => 0.6,
            _ => 0.4
        };
    }

    private static string DetermineChangeFrequency(int depth)
    {
        // Shallower pages typically change more frequently
        return depth switch
        {
            0 => "daily",    // Homepage
            1 => "weekly",   // Main sections
            2 => "monthly",  // Subsections
            _ => "yearly"    // Deep pages
        };
    }
}

