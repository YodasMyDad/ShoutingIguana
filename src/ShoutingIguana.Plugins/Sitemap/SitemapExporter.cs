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

            // Filter for indexable URLs using repository accessor
            var indexableUrls = new List<SitemapUrl>();
            
            await foreach (var url in _repositoryAccessor.GetUrlsAsync(ctx.ProjectId, ct))
            {
                if (url.IsIndexable && url.Status == 200 && !string.IsNullOrEmpty(url.Address))
                {
                    indexableUrls.Add(new SitemapUrl
                    {
                        Address = url.Address,
                        LastCrawled = null, // Not available in UrlInfo - can be added if needed
                        Depth = url.Depth
                    });
                }
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

                // Add lastmod if available
                if (url.LastCrawled.HasValue)
                {
                    urlElement.Add(new XElement(ns + "lastmod", 
                        url.LastCrawled.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")));
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

