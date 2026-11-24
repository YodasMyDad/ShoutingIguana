using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

internal sealed partial class JavaScriptRenderer
{
    /// <summary>One browser context per host the crawl is reading in the browser, so a clearance cookie is sent again.</summary>
    private readonly ConcurrentDictionary<string, FallbackHost> _fallbackHosts = new(StringComparer.Ordinal);

    /// <summary>
    /// The page a browser got for <paramref name="uri"/>, after a host blocked plain HTTP. Which browser depends on
    /// the block. A check (<see cref="BlockDetector.IsCheck"/>: a challenge page, a CAPTCHA) goes to the computer's own
    /// Chrome or Edge (<see cref="LoadInstalledAsync"/>): the headless browser has never passed one, and trying it
    /// first only makes the site warier of this address. Any other block goes to the headless browser, the same one as
    /// rendering, and to the installed one if that is refused too. A host stays with the browser that read it. One
    /// page of a <paramref name="hostKey"/> at a time. Null when no browser can run, the navigation fails, or the page
    /// leaves this URL: the caller keeps the HTTP response. A page still refused comes back as the block, never as
    /// the site's page. The HTML is the page. The caller parses it once. This is not JavaScript rendering.
    /// </summary>
    /// <param name="blockReason">What plain HTTP was refused with. Null for a host a browser already reads.</param>
    /// <param name="mayAsk">
    /// False for a check of another version of the site (<c>http</c>, or without <c>www</c>): it is read only in a
    /// browser that already reads the host, nobody is asked to complete a check for it, and a refusal of it leaves
    /// the host's pages where they were.
    /// </param>
    public async Task<FetchResponse?> LoadAsync(Uri uri, string hostKey, CancellationToken cancellationToken, string? blockReason = null,
        bool mayAsk = true)
    {
        var session = _fallbackHosts.GetOrAdd(hostKey, static _ => new FallbackHost());
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.GaveUp || (!mayAsk && !session.Headless && !session.Installed))
            {
                return null;
            }

            FetchResponse? headless = null;
            var installedFirst = blockReason is not null && BlockDetector.IsCheck(blockReason) && CanUseInstalled;
            if (session.Headless || (!session.Installed && !installedFirst))
            {
                headless = await LoadHeadlessAsync(uri, session, cancellationToken).ConfigureAwait(false);
                if (session.Headless || headless is { Outcome: not FetchOutcome.Blocked } || headless?.IsRateLimited == true)
                {
                    session.Headless |= headless is not null;
                    return headless;
                }
            }

            var installed = await LoadInstalledAsync(uri, session, mayAsk, cancellationToken).ConfigureAwait(false);
            if (installed is { Outcome: not FetchOutcome.Blocked })
            {
                session.Installed = true;
                return installed;
            }

            if (!mayAsk)
            {
                return installed;
            }

            if (installed is null && installedFirst && !session.Installed && !CanUseInstalled)
            {
                // The computer's browser couldn't be started after all. The headless one is all there is.
                headless = await LoadHeadlessAsync(uri, session, cancellationToken).ConfigureAwait(false);
                if (headless is { Outcome: not FetchOutcome.Blocked })
                {
                    session.Headless = true;
                    return headless;
                }
            }

            // A check that could not be passed is not tried on every page after it: the host stays blocked.
            if (!session.Installed || (installed?.BlockReason is { } reason && BlockDetector.IsCheck(reason)))
            {
                session.GaveUp = true;
            }

