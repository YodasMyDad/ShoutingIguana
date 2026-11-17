using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>Keeps <see cref="SyntheticRateLimit"/> for the server, on the real clock: the crawler under test runs on it too.</summary>
internal sealed class SyntheticRateLimiter(SyntheticRateLimit limit, TimeProvider timeProvider)
{
    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Lock _sync = new();
    private DateTimeOffset _penaltyEnds = DateTimeOffset.MinValue;
    private long _limited;

    /// <summary>The 429s it has answered with.</summary>
    public long Limited => Interlocked.Read(ref _limited);

    /// <summary>The Retry-After header's value, in whole seconds, at least one.</summary>
    public string RetryAfter { get; } = Math.Max(1, (int)Math.Ceiling(limit.Penalty.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether this request is over the limit: a product request while the penalty runs (renewing it), or one too many.</summary>
    public bool IsOverTheLimit(string path)
    {
        if (!path.StartsWith("/p/", StringComparison.Ordinal))
        {
            return false;
        }

        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            while (_recent.Count > 0 && now - _recent.Peek() >= limit.Window)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(now);
            if (limit.NeverRelents || now < _penaltyEnds || _recent.Count > limit.MaxRequests)
            {
                _penaltyEnds = now + limit.Penalty;
                _limited++;
                return true;
            }

            return false;
        }
    }
}
