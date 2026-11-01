using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Live crawl totals. Only the database writer changes them, so they always match what is stored.
/// Progress reporting reads them from other threads.
/// </summary>
internal sealed class CrawlCounters
{
    /// <summary>Status codes are three digits: one slot each, so counting one never allocates.</summary>
    private const int STATUS_CODE_SLOTS = 1_000;

    private readonly long[] _statusCodes = new long[STATUS_CODE_SLOTS];
    private long _discovered;
    private long _queued;
    private long _skipped;
    private long _inFlight;
    private long _fetched;
    private long _htmlPages;
    private long _redirects;
    private long _errors;
    private long _blocked;
    private long _robotsDisallowed;
    private long _bytes;
    private long _internalFound;
    private long _internalFetched;
    private long _status2xx;
    private long _status3xx;
    private long _status4xx;
    private long _status5xx;
    private long _otherStatus;
    private long _timeouts;
    private long _networkErrors;
    private long _notChecked;
    private long _processingErrors;

    public long Discovered => Interlocked.Read(ref _discovered);
    public long Queued => Interlocked.Read(ref _queued);
    public long Skipped => Interlocked.Read(ref _skipped);
    public long InFlight => Interlocked.Read(ref _inFlight);
    public long Fetched => Interlocked.Read(ref _fetched);
    public long HtmlPages => Interlocked.Read(ref _htmlPages);
    public long Redirects => Interlocked.Read(ref _redirects);
    public long Errors => Interlocked.Read(ref _errors);
    public long Blocked => Interlocked.Read(ref _blocked);
    public long RobotsDisallowed => Interlocked.Read(ref _robotsDisallowed);

    /// <summary>Bytes of the bodies the crawler read, as sent; sizes it was only told by Content-Length aren't counted.</summary>
    public long Bytes => Interlocked.Read(ref _bytes);

    /// <summary>The site's own URLs (internal, not probes of its other versions) queued, in flight or fetched.</summary>
    public long InternalFound => Interlocked.Read(ref _internalFound);

    /// <summary>The site's own URLs with a stored response.</summary>
    public long InternalFetched => Interlocked.Read(ref _internalFetched);

    public CrawlNoResponseCounts NoResponse => new(RobotsDisallowed, Interlocked.Read(ref _timeouts), Interlocked.Read(ref _networkErrors),
        Interlocked.Read(ref _notChecked), Interlocked.Read(ref _processingErrors));

    public CrawlStatusClasses StatusClasses
    {
        get
        {
            var noResponse = NoResponse;
            return new CrawlStatusClasses(Interlocked.Read(ref _status2xx), Interlocked.Read(ref _status3xx), Interlocked.Read(ref _status4xx),
                Interlocked.Read(ref _status5xx), Interlocked.Read(ref _otherStatus),
                noResponse.RobotsDisallowed + noResponse.Timeout + noResponse.NetworkError + noResponse.NotChecked + noResponse.ProcessingError,
                Blocked);
        }
    }

    /// <summary>The responses with a status that weren't blocked, by status code: only the codes seen.</summary>
    public IReadOnlyDictionary<int, long> StatusCodes()
    {
        Dictionary<int, long> codes = [];
        for (var code = 0; code < STATUS_CODE_SLOTS; code++)
        {
            if (Interlocked.Read(ref _statusCodes[code]) is > 0 and var count)
            {
                codes[code] = count;
            }
        }

        return codes;
    }

    /// <param name="isSiteUrl">One of the site's own URLs: internal, and not a probe of the site's other versions.</param>
    public void OnUrlAdded(bool queued, bool isSiteUrl = false)
    {
        Interlocked.Increment(ref _discovered);
        Interlocked.Increment(ref queued ? ref _queued : ref _skipped);
        if (queued && isSiteUrl)
        {
            Interlocked.Increment(ref _internalFound);
        }
    }

    public void OnUrlRequeued(bool isSiteUrl = false)
    {
        Interlocked.Decrement(ref _skipped);
        Interlocked.Increment(ref _queued);
        if (isSiteUrl)
        {
            Interlocked.Increment(ref _internalFound);
        }
    }

    public void OnClaimed(int count)
    {
        Interlocked.Add(ref _queued, -count);
        Interlocked.Add(ref _inFlight, count);
    }

    /// <summary>Queued URLs skipped all at once, when the URL limit is reached: the site's own, every one.</summary>
    public void OnUrlsSkipped(long count)
    {
        Interlocked.Add(ref _queued, -count);
        Interlocked.Add(ref _skipped, count);
        Interlocked.Add(ref _internalFound, -count);
    }

