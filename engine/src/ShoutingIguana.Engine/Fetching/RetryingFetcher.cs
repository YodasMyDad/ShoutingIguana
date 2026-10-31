using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The polite way to fetch, shared by pages and robots.txt: through the host's gate, telling the gate how the host
/// answered, and retrying transient failures (no response, 429, 502, 503, 504) after an exponential backoff or the
/// server's Retry-After, capped at a minute. When <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> URLs in a row get no
/// answer, another site's remaining URLs fail at once instead of each costing several timeouts, and the site being
/// crawled is waited out: its gate pauses for <see cref="UNREACHABLE_PAUSE"/> after every unanswered attempt.
/// </summary>
internal sealed class RetryingFetcher(
    HttpFetcher fetcher,
    HostGateRegistry gates,
    CrawlScope scope,
    int maxRetries,
    ICrawlObserver observer,
    TimeProvider timeProvider,
    Random? random = null)
{
    private const int MAX_JITTER_MS = 250;
    private static readonly TimeSpan MAX_RETRY_WAIT = TimeSpan.FromSeconds(60);

    /// <summary>How long the site's own gate holds every request after an unanswered attempt while the site is unreachable.</summary>
    internal static readonly TimeSpan UNREACHABLE_PAUSE = TimeSpan.FromMinutes(1);

    private readonly Random _random = random ?? Random.Shared;

    /// <param name="singleAttempt">No retries: the URL already had them, and this is its one more try at the end of the crawl.</param>
    public async Task<FetchResponse> FetchAsync(Uri uri, FetchBody body, CancellationToken cancellationToken, int? maxBodyBytes = null, bool singleAttempt = false)
    {
        var gate = gates.For(uri);
        var isInternal = scope.IsInternal(uri);
        var retries = singleAttempt ? 0 : maxRetries;
        for (var attempt = 0; ; attempt++)
        {
            if (!isInternal && gate.IsUnreachable)
            {
                return NotChecked(uri);
            }

            FetchResponse response;
            using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                response = await fetcher.FetchAsync(uri, body, cancellationToken, maxBodyBytes).ConfigureAwait(false);
            }

            gate.Report(SignalFor(response), response.RetryAfter);
            var answered = response.StatusCode is not null;
            if (isInternal && !answered && gate.IsUnreachable)
            {
                gate.Pause(UNREACHABLE_PAUSE);
            }

            if (attempt >= retries || !IsRetryable(response))
            {
                if (gate.ReportUrl(answered) && isInternal)
                {
                    gate.Pause(UNREACHABLE_PAUSE);
                    observer.OnNotice(new CrawlNotice(
                        CrawlNoticeLevel.Warning,
                        "site-unreachable",
                        $"{uri.Host} stopped answering: {HostGate.UNREACHABLE_AFTER_URLS} URLs in a row got no response ({response.Error}). The crawler waits {UNREACHABLE_PAUSE.TotalMinutes:0} minute between tries until it answers again, and retries the URLs that failed at the end of the crawl."));
                }

                return response;
            }

            await Task.Delay(RetryDelay(attempt, response.RetryAfter), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A URL on another site that has stopped answering, failed without a request.</summary>
    private static FetchResponse NotChecked(Uri uri) => new()
    {
        RequestUri = uri,
        Outcome = FetchOutcome.NetworkError,
        Error = $"Not checked: {uri.Host} stopped answering ({HostGate.UNREACHABLE_AFTER_URLS} URLs in a row got no response)"
    };

    private static HostSignal SignalFor(FetchResponse response) => response switch
    {
        { Outcome: FetchOutcome.Blocked } or { StatusCode: 429 or 503 } => HostSignal.Throttled,
        { Outcome: FetchOutcome.Timeout or FetchOutcome.NetworkError } or { StatusCode: 502 or 504 } => HostSignal.Failed,
        _ => HostSignal.Success
    };

    private static bool IsRetryable(FetchResponse response) =>
        response.IsTransientFailure || response.StatusCode is 429 or 502 or 503 or 504;

    private TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait < MAX_RETRY_WAIT ? wait : MAX_RETRY_WAIT;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(_random.Next(0, MAX_JITTER_MS));
    }
}
