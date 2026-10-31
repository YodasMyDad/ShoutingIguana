using System.IO.Compression;
using System.Net.Http.Headers;
using System.Xml;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Sitemaps;

/// <summary>
/// Finds sitemaps (from robots.txt, from the options, or /sitemap.xml as a fallback), follows sitemap
/// indexes, and feeds every listed URL to the frontier while recording which sitemap listed it. URLs that
/// are never linked keep the "unlinked" depth, which is how orphan pages show up.
/// </summary>
internal sealed class SitemapDiscoverer(
    CrawlOptions options,
    HttpFetcher fetcher,
    HostGateRegistry gates,
    RobotsCache robots,
    CrawlWriter writer,
    OutLinkFactory links,
    ICrawlObserver observer,
    ILogger logger)
{
    private const int MAX_SITEMAPS = 1_000;
    private const int MAX_NESTING = 3;
    private const int MAX_REDIRECTS = 5;
    private const int BATCH_SIZE = 1_000;

    public async Task RunAsync(Uri startUri, CancellationToken cancellationToken)
    {
        var rules = await robots.GetAsync(startUri).WaitAsync(cancellationToken).ConfigureAwait(false);
        var candidates = rules.Sitemaps.Concat(options.ExtraSitemaps).ToList();

        // Nothing declares a sitemap, so try the conventional location. Analysis treats a failed probe as "no
        // sitemap" rather than as a broken one.
        var isProbe = candidates.Count == 0;
        if (isProbe)
        {
            candidates.Add(startUri.GetLeftPart(UriPartial.Authority) + "/sitemap.xml");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Url, int? ParentId, int Level)>();
        foreach (var candidate in candidates)
        {
            if (UrlNormalizer.TryNormalize(candidate, out var normalized) && seen.Add(normalized))
            {
                pending.Enqueue((normalized, null, 0));
            }
        }

        var sitemapCount = 0;
        var urlCount = 0;
        while (pending.Count > 0 && sitemapCount < MAX_SITEMAPS)
        {
            var (url, parentId, level) = pending.Dequeue();
            sitemapCount++;
            var sitemapId = await writer.RegisterSitemapAsync(url, parentId, cancellationToken).ConfigureAwait(false);
            var (children, listed) = await ReadSitemapAsync(new Uri(url), sitemapId, isProbe && level == 0, cancellationToken).ConfigureAwait(false);
            urlCount += listed;

            if (level >= MAX_NESTING)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (UrlNormalizer.TryNormalize(child, out var normalized) && seen.Add(normalized))
                {
                    pending.Enqueue((normalized, sitemapId, level + 1));
                }
            }
        }

        await writer.MarkSitemapsDoneAsync(cancellationToken).ConfigureAwait(false);
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Info,
            "sitemaps",
            urlCount > 0 ? $"Read {sitemapCount} sitemap(s) listing {urlCount:N0} URLs." : "No sitemap URLs found."));
    }

    private async Task<(List<string> Children, int UrlCount)> ReadSitemapAsync(Uri uri, int sitemapId, bool isProbe, CancellationToken cancellationToken)
    {
        List<string> children = [];
        var urlCount = 0;
        var isIndex = false;
        int? statusCode = null;
        string? error = null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(60, options.RequestTimeoutSeconds * 3)));

            var (response, lease, location) = await SendFollowingRedirectsAsync(uri, timeout.Token).ConfigureAwait(false);
            using var heldLease = lease;
            using var disposeResponse = response;
            uri = location;
            statusCode = (int)response.StatusCode;
            if (statusCode is < 200 or >= 300)
            {
                error = $"HTTP {statusCode}";
            }
            else
            {
                await using var body = await HttpFetcher.OpenBodyAsync(response, timeout.Token).ConfigureAwait(false);
                await using var content = IsGzipFile(uri, response.Content.Headers) ? new GZipStream(body, CompressionMode.Decompress) : body;
                var isText = response.Content.Headers.ContentType?.MediaType == "text/plain" ||
                             uri.AbsolutePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                var batch = new List<OutLink>(BATCH_SIZE);
                await foreach (var entry in SitemapReader.ReadAsync(content, isText, timeout.Token).ConfigureAwait(false))
                {
                    if (entry.IsSitemap)
                    {
                        isIndex = true;
                        children.Add(entry.Location);
                        continue;
                    }

                    if (!Uri.TryCreate(entry.Location, UriKind.Absolute, out var target) || !UrlNormalizer.IsHttp(target))
                    {
                        continue;
                    }

                    batch.Add(links.Create(target, LinkKind.Hyperlink, LinkFlags.None, LinkPosition.Content, null));
                    urlCount++;
                    if (batch.Count == BATCH_SIZE)
                    {
                        await writer.DiscoverAsync(batch, DiscoverySource.Sitemap, sitemapId, cancellationToken).ConfigureAwait(false);
                        batch = new List<OutLink>(BATCH_SIZE);
                    }
                }

                if (batch.Count > 0)
                {
                    await writer.DiscoverAsync(batch, DiscoverySource.Sitemap, sitemapId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = "Timed out";
        }
        catch (Exception exception) when (exception is HttpRequestException or XmlException or IOException or InvalidDataException)
        {
            error = exception.Message;
            logger.LogWarning("Sitemap {Url} could not be read: {Error}", uri, exception.Message);
        }

        await writer.CompleteSitemapAsync(new SitemapRecord(sitemapId, statusCode, isIndex, urlCount, error, isProbe), cancellationToken).ConfigureAwait(false);
        return (children, urlCount);
    }

    /// <summary>
    /// Sitemaps often redirect (Yoast's /sitemap.xml goes to /sitemap_index.xml, http to https), so follow up to
    /// <see cref="MAX_REDIRECTS"/> hops. The gate lease for the last hop is held while its body is read.
    /// </summary>
    private async Task<(HttpResponseMessage Response, HostGateLease Lease, Uri Location)> SendFollowingRedirectsAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var hop = 0; ; hop++)
        {
            var lease = await gates.For(uri).AcquireAsync(cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                response = await fetcher.SendAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lease.Dispose();
                throw;
            }

            if ((int)response.StatusCode is < 300 or >= 400 || hop == MAX_REDIRECTS || response.Headers.Location is not { } location ||
                !Uri.TryCreate(uri, location, out var next) || !UrlNormalizer.IsHttp(next))
            {
                return (response, lease, uri);
            }

            response.Dispose();
            lease.Dispose();
            uri = next;
        }
    }

    /// <summary>A .gz sitemap served as a file, as opposed to plain XML that was only gzipped in transit.</summary>
    private static bool IsGzipFile(Uri uri, HttpContentHeaders headers)
    {
        var transportGzip = headers.ContentEncoding.Any(encoding => encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase));
        var mediaType = headers.ContentType?.MediaType;
        var looksGzip = mediaType is "application/gzip" or "application/x-gzip" ||
                        uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        return looksGzip && !transportGzip;
    }
}