    /// <summary>
    /// A URL only a probe of the site's other versions had found turned out to be one of the site's own: found, unless it
    /// was skipped, and fetched if the probe's response is kept for it.
    /// </summary>
    public void OnAdopted(bool found, bool fetched)
    {
        Interlocked.Add(ref _internalFound, found ? 1 : 0);
        Interlocked.Add(ref _internalFetched, fetched ? 1 : 0);
    }

    /// <summary>A claimed URL went back on the queue without a response, to be fetched again in full.</summary>
    public void OnReturned()
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _queued);
    }

    /// <summary>A stored response was dropped so its URL can be fetched again in full, and the URL queued again.</summary>
    public void OnResponseRemoved(RecordedResponse response)
    {
        AddResponses(response.Outcome, response.StatusCode, -1, response.IsSitePage ? -1 : 0, -response.Bytes,
            response.IsSiteUrl ? -1 : 0, response.IsNotChecked ? -1 : 0);
        Interlocked.Increment(ref _queued);
    }

    public void OnRecorded(RecordedResponse response)
    {
        Interlocked.Decrement(ref _inFlight);
        AddResponses(response.Outcome, response.StatusCode, 1, response.IsSitePage ? 1 : 0, response.Bytes,
            response.IsSiteUrl ? 1 : 0, response.IsNotChecked ? 1 : 0);
    }

    /// <summary>Restores URL totals when a crawl resumes; response totals follow via <see cref="AddResponses"/>.</summary>
    public void RestoreUrls(long discovered, long queued, long skipped, long internalFound)
    {
        Interlocked.Exchange(ref _discovered, discovered);
        Interlocked.Exchange(ref _queued, queued);
        Interlocked.Exchange(ref _skipped, skipped);
        Interlocked.Exchange(ref _internalFound, internalFound);
    }

    /// <summary>
    /// Adds (or, with a negative <paramref name="count"/>, takes away) responses that answered alike: the same outcome and
    /// status code. The other counts say how many of them were parsed pages, the site's own URLs, and network errors that
    /// were never asked (Not checked), and the bytes of their bodies.
    /// </summary>
    public void AddResponses(FetchOutcome outcome, int? statusCode, long count, long parsedPages, long bytes, long siteUrls = 0, long notChecked = 0)
    {
        Interlocked.Add(ref _fetched, count);
        Interlocked.Add(ref _htmlPages, parsedPages);
        Interlocked.Add(ref _bytes, bytes);
        Interlocked.Add(ref _internalFetched, siteUrls);

        switch (outcome)
        {
            case FetchOutcome.Redirect:
                Interlocked.Add(ref _redirects, count);
                break;
            case FetchOutcome.Blocked:
                Interlocked.Add(ref _blocked, count);
                break;
            case FetchOutcome.RobotsDisallowed:
                Interlocked.Add(ref _robotsDisallowed, count);
                break;
            case FetchOutcome.ClientError or FetchOutcome.ServerError or FetchOutcome.Timeout
                or FetchOutcome.NetworkError or FetchOutcome.ProcessingError:
                Interlocked.Add(ref _errors, count);
                break;
        }

        // A block page's status is the firewall's, so a blocked URL is counted as blocked alone. RobotsDisallowed is
        // counted above.
        if (outcome == FetchOutcome.Blocked)
        {
            return;
        }

        if (statusCode is not { } code)
        {
            switch (outcome)
            {
                case FetchOutcome.RobotsDisallowed:
                    break;
                case FetchOutcome.Timeout:
                    Interlocked.Add(ref _timeouts, count);
                    break;
                case FetchOutcome.NetworkError:
                    Interlocked.Add(ref _networkErrors, count - notChecked);
                    Interlocked.Add(ref _notChecked, notChecked);
                    break;
                default:
                    // A processing error, or a status the file lost: either way, no status was recorded.
                    Interlocked.Add(ref _processingErrors, count);
                    break;
            }

            return;
        }

        Interlocked.Add(ref ClassOf(code), count);
        if (code is >= 0 and < STATUS_CODE_SLOTS)
        {
            Interlocked.Add(ref _statusCodes[code], count);
        }
    }

    private ref long ClassOf(int statusCode)
    {
        switch (statusCode)
        {
            case >= 200 and < 300:
                return ref _status2xx;
            case >= 300 and < 400:
                return ref _status3xx;
            case >= 400 and < 500:
                return ref _status4xx;
            case >= 500 and < 600:
                return ref _status5xx;
            default:
                return ref _otherStatus;
        }
    }
}
