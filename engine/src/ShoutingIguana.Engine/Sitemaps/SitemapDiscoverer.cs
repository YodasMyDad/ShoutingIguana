using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Xml;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Rules;
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
    PauseGate pause,
    TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>
    /// Starts the error of a sitemap the crawler chose not to read (one past the limits below), so the analysis can tell
    /// that the sitemaps it has aren't all there are.
    /// </summary>
    internal const string NOT_READ_ERROR = "Not read";

    internal const int MAX_SITEMAPS = 1_000;
    internal const int MAX_NESTING = 3;
    private const int MAX_REDIRECTS = 5;

    /// <summary>Times a sitemap answered 429 is asked, each after the wait its host or section asked for, as a page is.</summary>
    private const int MAX_TRIES = 4;

    private static readonly TimeSpan MAX_RETRY_WAIT = TimeSpan.FromMinutes(2);
    private const int BATCH_SIZE = 1_000;

    public async Task RunAsync(Uri startUri, CancellationToken cancellationToken)
    {
        // A robots.txt that couldn't be read says nothing about the sitemaps it declares, so it is asked once more when its
        // cool-down ends rather than leave them out of the crawl. Nothing is read while the crawl is paused.
        await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
        var rules = await robots.GetAfterCoolDownAsync(startUri, pause, cancellationToken).ConfigureAwait(false);

        // Sitemap: lines should be absolute, but relative ones are common; they resolve against robots.txt.
        var robotsUri = new Uri(startUri, "/robots.txt");
        var declared = rules.Sitemaps.Select(sitemap => UrlNormalizer.TryResolve(robotsUri, sitemap, out var resolved) ? resolved.AbsoluteUri : sitemap);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Url, int? ParentId, int Level)>();
        foreach (var candidate in declared.Concat(options.ExtraSitemaps))
        {
            if (UrlNormalizer.TryNormalize(candidate, out var normalized) && seen.Add(normalized))
            {
                pending.Enqueue((normalized, null, 0));
            }
        }

        // Nothing usable declares a sitemap, so try the conventional location. Analysis treats a failed probe as "no
        // sitemap" rather than as a broken one.
        var isProbe = pending.Count == 0;
        if (isProbe && UrlNormalizer.TryNormalize(startUri.GetLeftPart(UriPartial.Authority) + "/sitemap.xml", out var probe))
        {
            seen.Add(probe);
            pending.Enqueue((probe, null, 0));
        }

        var sitemapCount = 0;
        var urlCount = 0;
        var tooDeepRecorded = false;
        while (pending.Count > 0 && sitemapCount < MAX_SITEMAPS)
        {
            await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
            var (url, parentId, level) = pending.Dequeue();
            sitemapCount++;
            var sitemapId = await writer.RegisterSitemapAsync(url, parentId, cancellationToken).ConfigureAwait(false);
            var (children, listed) = await ReadSitemapAsync(new Uri(url), sitemapId, isProbe && level == 0, cancellationToken).ConfigureAwait(false);
            urlCount += listed;

            foreach (var child in children)
            {
                if (!UrlNormalizer.TryNormalize(child, out var normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                if (level < MAX_NESTING)
                {
                    pending.Enqueue((normalized, sitemapId, level + 1));
                }
                else if (!tooDeepRecorded)
                {
                    tooDeepRecorded = true;
                    await RecordNotReadAsync(normalized, sitemapId, $"sitemap indexes nested more than {MAX_NESTING} deep aren't followed", cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (pending.TryDequeue(out var dropped))
        {
            await RecordNotReadAsync(dropped.Url, dropped.ParentId,
                $"the crawler reads at most {MAX_SITEMAPS.ToString("N0", CultureInfo.InvariantCulture)} sitemaps", cancellationToken).ConfigureAwait(false);
        }

        await writer.MarkSitemapsDoneAsync(cancellationToken).ConfigureAwait(false);
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Info,
            "sitemaps",
            urlCount > 0 ? $"Read {sitemapCount} sitemap(s) listing {urlCount:N0} URLs." : "No sitemap URLs found.")
        {
            Data = new CrawlNoticeData(Count: sitemapCount, UrlCount: urlCount)
        });
    }

    /// <summary>
    /// Records the first sitemap left unread for a reason, without a status, so the sitemaps read aren't taken for all
    /// there are. The notice says there may be more like it.
    /// </summary>
    private async Task RecordNotReadAsync(string url, int? parentId, string reason, CancellationToken cancellationToken)
    {
        var sitemapId = await writer.RegisterSitemapAsync(url, parentId, cancellationToken).ConfigureAwait(false);
        await writer.CompleteSitemapAsync(new SitemapRecord(sitemapId, null, false, 0, $"{NOT_READ_ERROR}: {reason}"), cancellationToken).ConfigureAwait(false);
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Warning,
            "sitemaps-not-read",
            $"Not every sitemap was read: {reason}, so {url} was left out, and any more like it. Checks against the sitemaps only see the ones read.")
        {
            Data = new CrawlNoticeData(Url: url, Reason: reason)
        });
    }

    private async Task<(List<string> Children, int UrlCount)> ReadSitemapAsync(Uri uri, int sitemapId, bool isProbe, CancellationToken cancellationToken)
    {
        List<string> children = [];
        var batch = new List<OutLink>(BATCH_SIZE);
        var lastmods = new List<long?>(BATCH_SIZE);
        CountingStream? counted = null;
        var urlCount = 0;
        var isIndex = false;
        var bodyRead = false;
        var turnedAway = false;
        var brokeOff = false;
        int? statusCode = null;
        string? error = null;

        try
        {
            // The deadline starts with the first request, not with the wait for a turn at the gate: a host that asked
            // for a minute's pause would otherwise time its sitemap out before it was asked.
            using var deadline = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

            var (response, lease, location) = await SendAskingAgainAfter429Async(uri, deadline, timeout.Token, cancellationToken).ConfigureAwait(false);
            using var heldLease = lease;
            using var disposeResponse = response;
            uri = location;
            statusCode = (int)response.StatusCode;
            if (statusCode is < 200 or >= 300)
            {
                // The status by its name, as an issue's detail names it: "404 Not Found" ("HTTP 520" for one with none).
                error = HttpStatusNames.Describe(statusCode.Value);

                // Asked to slow down at every try, or a firewall answered in the file's place: not the file's own answer.
                turnedAway = BlockDetector.Detect(statusCode.Value, HttpFetcher.CollectHeaders(response), null) is not null;
            }
            else
            {
                await using var body = await HttpFetcher.OpenBodyAsync(response, timeout.Token).ConfigureAwait(false);
                await using var unpacked = IsGzipFile(uri, response.Content.Headers) ? new GZipStream(body, CompressionMode.Decompress) : body;

                // The protocol's 50 MB limit is on the file uncompressed: counted as it's read.
                await using var content = counted = new CountingStream(unpacked);
                var isText = MediaTypeOf(response.Content.Headers) == "text/plain" ||
                             uri.AbsolutePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                // From here on the file is being read: what it lists is counted, to its end or to where it fails.
                bodyRead = true;
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
                    lastmods.Add(SitemapLastmod.Parse(entry.Lastmod));
                    urlCount++;
                    if (batch.Count == BATCH_SIZE)
                    {
                        await writer.DiscoverAsync(batch, lastmods, sitemapId, cancellationToken).ConfigureAwait(false);
                        batch = new List<OutLink>(BATCH_SIZE);
                        lastmods = new List<long?>(BATCH_SIZE);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = "Timed out";
            brokeOff = statusCode is not null;
        }
        catch (Exception exception) when (exception is HttpRequestException or XmlException or IOException or InvalidDataException)
        {
            error = exception.Message;

            // The connection's fault after the status came, not the file's: broken XML or a bad gzip is what the file says.
            brokeOff = statusCode is not null && exception is HttpRequestException or IOException;
            logger.LogWarning("Sitemap {Url} could not be read: {Error}", uri, exception.Message);
        }

        // Also after a parse error or a timeout part-way through: the URLs read before it are listed, and counted.
        if (batch.Count > 0)
        {
            await writer.DiscoverAsync(batch, lastmods, sitemapId, cancellationToken).ConfigureAwait(false);
        }

        // Every sitemap the file lists is counted, whether or not the crawl goes on to read it: past its limit of sitemaps,
        // or of nesting, an index still says how many it names.
        await writer.CompleteSitemapAsync(
                new SitemapRecord(sitemapId, statusCode, isIndex, urlCount, error, isProbe, bodyRead ? children.Count : null, bodyRead ? counted?.BytesRead : null,
                    turnedAway, brokeOff),
                cancellationToken)
            .ConfigureAwait(false);
        return (children, urlCount);
    }

    /// <summary>
    /// Sends for a sitemap, and asks again if its host answers 429, up to <see cref="MAX_TRIES"/> times in all, as a page
    /// would be: after the pause the host asked for (the gate keeps it), or at the section's turn when the host limits
    /// the section apart. The deadline restarts with each try's turn at the gate.
    /// </summary>
    private async Task<(HttpResponseMessage Response, HostGateLease Lease, Uri Location)> SendAskingAgainAfter429Async(Uri uri,
        CancellationTokenSource deadline, CancellationToken sending, CancellationToken cancellationToken)
    {
        for (var tries = 1; ; tries++)
        {
            var (response, lease, location) = await SendFollowingRedirectsAsync(uri, cancellationToken, sending,
                () => deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(60, options.RequestTimeoutSeconds * 3)))).ConfigureAwait(false);
            var retryAfter = fetcher.GetRetryAfter(response);
            lease.ReportStatus((int)response.StatusCode, retryAfter, location);
            if ((int)response.StatusCode != 429 || tries == MAX_TRIES)
            {
                return (response, lease, location);
            }

            response.Dispose();
            lease.Dispose();
            logger.LogInformation("Sitemap {Url} answered 429; asking again", location);
            await WaitAfter429Async(location, retryAfter, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Until the section's turn when its host limits it apart; otherwise for the Retry-After, or ten seconds.</summary>
    private async Task WaitAfter429Async(Uri location, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (gates.For(location).LimitedSectionOf(location) is { } section)
        {
            while (!section.TryStart(timeProvider.GetUtcNow()))
            {
                var wait = section.NextDue - timeProvider.GetUtcNow();
                await Task.Delay(wait < TimeSpan.FromSeconds(1) && wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        var asked = retryAfter is { } given && given > TimeSpan.Zero ? given : SectionPace.DEFAULT_INTERVAL;
        await Task.Delay(asked < MAX_RETRY_WAIT ? asked : MAX_RETRY_WAIT, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sitemaps often redirect (Yoast's /sitemap.xml goes to /sitemap_index.xml, http to https), so follow up to
    /// <see cref="MAX_REDIRECTS"/> hops. The gate lease for the last hop is held while its body is read.
    /// </summary>
    /// <param name="waiting">Ends the waits for a turn at the gate: the crawl stopping.</param>
    /// <param name="sending">Ends the requests: the crawl stopping, or the sitemap's deadline.</param>
    /// <param name="startDeadline">Called once, when the first request's turn has come.</param>
    private async Task<(HttpResponseMessage Response, HostGateLease Lease, Uri Location)> SendFollowingRedirectsAsync(Uri uri,
        CancellationToken waiting, CancellationToken sending, Action startDeadline)
    {
        for (var hop = 0; ; hop++)
        {
            var lease = await gates.For(uri).AcquireAsync(waiting).ConfigureAwait(false);
            if (hop == 0)
            {
                startDeadline();
            }

            HttpResponseMessage response;
            try
            {
                response = await fetcher.SendAsync(uri, sending).ConfigureAwait(false);
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
        var looksGzip = MediaTypeOf(headers) is "application/gzip" or "application/x-gzip" ||
                        uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        return looksGzip && !transportGzip;
    }

    private static string? MediaTypeOf(HttpContentHeaders headers) => ContentTypeHeader.MediaTypeOf(ContentTypeHeader.Read(headers));
}
