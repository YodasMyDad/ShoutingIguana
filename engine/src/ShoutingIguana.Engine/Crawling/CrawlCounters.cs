using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Live crawl totals, kept by the database writer as it stores each response, so they always match what is stored. Only
/// the writer's thread changes them (and, as a crawl resumes, the thread opening the writer, before its loop starts), so
/// they are plain fields. Other threads read <see cref="Current"/>: the totals as the writer last published them, every
/// one from the same moment, so one progress report's numbers always agree with each other.
/// </summary>
internal sealed class CrawlCounters
{
    /// <summary>Status codes are three digits: one slot each, so counting one never allocates.</summary>
    private const int STATUS_CODE_SLOTS = 1_000;

    private readonly long[] _statusCodes = new long[STATUS_CODE_SLOTS];
    private CrawlCounts _current = CrawlCounts.None;
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

    /// <summary>The totals as the writer last published them, after the last batch it stored. Safe from any thread.</summary>
    public CrawlCounts Current => Volatile.Read(ref _current);

    /// <summary>
    /// Makes the totals as they stand now what <see cref="Current"/> gives. Called on the writer's thread once a batch is
    /// stored (and once a resumed crawl's totals are restored): one record a batch, never one a response.
    /// </summary>
    public void Publish()
    {
        var noResponse = new CrawlNoResponseCounts(_robotsDisallowed, _timeouts, _networkErrors, _notChecked, _processingErrors);
        var classes = new CrawlStatusClasses(_status2xx, _status3xx, _status4xx, _status5xx, _otherStatus,
            noResponse.RobotsDisallowed + noResponse.Timeout + noResponse.NetworkError + noResponse.NotChecked + noResponse.ProcessingError,
            _blocked);
        Dictionary<int, long> codes = [];
        for (var code = 0; code < STATUS_CODE_SLOTS; code++)
        {
            if (_statusCodes[code] > 0)
            {
                codes[code] = _statusCodes[code];
            }
        }

        Volatile.Write(ref _current, new CrawlCounts(_discovered, _queued, _skipped, _inFlight, _fetched, _htmlPages, _redirects, _errors,
            _blocked, _robotsDisallowed, _bytes, _internalFound, _internalFetched, classes, noResponse, codes));
    }

    /// <param name="isSiteUrl">One of the site's own URLs: internal, and not a probe of the site's other versions.</param>
    public void OnUrlAdded(bool queued, bool isSiteUrl = false)
    {
        _discovered++;
        if (!queued)
        {
            _skipped++;
            return;
        }

        _queued++;
        if (isSiteUrl)
        {
            _internalFound++;
        }
    }

    public void OnUrlRequeued(bool isSiteUrl = false)
    {
        _skipped--;
        _queued++;
        if (isSiteUrl)
        {
            _internalFound++;
        }
    }

    public void OnClaimed(int count)
    {
        _queued -= count;
        _inFlight += count;
    }

    /// <summary>Queued URLs skipped all at once, when the URL limit is reached: the site's own, every one.</summary>
    public void OnUrlsSkipped(long count)
    {
        _queued -= count;
        _skipped += count;
        _internalFound -= count;
    }

    /// <summary>
    /// A URL only a probe of the site's other versions had found turned out to be one of the site's own: found, unless it
    /// was skipped, and fetched if the probe's response is kept for it.
    /// </summary>
    public void OnAdopted(bool found, bool fetched)
    {
        _internalFound += found ? 1 : 0;
        _internalFetched += fetched ? 1 : 0;
    }

    /// <summary>A claimed URL went back on the queue without a response, to be fetched again in full.</summary>
    public void OnReturned()
    {
        _inFlight--;
        _queued++;
    }

    /// <summary>A stored response was dropped so its URL can be fetched again in full, and the URL queued again.</summary>
    public void OnResponseRemoved(RecordedResponse response)
    {
        AddResponses(response.Outcome, response.StatusCode, -1, response.IsSitePage ? -1 : 0, -response.Bytes,
            response.IsSiteUrl ? -1 : 0, response.IsNotChecked ? -1 : 0);
        _queued++;
    }

    public void OnRecorded(RecordedResponse response)
    {
        _inFlight--;
        AddResponses(response.Outcome, response.StatusCode, 1, response.IsSitePage ? 1 : 0, response.Bytes,
            response.IsSiteUrl ? 1 : 0, response.IsNotChecked ? 1 : 0);
    }

    /// <summary>Restores URL totals when a crawl resumes; response totals follow via <see cref="AddResponses"/>.</summary>
    public void RestoreUrls(long discovered, long queued, long skipped, long internalFound)
    {
        _discovered = discovered;
        _queued = queued;
        _skipped = skipped;
        _internalFound = internalFound;
    }

    /// <summary>
    /// Adds (or, with a negative <paramref name="count"/>, takes away) responses that answered alike: the same outcome and
    /// status code. The other counts say how many of them were parsed pages, the site's own URLs, and network errors that
    /// were never asked (Not checked), and the bytes of their bodies.
    /// </summary>
    public void AddResponses(FetchOutcome outcome, int? statusCode, long count, long parsedPages, long bytes, long siteUrls = 0, long notChecked = 0)
    {
        _fetched += count;
        _htmlPages += parsedPages;
        _bytes += bytes;
        _internalFetched += siteUrls;

        switch (outcome)
        {
            case FetchOutcome.Redirect:
                _redirects += count;
                break;
            case FetchOutcome.Blocked:
                _blocked += count;
                break;
            case FetchOutcome.RobotsDisallowed:
                _robotsDisallowed += count;
                break;
            case FetchOutcome.ClientError or FetchOutcome.ServerError or FetchOutcome.Timeout
                or FetchOutcome.NetworkError or FetchOutcome.ProcessingError:
                _errors += count;
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
                    _timeouts += count;
                    break;
                case FetchOutcome.NetworkError:
                    _networkErrors += count - notChecked;
                    _notChecked += notChecked;
                    break;
                default:
                    // A processing error, or a status the file lost: either way, no status was recorded.
                    _processingErrors += count;
                    break;
            }

            return;
        }

        ClassOf(code) += count;
        if (code is >= 0 and < STATUS_CODE_SLOTS)
        {
            _statusCodes[code] += count;
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
