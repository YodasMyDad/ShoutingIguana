using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Live crawl totals. Only the database writer changes them, so they always match what is stored.
/// Progress reporting reads them from other threads.
/// </summary>
internal sealed class CrawlCounters
{
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
    public long Bytes => Interlocked.Read(ref _bytes);

    public void OnUrlAdded(bool queued)
    {
        Interlocked.Increment(ref _discovered);
        Interlocked.Increment(ref queued ? ref _queued : ref _skipped);
    }

    public void OnUrlRequeued()
    {
        Interlocked.Decrement(ref _skipped);
        Interlocked.Increment(ref _queued);
    }

    public void OnClaimed(int count)
    {
        Interlocked.Add(ref _queued, -count);
        Interlocked.Add(ref _inFlight, count);
    }

    /// <summary>Queued URLs skipped all at once, when the URL limit is reached.</summary>
    public void OnUrlsSkipped(long count)
    {
        Interlocked.Add(ref _queued, -count);
        Interlocked.Add(ref _skipped, count);
    }

    /// <summary>A claimed URL went back on the queue without a response, to be fetched again in full.</summary>
    public void OnReturned()
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _queued);
    }

    /// <summary>A stored response was dropped so its URL can be fetched again in full, and the URL queued again.</summary>
    public void OnResponseRemoved(FetchOutcome outcome, bool isSitePage, long bytes)
    {
        AddResponses(outcome, -1, isSitePage ? -1 : 0, -bytes);
        Interlocked.Increment(ref _queued);
    }

    /// <param name="isSitePage">A successful HTML page on the site being crawled (external pages are only checked).</param>
    public void OnRecorded(FetchOutcome outcome, bool isSitePage, long bytes)
    {
        Interlocked.Decrement(ref _inFlight);
        AddResponses(outcome, 1, isSitePage ? 1 : 0, bytes);
    }

    /// <summary>Restores URL totals when a crawl resumes; response totals follow via <see cref="AddResponses"/>.</summary>
    public void RestoreUrls(long discovered, long queued, long skipped)
    {
        Interlocked.Exchange(ref _discovered, discovered);
        Interlocked.Exchange(ref _queued, queued);
        Interlocked.Exchange(ref _skipped, skipped);
    }

    public void AddResponses(FetchOutcome outcome, long count, long parsedPages, long bytes)
    {
        Interlocked.Add(ref _fetched, count);
        Interlocked.Add(ref _htmlPages, parsedPages);
        Interlocked.Add(ref _bytes, bytes);

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
    }
}