            return installed ?? headless;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    /// <summary>The page the headless browser got. Null when it can't run, the navigation fails, or the page leaves this URL.</summary>
    private async Task<FetchResponse?> LoadHeadlessAsync(Uri uri, FallbackHost session, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _unavailable) == 1)
        {
            return null;
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        IPage? page = null;
        try
        {
            var browser = await BrowserAsync().ConfigureAwait(false);
            if (browser is null)
            {
                return null;
            }

            session.Context ??= await OpenContextAsync(browser, session.Held).ConfigureAwait(false);

            var started = Stopwatch.StartNew();
            page = await session.Context.NewPageAsync().ConfigureAwait(false);
            using var _ = cancellationToken.Register(() => page.CloseAsync());
            IResponse? navigation;
            try
            {
                navigation = await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = options.RequestTimeoutSeconds * 1000f
                }).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = SETTLE_MS })
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The document is parsed. A challenge that already cleared doesn't need a quiet network.
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            {
            }

            if (navigation is null || !StayedForFallback(page.Url, uri))
            {
                logger.LogDebug("Browser fallback for {Url} landed on {Final}; keeping the HTTP response", uri, page.Url);
                return null;
            }

            return await DocumentAsync(uri, navigation, page, (int)started.ElapsedMilliseconds).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A crawl that is stopping closed the page: the URL is not read, rather than kept as the block.
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug(ex, "Browser fallback for {Url} failed; keeping the HTTP response", uri);
            return null;
        }
        finally
        {
            if (page is not null)
            {
                try
                {
                    await page.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Closing the fallback page for {Url} failed", uri);
                }
            }

            _slots.Release();
        }
    }

    private async Task CloseFallbackAsync()
    {
        foreach (var session in _fallbackHosts.Values)
        {
            try
            {
                if (session.Context is not null)
                {
                    await session.Context.CloseAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Closing a fallback browser context failed");
            }

            session.Gate.Dispose();
        }

        foreach (var (host, session) in _fallbackHosts.Where(host => host.Value.ReadInInstalled > 0))
        {
            logger.LogInformation("{Count} pages of {Host} were read in the computer's own browser", session.ReadInInstalled, host);
        }

        _fallbackHosts.Clear();
        await CloseInstalledAsync().ConfigureAwait(false);
    }

    private async Task<FetchResponse> DocumentAsync(Uri uri, IResponse navigation, IPage page, int elapsedMs)
    {
        List<ResponseHeader> headers = [];
        foreach (var (name, value) in await navigation.AllHeadersAsync().ConfigureAwait(false))
        {
            headers.Add(new ResponseHeader(name.ToLowerInvariant(), value));
        }

        var html = await page.ContentAsync().ConfigureAwait(false);
        return BrowserDocument(uri, navigation.Status, headers, html, elapsedMs, elapsedMs);
    }

    /// <summary>A document a browser was answered with, as the crawl stores a fetch: its HTML only when it is the site's page.</summary>
    private FetchResponse BrowserDocument(Uri uri, int status, List<ResponseHeader> headers, string html, int timeToFirstByteMs, int totalMs)
    {
        var rawType = headers.FirstOrDefault(header => header.Name == "content-type").Value;
        var mediaType = ContentTypeHeader.MediaTypeOf(rawType);
        var isHtml = mediaType is "text/html" or "application/xhtml+xml" or null;
        if (mediaType is null && isHtml)
        {
            mediaType = "text/html";
        }

        string? body = null;
        long? bodyBytes = null;
        var truncated = false;
        if (isHtml)
        {
            body = html;
            bodyBytes = Encoding.UTF8.GetByteCount(body);
            if (bodyBytes > options.MaxHtmlBytes)
            {
                body = TruncateUtf8(body, options.MaxHtmlBytes);
                bodyBytes = Encoding.UTF8.GetByteCount(body);
                truncated = true;
            }
        }

        var blockReason = BlockDetector.Detect(status, headers, status is >= 200 and < 300 ? null : body);
        return new FetchResponse
        {
            RequestUri = uri,
            Outcome = HttpFetcher.Classify(status, blockReason),
            StatusCode = status,
            Headers = headers,
            ContentType = mediaType,
            IsHtml = isHtml,
            Body = blockReason is null && isHtml ? body : null,
            BodyBytes = bodyBytes,
            BodyTruncated = truncated,
            TimeToFirstByteMs = timeToFirstByteMs,
            TotalMs = totalMs,
            BlockReason = blockReason,
            FromBrowser = true
        };
    }

    /// <summary>The same path on the same site. A challenge that only changes http to https still counts.</summary>
    private static bool StayedForFallback(string pageUrl, Uri requested) =>
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var final) &&
        CrawlScope.SiteHost(UrlNormalizer.GetHost(final)) == CrawlScope.SiteHost(UrlNormalizer.GetHost(requested)) &&
        string.Equals(final.AbsolutePath, requested.AbsolutePath, StringComparison.Ordinal) &&
        string.Equals(final.Query, requested.Query, StringComparison.Ordinal);

    private static string TruncateUtf8(string text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var length = Math.Min(bytes.Length, maxBytes);
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>The host's browser context, and a gate so one of its pages is open at a time.</summary>
    private sealed class FallbackHost
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IBrowserContext? Context { get; set; }

        public HeldRequests Held { get; } = new();

        /// <summary>The headless browser reads this host.</summary>
        public bool Headless { get; set; }

        /// <summary>The headless browser was refused, and the computer's own browser reads this host.</summary>
        public bool Installed { get; set; }

        /// <summary>No browser got past the host's block. It is not tried again in this crawl.</summary>
        public bool GaveUp { get; set; }

        /// <summary>How many times a person has been asked to complete this host's check.</summary>
        public int PersonChecks { get; set; }

        public FallbackVerdict Verdict { get; set; }

        /// <summary>For <see cref="FallbackVerdict.WentElsewhere"/>: the address the page left for once its check had passed.</summary>
        public string? LandedOn { get; set; }

        /// <summary>How many of the host's pages the installed browser has read.</summary>
        public int ReadInInstalled { get; set; }
    }
}
