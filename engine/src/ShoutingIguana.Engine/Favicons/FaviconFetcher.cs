using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Favicons;

/// <summary>
/// Fetches the site's icon once, beside the crawl, and has the writer keep it. The first page of the site that a run
/// parses starts it: in a new crawl that is the start URL's page (or the page it redirects to), as nothing else is known
/// until it has been read. From then on it is a job of its own, and the crawl neither waits for it nor hears of its
/// failures.
/// <list type="bullet">
/// <item><b>Where it looks</b>: the icons that page links, best first, then <c>/favicon.ico</c>
/// (<see cref="FaviconCandidates"/>). The first that turns out to be an image the engine keeps is the icon
/// (<see cref="FaviconSniffer"/>): the bytes say what it is, whatever the site calls it.</item>
/// <item><b>Politely</b>: each request waits for its origin's robots.txt and obeys it when the crawl does, takes its turn
/// at the host's gate as any request to that host does (its concurrency, its rate, a crawl delay, a backoff), stops
/// while the crawl is paused, and goes to another site only when the crawl checks other sites' links. A redirect is
/// followed a few hops, each held to the same rules. A request has the crawl's own timeout.</item>
/// <item><b>It is no URL of the crawl</b>: nothing is discovered, recorded or counted for it, and a host that fails it is
/// not a site that is down. Only its origin's robots.txt, if the crawl hadn't read it yet, is kept as any robots.txt
/// the crawl reads is.</item>
/// <item><b>It never holds a crawl up</b>: a crawl that ends first gives it <see cref="FINISH_GRACE"/> to finish (a crawl
/// of a handful of pages is over before its icon arrives), then goes on without. A crawl that is stopped, or fails,
/// doesn't wait at all.</item>
/// </list>
/// A crawl that has its icon keeps it: a run that continues it asks for none. One that has none (its site had none the
/// engine keeps, its start page never answered, or an engine from before icons were kept made it) looks once more when
/// it is continued, from the first page that run parses: the same few requests, by the same rules.
/// </summary>
internal sealed class FaviconFetcher(
    CrawlOptions options,
    HttpFetcher fetcher,
    HostGateRegistry gates,
    RobotsCache robots,
    CrawlWriter writer,
    CrawlScope scope,
    PauseGate pause,
    TimeProvider timeProvider,
    ILogger logger,
    CancellationToken stopping)
{
    private const int MAX_REDIRECTS = 3;
    private const int READ_BUFFER_BYTES = 16 * 1024;

    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private Task _work = Task.CompletedTask;
    private int _started;

    /// <summary>How long a crawl that has completed waits for an icon still being fetched, by the wall clock, as a request's timeout is.</summary>
    internal static readonly TimeSpan FINISH_GRACE = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A page the crawl has parsed. The first of the site's own names the icons, and the fetch begins; every other page
    /// is passed over, and so is every page of a crawl that has its icon. Called by the crawl's workers, and returns at
    /// once.
    /// </summary>
    public void Offer(FrontierItem item, Uri page, PageFacts facts)
    {
        // Every page of the crawl comes by here, so the pages after the first cost one read.
        if (Volatile.Read(ref _started) != 0 || item is not { IsInternal: true, Mode: FetchMode.Crawl } || writer.HasFavicon ||
            Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var candidates = FaviconCandidates.Rank(page, facts.Icons);
        Volatile.Write(ref _work, Task.Run(() => FetchAsync(candidates, _stop.Token)));
    }

    /// <summary>
    /// Ends the fetch as the crawl ends: given a moment to finish when the crawl has completed, stopped at once otherwise.
    /// Never throws.
    /// </summary>
    public async Task FinishAsync(bool crawlCompleted)
    {
        var work = Volatile.Read(ref _work);
        if (crawlCompleted && !work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(FINISH_GRACE)).ConfigureAwait(false);
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await work.ConfigureAwait(false);
        _stop.Dispose();
    }

    /// <summary>Tries each candidate in turn and keeps the first icon. Whatever goes wrong ends here: the crawl never hears of it.</summary>
    private async Task FetchAsync(IReadOnlyList<Uri> candidates, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var candidate in candidates)
            {
                if (await TryAsync(candidate, cancellationToken).ConfigureAwait(false) is { } icon)
                {
                    await writer.RecordFaviconAsync(icon, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Kept the site's icon from {Url}: {ContentType}, {Width}x{Height}, {Bytes} bytes",
                        icon.Url, icon.ContentType, icon.Width, icon.Height, icon.Body.Length);
                    return;
                }
            }

            logger.LogInformation("The site has no icon the crawl keeps: tried {Count} URLs", candidates.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The crawl ended first.
        }
        catch (ChannelClosedException)
        {
            // The writer has closed, or failed: the crawl reports that itself.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Fetching the site's icon failed");
        }
    }

    /// <summary>One candidate: the icon it is, or null when it can't be asked for, doesn't answer with one, or isn't an image the engine keeps.</summary>
    private async Task<FaviconRecord?> TryAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            for (var hop = 0; hop <= MAX_REDIRECTS; hop++)
            {
                await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!await MayAskAsync(uri, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                // The wait for a turn at the gate isn't the request's time: a crawl delay can be longer than any timeout.
                using var lease = await gates.For(uri).AcquireAsync(cancellationToken).ConfigureAwait(false);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                using var response = await fetcher.SendAsync(uri, timeout.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400)
                {
                    if (response.Headers.Location is not { } location || !Uri.TryCreate(uri, location, out var next) || !UrlNormalizer.IsHttp(next))
                    {
                        return null;
                    }

                    uri = next;
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    continue;
                }

                // Too large by its own account: not read at all.
                if (status is < 200 or >= 300 || response.Content.Headers.ContentLength > FaviconSniffer.MAX_BYTES)
                {
                    return null;
                }

                var body = await ReadAsync(response, timeout.Token).ConfigureAwait(false);
                return body is not null && FaviconSniffer.Sniff(body) is { } image
                    ? new FaviconRecord(uri.AbsoluteUri, image.ContentType, image.Width, image.Height, body, timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
                    : null;
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request timed out.
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            logger.LogDebug(exception, "The icon at {Url} couldn't be fetched", uri);
            return null;
        }
    }

    /// <summary>
    /// Whether the crawl may ask for the URL at all: another site's only when the crawl checks other sites' links; and,
    /// when the crawl obeys robots.txt, any URL only once its origin's has been read and allows it. A robots.txt that
    /// couldn't be read refuses, as it does every URL.
    /// </summary>
    private async Task<bool> MayAskAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!scope.IsInternal(uri) && !options.CheckExternalLinks)
        {
            return false;
        }

        if (!options.RespectRobotsTxt)
        {
            return true;
        }

        var rules = await robots.GetAsync(uri).WaitAsync(cancellationToken).ConfigureAwait(false);
        return rules.IsAllowed(uri.PathAndQuery);
    }

    /// <summary>The body, with any Content-Encoding removed, or null when it's more than an icon may be: it's then read no further.</summary>
    private static async Task<byte[]?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await HttpFetcher.OpenBodyAsync(response, cancellationToken).ConfigureAwait(false);
        using var kept = new MemoryStream();
        var buffer = new byte[READ_BUFFER_BYTES];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (kept.Length + read > FaviconSniffer.MAX_BYTES)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }
}
