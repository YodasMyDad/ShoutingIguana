using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>A held request slot on a <see cref="HostGate"/>, and when its request started. Dispose to release it.</summary>
internal readonly struct HostGateLease(HostGate gate, DateTimeOffset startedAt) : IDisposable
{
    /// <summary>When the gate let the request go: its answer is judged against the pace it was sent at.</summary>
    public DateTimeOffset StartedAt => startedAt;

    /// <summary>
    /// How the host answered a request made beside the fetcher (a sitemap, the site's icon), by its status alone: so the
    /// pace it learns comes from every request to the host.
    /// </summary>
    public void ReportStatus(int statusCode, TimeSpan? retryAfter) => gate.Report(statusCode switch
    {
        429 => HostSignal.Throttled,
        503 when retryAfter is not null => HostSignal.Throttled,
        502 or 503 or 504 => HostSignal.Failed,
        _ => HostSignal.Success
    }, retryAfter, startedAt);

    public void Dispose() => gate.Release();
}
