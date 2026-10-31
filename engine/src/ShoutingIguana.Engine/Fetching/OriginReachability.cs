namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Whether one origin (scheme, host and port) answers at all. It counts the URLs in a row that got no response; when
/// <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> have, <see cref="RetryingFetcher"/> works out what that means. An origin
/// that has never answered while the rest of the site does is gone (a dead www twin, a closed port) and is written off:
/// its URLs fail without a request. One that has answered before, or one on a site that doesn't answer at all, is down:
/// it is waited out until it answers again. Origins are counted apart from the <see cref="HostGate"/> they share.
/// </summary>
internal sealed class OriginReachability
{
    private readonly Lock _sync = new();
    private int _unansweredUrls;
    private bool _isUnreachable;
    private bool _hasAnswered;
    private DateTimeOffset? _downSince;

    /// <summary>
    /// Written off: its URLs fail at once ("Not checked") for the rest of the crawl. Only a response to one of its URLs
    /// already in flight when it was written off can end it.
    /// </summary>
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

    /// <summary>It has answered at least once, so it is no dead twin or closed port: if it stops, it is waited out.</summary>
    public bool HasAnswered
    {
        get
        {
            lock (_sync)
            {
                return _hasAnswered;
            }
        }
    }

    /// <summary>Waited out: every unanswered try holds its gate back, until it answers again or the crawl gives up.</summary>
    public bool IsDown
    {
        get
        {
            lock (_sync)
            {
                return _downSince is not null;
            }
        }
    }

    /// <summary>How long it has been <see cref="IsDown"/>, paused time aside; zero while it isn't.</summary>
    public TimeSpan DownFor(DateTimeOffset now)
    {
        lock (_sync)
        {
            return _downSince is { } since ? now - since : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Records how a URL ended once its retries were spent. Any response, even an error status, resets the count and ends
    /// <see cref="IsUnreachable"/>. Returns true for the URL that makes <see cref="HostGate.UNREACHABLE_AFTER_URLS"/> in a
    /// row, which calls for a verdict: <see cref="MarkUnreachable"/>, <see cref="MarkDown"/> or <see cref="Reset"/>.
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

    /// <summary>A response to any request here, robots.txt included. Returns true when it ends <see cref="IsDown"/>.</summary>
    public bool ReportAnswer()
    {
        lock (_sync)
        {
            _hasAnswered = true;
            var wasDown = _downSince is not null;
            _downSince = null;
            return wasDown;
        }
    }

    /// <summary>The origin is gone while the rest of the site answers. Unless one of its URLs got a response meanwhile.</summary>
    public void MarkUnreachable()
    {
        lock (_sync)
        {
            _isUnreachable = _unansweredUrls >= HostGate.UNREACHABLE_AFTER_URLS;
        }
    }

    /// <summary>
    /// The origin is waited out from <paramref name="now"/>. Returns true when it wasn't down already. Counting starts again,
    /// so it is judged afresh when more of its URLs go unanswered.
    /// </summary>
    public bool MarkDown(DateTimeOffset now)
    {
        lock (_sync)
        {
            _unansweredUrls = 0;
            if (_downSince is not null)
            {
                return false;
            }

            _downSince = now;
            return true;
        }
    }

    /// <summary>
    /// It was down only because nothing on the site answered, and something does now: having never answered itself, it is
    /// gone after all, and written off. Returns true when it was.
    /// </summary>
    public bool WriteOffIfNeverAnswered()
    {
        lock (_sync)
        {
            if (_downSince is null || _hasAnswered)
            {
                return false;
            }

            _downSince = null;
            _isUnreachable = true;
            return true;
        }
    }

    /// <summary>The crawl was paused from <paramref name="from"/> to <paramref name="to"/>: that time isn't time down.</summary>
    public void ExcludePause(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_sync)
        {
            if (_downSince is { } since)
            {
                _downSince = since < from ? since + (to - from) : to;
            }
        }
    }

    /// <summary>The URLs failed for reasons of their own: count afresh.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _unansweredUrls = 0;
        }
    }
}
