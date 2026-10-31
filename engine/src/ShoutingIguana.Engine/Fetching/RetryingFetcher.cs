using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The polite way to fetch, shared by pages and robots.txt: through the host's gate, telling the gate how the host
/// answered, and retrying transient failures (no response, 429, 502, 503, 504) after an exponential backoff or the
/// server's Retry-After, capped at a minute. When <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> URLs in a row on one
/// origin get no answer, another site's remaining URLs there fail at once instead of each costing several timeouts. On
/// the site being crawled, robots.txt is asked first (headers only), at that origin and then at the start URL's: an
/// answer at the origin means the URLs failed for reasons of their own; an answer only at the start URL's means just
/// that origin is gone (a dead www twin, a closed http port, a subdomain), and it fails fast like another site. With no
/// answer at all the site is down, and it is waited out: its gate pauses for <see cref="UNREACHABLE_PAUSE"/> after every
/// unanswered attempt until any of its origins answers.
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
    /// <summary>Starts the error of a URL failed without a request, so the analysis can tell it from one that failed.</summary>
    internal const string NOT_CHECKED_ERROR = "Not checked";

    private const int MAX_JITTER_MS = 250;
    private static readonly TimeSpan MAX_RETRY_WAIT = TimeSpan.FromSeconds(60);

    /// <summary>How long the site's own gate holds every request after an unanswered attempt while the site is unreachable.</summary>
    internal static readonly TimeSpan UNREACHABLE_PAUSE = TimeSpan.FromMinutes(1);

    /// <summary>How long the site may go without answering before the crawl stops, resumable, instead of waiting on.</summary>
    internal static readonly TimeSpan GIVE_UP_AFTER = TimeSpan.FromMinutes(10);

    private readonly Random _random = random ?? Random.Shared;
    private readonly HostGate _siteGate = gates.For(scope.StartOrigin);
    private readonly OriginReachability _startOrigin = gates.OriginOf(scope.StartOrigin);

    /// <param name="singleAttempt">No retries: the URL already had them, and this is its one more try at the end of the crawl.</param>
    public async Task<FetchResponse> FetchAsync(Uri uri, FetchBody body, CancellationToken cancellationToken, int? maxBodyBytes = null, bool singleAttempt = false)
    {
        var gate = gates.For(uri);
        var origin = gates.OriginOf(uri);
        var isInternal = scope.IsInternal(uri);
        var retries = singleAttempt ? 0 : maxRetries;
        for (var attempt = 0; ; attempt++)
        {
            if (origin.IsUnreachable)
            {
                return NotChecked(uri);
            }

            FetchResponse response;
            using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                response = await fetcher.FetchAsync(uri, body, cancellationToken, maxBodyBytes).ConfigureAwait(false);
            }

            var answered = Report(gate, response);
            if (!answered && isInternal && _siteGate.IsUnreachable)
            {
                gate.Pause(UNREACHABLE_PAUSE);
            }

            if (attempt >= retries || !IsRetryable(response))
            {
                if (origin.ReportUrl(answered))
                {
                    await JudgeAsync(uri, gate, origin, response, cancellationToken).ConfigureAwait(false);
                }

                return response;
            }

            await Task.Delay(RetryDelay(attempt, response.RetryAfter), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> URLs in a row on <paramref name="uri"/>'s origin got no response,
    /// the last of them <paramref name="last"/>. Works out whether the origin, or the whole site, has stopped answering.
    /// </summary>
    private async Task JudgeAsync(Uri uri, HostGate gate, OriginReachability origin, FetchResponse last, CancellationToken cancellationToken)
    {
        if (!scope.IsInternal(uri))
        {
            origin.MarkUnreachable();
            return;
        }

        if (await AnswersAsync(uri, gate, cancellationToken).ConfigureAwait(false))
        {
            origin.Reset();
            return;
        }

        if (origin != _startOrigin && await AnswersAsync(scope.StartOrigin, _siteGate, cancellationToken).ConfigureAwait(false))
        {
            origin.MarkUnreachable();
            return;
        }

        // Nothing answers. Counting starts again, so an origin that tripped while the site was down is judged afresh later.
        origin.Reset();
        gate.Pause(UNREACHABLE_PAUSE);
        _siteGate.Pause(UNREACHABLE_PAUSE);
        if (_siteGate.ReportUnreachable())
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "site-unreachable",
                $"{scope.StartOrigin.Host} stopped answering: {HostGate.UNREACHABLE_AFTER_URLS} URLs in a row got no response ({last.Error}), and neither did its robots.txt. The crawler waits {UNREACHABLE_PAUSE.TotalMinutes:0} minute between tries until it answers again, and retries the URLs that failed at the end of the crawl. If it doesn't answer for {GIVE_UP_AFTER.TotalMinutes:0} minutes, the crawl stops, to be resumed once the site is back."));
        }
    }

    /// <summary>Whether the origin of <paramref name="uri"/> answers at all: one request for its robots.txt, headers only.</summary>
    private async Task<bool> AnswersAsync(Uri uri, HostGate gate, CancellationToken cancellationToken)
    {
        FetchResponse response;
        using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            response = await fetcher.FetchAsync(new Uri(uri, "/robots.txt"), FetchBody.None, cancellationToken).ConfigureAwait(false);
        }

        return Report(gate, response);
    }

    /// <summary>Tells the gate how the host answered. Returns whether it answered at all, which ends the site being down.</summary>
    private static bool Report(HostGate gate, FetchResponse response)
    {
        gate.Report(SignalFor(response), response.RetryAfter);
        if (response.StatusCode is null)
        {
            return false;
        }

        gate.ReportAnswered();
        return true;
    }

    /// <summary>A URL on an origin that has stopped answering, failed without a request.</summary>
    private static FetchResponse NotChecked(Uri uri) => new()
    {
        RequestUri = uri,
        Outcome = FetchOutcome.NetworkError,
        Error = $"{NOT_CHECKED_ERROR}: {uri.GetLeftPart(UriPartial.Authority)} stopped answering ({HostGate.UNREACHABLE_AFTER_URLS} URLs in a row got no response)"
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
