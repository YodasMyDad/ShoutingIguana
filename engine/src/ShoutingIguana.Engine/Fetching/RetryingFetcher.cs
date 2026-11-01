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
/// the site being crawled, the origin's robots.txt is asked first (headers only): an answer means the URLs failed for
/// reasons of their own. An origin that has never answered, while the origin of the site that answered last still does,
/// is gone (a dead www twin, a closed http port, a dead subdomain) and fails fast like another site. Otherwise the
/// origin is down, and it is waited out, never written off: its gate pauses for <see cref="UNREACHABLE_PAUSE"/> after
/// every unanswered attempt on it until it answers again, and the crawl gives up on it after <see cref="GIVE_UP_AFTER"/>.
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

    /// <summary>How long an origin's gate holds every request after an unanswered attempt on it while it is down.</summary>
    internal static readonly TimeSpan UNREACHABLE_PAUSE = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long an origin of the site may stay down, not counting time the crawl is paused, before the crawl stops,
    /// resumable, instead of waiting on.
    /// </summary>
    internal static readonly TimeSpan GIVE_UP_AFTER = TimeSpan.FromMinutes(10);

    private readonly Random _random = random ?? Random.Shared;

    // A URL on the site's origin that answered last: it stands for the site when an origin that never answered goes
    // quiet. Before anything has answered, the start URL does.
    private Uri _lastAnswered = scope.StartOrigin;

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

            var answered = Report(uri, gate, origin, isInternal, response);
            if (!answered && origin.IsDown)
            {
                gate.Pause(UNREACHABLE_PAUSE);
            }

            if (attempt >= retries || !IsRetryable(response))
            {
                if (origin.ReportUrl(answered))
                {
                    await JudgeAsync(uri, gate, origin, isInternal, response, cancellationToken).ConfigureAwait(false);
                }

                return response;
            }

            await Task.Delay(RetryDelay(attempt, response.RetryAfter), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> URLs in a row on <paramref name="uri"/>'s origin got no response,
    /// the last of them <paramref name="last"/>. Works out whether the origin is gone, down, or fine.
    /// </summary>
    private async Task JudgeAsync(Uri uri, HostGate gate, OriginReachability origin, bool isInternal, FetchResponse last, CancellationToken cancellationToken)
    {
        if (!isInternal)
        {
            origin.MarkUnreachable();
            return;
        }

        if (await AnswersAsync(uri, gate, origin, cancellationToken).ConfigureAwait(false))
        {
            origin.Reset();
            return;
        }

        // Never answered, while the site does: a dead www twin, a closed port, a dead subdomain.
        var lastAnswered = Volatile.Read(ref _lastAnswered);
        var lastAnsweredOrigin = gates.OriginOf(lastAnswered);
        if (!origin.HasAnswered && lastAnsweredOrigin != origin &&
            await AnswersAsync(lastAnswered, gates.For(lastAnswered), lastAnsweredOrigin, cancellationToken).ConfigureAwait(false))
        {
            origin.MarkUnreachable();
            return;
        }

        // It answered before, so it is having an outage; or nothing on the site answers. Either way it is waited out.
        gate.Pause(UNREACHABLE_PAUSE);
        if (gates.ReportDown(origin, uri, timeProvider.GetUtcNow()))
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "site-unreachable",
                $"{uri.GetLeftPart(UriPartial.Authority)} stopped answering: {HostGate.UNREACHABLE_AFTER_URLS} URLs in a row got no response ({last.Error}), and neither did its robots.txt. The crawler waits {UNREACHABLE_PAUSE.TotalMinutes:0} minute between tries until it answers again, and retries the URLs that failed at the end of the crawl. If it doesn't answer for {GIVE_UP_AFTER.TotalMinutes:0} minutes, the crawl stops, to be resumed once the site is back.")
            {
                Data = new CrawlNoticeData(Origin: uri.GetLeftPart(UriPartial.Authority), Reason: last.Error, Duration: UNREACHABLE_PAUSE)
            });
        }
    }

    /// <summary>Whether the origin of <paramref name="uri"/> answers at all: one request for its robots.txt, headers only.</summary>
    private async Task<bool> AnswersAsync(Uri uri, HostGate gate, OriginReachability origin, CancellationToken cancellationToken)
    {
        var robots = new Uri(uri, "/robots.txt");
        FetchResponse response;
        using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            response = await fetcher.FetchAsync(robots, FetchBody.None, cancellationToken).ConfigureAwait(false);
        }

        return Report(robots, gate, origin, isInternal: true, response);
    }

    /// <summary>Tells the gate how the host answered, and the registry whether it answered at all. Returns the latter.</summary>
    private bool Report(Uri uri, HostGate gate, OriginReachability origin, bool isInternal, FetchResponse response)
    {
        gate.Report(SignalFor(response), response.RetryAfter);
        if (response.StatusCode is null)
        {
            return false;
        }

        gates.ReportAnswer(origin, isInternal);
        if (isInternal)
        {
            Volatile.Write(ref _lastAnswered, uri);
        }

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
