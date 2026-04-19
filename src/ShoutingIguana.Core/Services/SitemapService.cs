using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

public class SitemapService(
    ILogger<SitemapService> logger,
    IHttpClientFactory httpClientFactory,
    IRobotsService robotsService) : ISitemapService
{
    private const string HttpClientName = "sitemap";
    private const int MaxSitemapUrls = 50000;
    private const int MaxSitemapsPerRobotsListing = 50;
    private const int MaxSitemapBytes = 50 * 1024 * 1024;
    private const int MaxConcurrentSitemapFetches = 5;
    private static readonly TimeSpan PerFetchTimeout = TimeSpan.FromSeconds(30);

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

    public async Task<List<string>> DiscoverSitemapUrlsAsync(string baseUrl, HttpClient? httpClient = null)
    {
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                logger.LogWarning("Invalid base URL for sitemap discovery: {BaseUrl}", baseUrl);
                return [];
            }

            var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sitemapLocations = new List<string>();
            var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var fetchGate = new SemaphoreSlim(MaxConcurrentSitemapFetches, MaxConcurrentSitemapFetches);

            logger.LogInformation("Checking robots.txt for sitemap URLs at {BaseUrl}", baseUrl);
            var robotsSitemaps = await robotsService.GetSitemapUrlsFromRobotsTxtAsync(baseUrl, httpClient).ConfigureAwait(false);
            if (robotsSitemaps.Count > MaxSitemapsPerRobotsListing)
            {
                logger.LogWarning("robots.txt for {BaseUrl} lists {Count} sitemaps which exceeds the safety cap of {Cap}; truncating", baseUrl, robotsSitemaps.Count, MaxSitemapsPerRobotsListing);
                robotsSitemaps = robotsSitemaps.Take(MaxSitemapsPerRobotsListing).ToList();
            }
            if (robotsSitemaps.Count > 0)
            {
                logger.LogInformation("Found {Count} sitemap(s) in robots.txt", robotsSitemaps.Count);
                sitemapLocations.AddRange(robotsSitemaps);
            }

            logger.LogDebug("Checking common sitemap locations");
            foreach (var location in CommonSitemapLocations)
            {
                var sitemapUrl = $"{baseUri.Scheme}://{baseUri.Host}{location}";
                if (!sitemapLocations.Contains(sitemapUrl, StringComparer.OrdinalIgnoreCase))
                {
                    sitemapLocations.Add(sitemapUrl);
                }
            }

            foreach (var sitemapUrl in sitemapLocations)
            {
                if (discoveredUrls.Count >= MaxSitemapUrls)
                {
                    logger.LogWarning("Reached maximum sitemap URL limit of {Max}, stopping discovery", MaxSitemapUrls);
                    break;
                }

                await ProcessSitemapAsync(sitemapUrl, discoveredUrls, visitedSitemaps, fetchGate, httpClient).ConfigureAwait(false);
            }

            logger.LogInformation("Sitemap discovery completed. Found {Count} URLs", discoveredUrls.Count);
            return discoveredUrls.ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sitemap discovery for {BaseUrl}", baseUrl);
            return [];
        }
    }

    private async Task ProcessSitemapAsync(string sitemapUrl, HashSet<string> discoveredUrls, HashSet<string> visitedSitemaps, SemaphoreSlim fetchGate, HttpClient? httpClient)
    {
        if (!visitedSitemaps.Add(sitemapUrl))
        {
            logger.LogDebug("Skipping already visited sitemap: {Url}", sitemapUrl);
            return;
        }

        byte[]? body;
        try
        {
            await fetchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                body = await FetchSitemapAsync(sitemapUrl, httpClient).ConfigureAwait(false);
            }
            finally
            {
                fetchGate.Release();
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("HTTP error fetching sitemap {Url}: {Message}", sitemapUrl, ex.Message);
            return;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Timeout fetching sitemap {Url}", sitemapUrl);
            return;
        }

        if (body is null || body.Length == 0)
        {
            logger.LogDebug("Empty sitemap content at {Url}", sitemapUrl);
            return;
        }

        try
        {
            using var ms = new MemoryStream(body);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersFromEntities = 1024,
                MaxCharactersInDocument = 10_000_000,
                XmlResolver = null,
                CloseInput = true,
            };
            using var reader = XmlReader.Create(ms, settings);
            var doc = XDocument.Load(reader);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var sitemapElements = doc.Descendants(ns + "sitemap").ToList();
            if (sitemapElements.Count > 0)
            {
                logger.LogDebug("Found sitemap index at {Url} with {Count} sitemaps", sitemapUrl, sitemapElements.Count);

                int processed = 0;
                foreach (var sitemapElement in sitemapElements)
                {
                    if (discoveredUrls.Count >= MaxSitemapUrls)
                        break;

                    if (processed >= MaxSitemapsPerRobotsListing)
                    {
                        logger.LogWarning("Sitemap index at {Url} lists more than {Cap} child sitemaps; truncating", sitemapUrl, MaxSitemapsPerRobotsListing);
                        break;
                    }

                    var locElement = sitemapElement.Element(ns + "loc");
                    if (locElement != null && !string.IsNullOrWhiteSpace(locElement.Value))
                    {
                        await ProcessSitemapAsync(locElement.Value, discoveredUrls, visitedSitemaps, fetchGate, httpClient).ConfigureAwait(false);
                    }
                    processed++;
                }
            }
            else
            {
                var urlElements = doc.Descendants(ns + "url").ToList();
                logger.LogInformation("Found sitemap at {Url} with {Count} URLs", sitemapUrl, urlElements.Count);

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
                            logger.LogDebug("Invalid URL in sitemap: {Url}", url);
                        }
                    }
                }
            }
        }
        catch (XmlException ex)
        {
            logger.LogWarning("Failed to parse sitemap XML at {Url}: {Message}", sitemapUrl, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error processing sitemap {Url}", sitemapUrl);
        }
    }

    private async Task<byte[]?> FetchSitemapAsync(string sitemapUrl, HttpClient? httpClient)
    {
        logger.LogDebug("Fetching sitemap: {Url}", sitemapUrl);
        var client = httpClient ?? httpClientFactory.CreateClient(HttpClientName);

        using var timeoutCts = new CancellationTokenSource(PerFetchTimeout);
        using var response = await client.GetAsync(sitemapUrl, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Sitemap not found at {Url}: {Status}", sitemapUrl, response.StatusCode);
            return null;
        }

        await using var netStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var pool = new byte[8192];
        long total = 0;
        bool truncated = false;

        while (true)
        {
            int read = await netStream.ReadAsync(pool.AsMemory(0, pool.Length), timeoutCts.Token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > MaxSitemapBytes)
            {
                var allowed = (int)(MaxSitemapBytes - (total - read));
                if (allowed > 0)
                {
                    buffer.Write(pool, 0, allowed);
                }
                truncated = true;
                break;
            }

            buffer.Write(pool, 0, read);
        }

        if (truncated)
        {
            logger.LogWarning("Sitemap {Url} exceeded {Max} bytes; truncated", sitemapUrl, MaxSitemapBytes);
        }

        return buffer.ToArray();
    }
}
