using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

public class SitemapService(
    ILogger<SitemapService> logger,
    IHttpClientFactory httpClientFactory,
    IRobotsService robotsService) : ISitemapService
{
    private readonly ILogger<SitemapService> _logger = logger;
    private readonly HttpClient _httpClient = CreateHttpClient(httpClientFactory);
    private readonly IRobotsService _robotsService = robotsService;

    private const int MaxSitemapUrls = 50000;

    private static readonly string[] CommonSitemapLocations =
    [
        "/sitemap.xml",
        "/sitemap_index.xml",
        "/wp-sitemap.xml",
        "/sitemap-index.xml",
        "/sitemap/sitemap.xml",
        "/sitemap1.xml",
        "/media/sitemap.xml",
        "/sitemap-posts.xml"
    ];

    private static HttpClient CreateHttpClient(IHttpClientFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    public async Task<List<string>> DiscoverSitemapUrlsAsync(string baseUrl)
    {
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                _logger.LogWarning("Invalid base URL for sitemap discovery: {BaseUrl}", baseUrl);
                return [];
            }

            var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sitemapLocations = new List<string>();
            var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First, check robots.txt for sitemap directives
            _logger.LogInformation("Checking robots.txt for sitemap URLs at {BaseUrl}", baseUrl);
            var robotsSitemaps = await _robotsService.GetSitemapUrlsFromRobotsTxtAsync(baseUrl).ConfigureAwait(false);
            if (robotsSitemaps.Any())
            {
                _logger.LogInformation("Found {Count} sitemap(s) in robots.txt", robotsSitemaps.Count);
                sitemapLocations.AddRange(robotsSitemaps);
            }

            // Then check common locations
            _logger.LogDebug("Checking common sitemap locations");
            foreach (var location in CommonSitemapLocations)
            {
                var sitemapUrl = $"{baseUri.Scheme}://{baseUri.Host}{location}";
                if (!sitemapLocations.Contains(sitemapUrl, StringComparer.OrdinalIgnoreCase))
                {
                    sitemapLocations.Add(sitemapUrl);
                }
            }

            // Process all sitemap locations
            foreach (var sitemapUrl in sitemapLocations)
            {
                if (discoveredUrls.Count >= MaxSitemapUrls)
                {
                    _logger.LogWarning("Reached maximum sitemap URL limit of {Max}, stopping discovery", MaxSitemapUrls);
                    break;
                }

                await ProcessSitemapAsync(sitemapUrl, discoveredUrls, visitedSitemaps).ConfigureAwait(false);
            }

            _logger.LogInformation("Sitemap discovery completed. Found {Count} URLs", discoveredUrls.Count);
            return discoveredUrls.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sitemap discovery for {BaseUrl}", baseUrl);
            return [];
        }
    }

    private async Task ProcessSitemapAsync(string sitemapUrl, HashSet<string> discoveredUrls, HashSet<string> visitedSitemaps)
    {
        // Prevent infinite recursion by tracking visited sitemaps
        if (!visitedSitemaps.Add(sitemapUrl))
        {
            _logger.LogDebug("Skipping already visited sitemap: {Url}", sitemapUrl);
            return;
        }

        try
        {
            _logger.LogDebug("Fetching sitemap: {Url}", sitemapUrl);
            var response = await _httpClient.GetAsync(sitemapUrl).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Sitemap not found at {Url}: {Status}", sitemapUrl, response.StatusCode);
                return;
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogDebug("Empty sitemap content at {Url}", sitemapUrl);
                return;
            }

            // Parse XML
            var doc = XDocument.Parse(content);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Check if this is a sitemap index
            var sitemapElements = doc.Descendants(ns + "sitemap").ToList();
            if (sitemapElements.Any())
            {
                _logger.LogDebug("Found sitemap index at {Url} with {Count} sitemaps", sitemapUrl, sitemapElements.Count);
                
                // Process each sitemap in the index
                foreach (var sitemapElement in sitemapElements)
                {
                    if (discoveredUrls.Count >= MaxSitemapUrls)
                        break;

                    var locElement = sitemapElement.Element(ns + "loc");
                    if (locElement != null && !string.IsNullOrWhiteSpace(locElement.Value))
                    {
                        await ProcessSitemapAsync(locElement.Value, discoveredUrls, visitedSitemaps).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // This is a regular sitemap with URLs
                var urlElements = doc.Descendants(ns + "url").ToList();
                _logger.LogInformation("Found sitemap at {Url} with {Count} URLs", sitemapUrl, urlElements.Count);

                foreach (var urlElement in urlElements)
                {
                    if (discoveredUrls.Count >= MaxSitemapUrls)
                        break;

                    var locElement = urlElement.Element(ns + "loc");
                    if (locElement != null && !string.IsNullOrWhiteSpace(locElement.Value))
                    {
                        var url = locElement.Value.Trim();
                        if (Uri.TryCreate(url, UriKind.Absolute, out _))
                        {
                            discoveredUrls.Add(url);
                        }
                        else
                        {
                            _logger.LogDebug("Invalid URL in sitemap: {Url}", url);
                        }
                    }
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("HTTP error fetching sitemap {Url}: {Message}", sitemapUrl, ex.Message);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning("Failed to parse sitemap XML at {Url}: {Message}", sitemapUrl, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error processing sitemap {Url}", sitemapUrl);
        }
    }
}

