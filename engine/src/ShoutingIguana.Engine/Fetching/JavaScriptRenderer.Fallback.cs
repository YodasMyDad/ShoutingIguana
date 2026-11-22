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
    /// The page a headless browser got for <paramref name="uri"/>, after a host blocked plain HTTP. The same browser
    /// as rendering, started the first time it's needed. One context per <paramref name="hostKey"/>, kept for the
    /// crawl. Null when the browser can't run, the navigation fails, or the page leaves this URL: the caller keeps
    /// the HTTP response. The HTML is the page. The caller parses it once. This is not JavaScript rendering.
    /// </summary>
    public async Task<FetchResponse?> LoadAsync(Uri uri, string hostKey, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _unavailable) == 1)
        {
            return null;
        }

        var session = _fallbackHosts.GetOrAdd(hostKey, static _ => new FallbackHost());
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            IPage? page = null;
            try
            {
                var browser = await BrowserAsync().ConfigureAwait(false);
                if (browser is null)
                {
                    return null;
                }

                session.Context ??= await OpenContextAsync(browser).ConfigureAwait(false);

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
        finally
        {
            session.Gate.Release();
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

        _fallbackHosts.Clear();
    }

    private async Task<FetchResponse> DocumentAsync(Uri uri, IResponse navigation, IPage page, int elapsedMs)
    {
        List<ResponseHeader> headers = [];
        foreach (var (name, value) in await navigation.AllHeadersAsync().ConfigureAwait(false))
        {
            headers.Add(new ResponseHeader(name.ToLowerInvariant(), value));
        }

        var status = navigation.Status;
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
            body = await page.ContentAsync().ConfigureAwait(false);
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
            TimeToFirstByteMs = elapsedMs,
            TotalMs = elapsedMs,
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
    }
}
