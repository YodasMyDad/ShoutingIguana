using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The polite way to fetch, shared by pages and robots.txt: through the host's gate, telling the gate how the host
/// answered, and retrying transient failures (no response, 429, 502, 503, 504) after an exponential backoff or the
/// server's Retry-After, capped at a minute.
/// </summary>
internal sealed class RetryingFetcher(HttpFetcher fetcher, HostGateRegistry gates, int maxRetries, TimeProvider timeProvider, Random? random = null)
{
    private const int MAX_JITTER_MS = 250;
    private static readonly TimeSpan MAX_RETRY_WAIT = TimeSpan.FromSeconds(60);

    private readonly Random _random = random ?? Random.Shared;

    public async Task<FetchResponse> FetchAsync(Uri uri, FetchBody body, CancellationToken cancellationToken, int? maxBodyBytes = null)
    {
        var gate = gates.For(uri);
        for (var attempt = 0; ; attempt++)
        {
            FetchResponse response;
            using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                response = await fetcher.FetchAsync(uri, body, cancellationToken, maxBodyBytes).ConfigureAwait(false);
            }

            gate.Report(SignalFor(response), response.RetryAfter);
            if (attempt >= maxRetries || !IsRetryable(response))
            {
                return response;
            }

            await Task.Delay(RetryDelay(attempt, response.RetryAfter), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

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
