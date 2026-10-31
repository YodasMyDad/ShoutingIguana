namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Whether one origin (scheme, host and port) answers at all. It counts the URLs in a row that got no response; when
/// <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> have, <see cref="RetryingFetcher"/> works out what that means, and an
/// origin it finds unreachable has the rest of its URLs failed without a request. Origins are counted apart from the
/// <see cref="HostGate"/> they share: a dead www twin or a closed http port says nothing about the rest of the site.
/// </summary>
internal sealed class OriginReachability
{
    private readonly Lock _sync = new();
    private int _unansweredUrls;
    private bool _isUnreachable;

    /// <summary>Found unreachable: its URLs fail at once ("Not checked") until one of them gets a response.</summary>
    public bool IsUnreachable
    {
        get
        {
            lock (_sync)
            {
                return _isUnreachable;
            }
        }
    }

    /// <summary>
    /// Records how a URL ended once its retries were spent. Any response, even an error status, resets the count and ends
    /// <see cref="IsUnreachable"/>. Returns true for the URL that makes <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> in a
    /// row, which calls for a verdict: <see cref="MarkUnreachable"/> or <see cref="Reset"/>.
    /// </summary>
    public bool ReportUrl(bool answered)
    {
        lock (_sync)
        {
            if (answered)
            {
                _unansweredUrls = 0;
                _isUnreachable = false;
                return false;
            }

            return ++_unansweredUrls == HostGate.UNREACHABLE_AFTER_URLS;
        }
    }

    /// <summary>The origin is gone while the rest of the web answers. Unless one of its URLs got a response meanwhile.</summary>
    public void MarkUnreachable()
    {
        lock (_sync)
        {
            _isUnreachable = _unansweredUrls >= HostGate.UNREACHABLE_AFTER_URLS;
        }
    }

    /// <summary>The URLs failed for reasons of their own, or the whole site is down and waited out: count afresh.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _unansweredUrls = 0;
        }
    }
}
