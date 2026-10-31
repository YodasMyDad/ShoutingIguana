using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// One run of a crawl. A dispatcher claims batches of queued URLs from the writer and hands them to two
/// worker pools: one for the site itself, one for status checks on other sites. Workers fetch and parse,
/// then send records back to the writer. The site's URLs that got no response (or met an unreadable
/// robots.txt) are held back and tried once more when nothing else can be claimed. That comes before the URLs only
/// sitemaps list: the writer hands those out once no linked URL is in flight, and a held-back URL still is. The crawl
/// is complete when a claim finds nothing queued and nothing in flight after sitemap discovery has finished. Stopping
/// leaves the database resumable; held-back URLs are claimed but unrecorded, so a resume fetches them again.
/// </summary>
internal sealed class CrawlSession
{
    private const int EXTERNAL_CONCURRENCY_PER_HOST = 2;
    private const int RATE_WINDOW_SECONDS = 5;

    /// <summary>At most this many URLs are held back for a second try, so memory stays flat when a site goes down.</summary>
    private const int MAX_DEFERRED = 10_000;

    private static readonly TimeSpan IDLE_DELAY = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan PROGRESS_INTERVAL = TimeSpan.FromMilliseconds(500);

    private readonly CrawlRequest _request;
    private readonly CrawlOptions _options;
    private readonly Uri _startUri;
    private readonly ICrawlObserver _observer;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CrawlCounters _counters = new();
    private readonly PauseGate _pause = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _elapsed = new();
    private readonly CrawlWriter _writer;
    private readonly HttpFetcher _fetcher;
    private readonly OutLinkFactory _links;
    private readonly PageProcessor _processor;
    private readonly RobotsCache _robots;
    private readonly SitemapDiscoverer _sitemaps;
    private readonly HostGateRegistry _gates;
    private readonly Channel<FrontierItem> _internalQueue;
    private readonly Channel<FrontierItem> _externalQueue;

    // Held back with how they ended, which is what gets recorded if their origin is written off before their one more try.
    private readonly ConcurrentQueue<PageRecord> _deferred = new();
    private readonly ConcurrentDictionary<int, PageRecord> _retried = new();
    private readonly Lock _pauseSync = new();
    private volatile CrawlStatus _status = CrawlStatus.Running;
    private double _pagesPerSecond;
    private int _deferredTotal;
    private DateTimeOffset? _pausedAt;

    // Held back since the last retry pass began because robots.txt couldn't be read.
    private int _refusedByRobots;

    private CrawlSession(CrawlRequest request, ICrawlObserver observer, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        _request = request;
        _options = request.Options;
        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<CrawlSession>();
        _observer = new SafeCrawlObserver(observer, _logger);

        UrlNormalizer.TryNormalize(_options.StartUrl, out var startUrl);
        _startUri = new Uri(startUrl);
        var scope = new CrawlScope(_startUri, _options.IncludeSubdomains, _options.StayInStartFolder);
        var userAgent = UserAgents.Resolve(_options.UserAgent, _options.CustomUserAgent);
        var interval = _options.MaxRequestsPerSecond > 0 ? TimeSpan.FromSeconds(1 / _options.MaxRequestsPerSecond) : TimeSpan.Zero;
        _gates = new HostGateRegistry(scope, isInternal => isInternal
            ? new HostGate(_options.Concurrency, interval, timeProvider)
            : new HostGate(EXTERNAL_CONCURRENCY_PER_HOST, TimeSpan.Zero, timeProvider));

        _writer = CrawlWriter.Open(request.DatabasePath, _options, request.Resume, _counters, loggerFactory.CreateLogger<CrawlWriter>());
        _fetcher = new HttpFetcher(_options, userAgent, scope, timeProvider);
        var fetcher = new RetryingFetcher(_fetcher, _gates, scope, _options.MaxRetries, _observer, timeProvider);
        _robots = new RobotsCache(fetcher, _gates, _writer, scope, _options, userAgent.RobotsToken, _observer,
            timeProvider, loggerFactory.CreateLogger<RobotsCache>(), _stop.Token);
        _links = new OutLinkFactory(scope);
        _processor = new PageProcessor(_options, userAgent, fetcher, _robots, _links, _observer, timeProvider);
        _sitemaps = new SitemapDiscoverer(_options, _fetcher, _gates, _robots, _writer, _links, _observer, _pause, timeProvider,
            loggerFactory.CreateLogger<SitemapDiscoverer>());
        _internalQueue = Channel.CreateBounded<FrontierItem>(new BoundedChannelOptions(_options.Concurrency * 2) { SingleWriter = true });
        _externalQueue = Channel.CreateBounded<FrontierItem>(new BoundedChannelOptions(_options.ExternalConcurrency * 2) { SingleWriter = true });
    }

    public bool IsPaused => _pause.IsPaused;

    /// <summary>Validates the request and opens (or resumes) the crawl database.</summary>
    public static CrawlSession Open(CrawlRequest request, ICrawlObserver observer, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var errors = request.Options.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(request));
        }

        return new CrawlSession(request, observer, loggerFactory, timeProvider);
    }

    public void Pause()
    {
        lock (_pauseSync)
        {
            _pausedAt ??= _timeProvider.GetUtcNow();
            _pause.Pause();
        }
    }

    /// <summary>Time spent paused doesn't count as a site not answering: nothing was asked of it.</summary>
    public void Resume()
    {
        lock (_pauseSync)
        {
            if (_pausedAt is { } pausedAt)
            {
                _gates.ExcludePause(pausedAt, _timeProvider.GetUtcNow());
                _pausedAt = null;
            }

            _pause.Resume();
        }
    }

    public void RequestStop()
    {
        _stop.Cancel();
        _pause.Resume();
    }

    public CrawlProgress Snapshot()
    {
        var status = _status;
        return new CrawlProgress(
            status == CrawlStatus.Running && _pause.IsPaused ? CrawlStatus.Paused : status,
            _elapsed.Elapsed,
            _counters.Discovered,
            _counters.Queued,
            _counters.InFlight,
            _counters.Fetched,
            _counters.HtmlPages,
            _counters.Redirects,
            _counters.Errors,
            _counters.Blocked,
            _counters.RobotsDisallowed,
            _counters.Skipped,
            _counters.Bytes,
            Volatile.Read(ref _pagesPerSecond));
    }

    public async Task<CrawlSummary> RunAsync()
    {
        _elapsed.Start();
        using var progressStop = new CancellationTokenSource();
        var progress = ReportProgressAsync(progressStop.Token);
        var sitemaps = Task.CompletedTask;
        Task[] workers = [];
        string? error = null;

        try
        {
            // Opening the writer has already marked the crawl running.
            await SeedAsync(_stop.Token).ConfigureAwait(false);
            sitemaps = DiscoverSitemapsAsync(_stop.Token);
            workers =
            [
                .. StartWorkers(_internalQueue.Reader, _options.Concurrency),
                .. StartWorkers(_externalQueue.Reader, _options.ExternalConcurrency)
            ];

            var exhausted = await DispatchAsync(sitemaps, _stop.Token).ConfigureAwait(false);
            _status = exhausted ? CrawlStatus.Completed : CrawlStatus.Stopped;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            _status = _writer.Completion.IsFaulted ? CrawlStatus.Failed : CrawlStatus.Stopped;
        }
        catch (Exception exception)
        {
            _status = CrawlStatus.Failed;
            error = exception.Message;
            _logger.LogError(exception, "Crawl failed");
        }
        finally
        {
            if (_status == CrawlStatus.Failed)
            {
                await _stop.CancelAsync().ConfigureAwait(false);
            }

            _internalQueue.Writer.TryComplete();
            _externalQueue.Writer.TryComplete();
            await WhenAllQuietlyAsync([.. workers, sitemaps]).ConfigureAwait(false);
            await progressStop.CancelAsync().ConfigureAwait(false);
            await WhenAllQuietlyAsync([progress]).ConfigureAwait(false);
            _elapsed.Stop();
            await CloseAsync().ConfigureAwait(false);
        }

        if (_status == CrawlStatus.Failed)
        {
            error = _writer.Completion.Exception?.GetBaseException().Message ?? error;
        }

        var final = Snapshot();
        _observer.OnProgress(final);
        _logger.LogInformation("Crawl {Status}: {Fetched} URLs fetched in {Elapsed}", _status, final.Fetched, final.Elapsed);
        return new CrawlSummary(_status, final, _request.DatabasePath, error);
    }

    /// <summary>
    /// Stores the final status, closes the crawl file and lets go of the network. A write loop that failed, even on this
    /// last drain, fails the crawl: the writer marks the file failed, and the summary says so too.
    /// </summary>
    private async Task CloseAsync()
    {
        try
        {
            if (!_writer.Completion.IsCompleted)
            {
                await _writer.UpdateStatusAsync(_status, _status == CrawlStatus.Completed, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException)
        {
            // The write loop failed after the check above; its error is reported below.
        }
        finally
        {
            try
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _fetcher.Dispose();
            }
        }

        if (_writer.Completion.IsFaulted)
        {
            _status = CrawlStatus.Failed;
        }
    }

    private ValueTask SeedAsync(CancellationToken cancellationToken)
    {
        var seed = _links.Create(_startUri, LinkKind.Hyperlink, LinkFlags.None, LinkPosition.Content, null);
        return _writer.DiscoverAsync([seed], DiscoverySource.Seed, null, cancellationToken);
    }

    private async Task DiscoverSitemapsAsync(CancellationToken cancellationToken)
    {
        if (!_options.DiscoverSitemaps || _writer.SitemapsDone)
        {
            return;
        }

        try
        {
            await _sitemaps.RunAsync(_startUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ChannelClosedException))
        {
            _logger.LogWarning(exception, "Sitemap discovery failed");
            _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Warning, "sitemaps-failed", $"Sitemap discovery failed: {exception.Message}"));
        }
    }

    /// <summary>Returns true when the frontier was exhausted, false if the crawl was stopped first.</summary>
    private async Task<bool> DispatchAsync(Task sitemaps, CancellationToken cancellationToken)
    {
        var internalCapacity = _options.Concurrency * 2;
        var externalCapacity = _options.ExternalConcurrency * 2;
        var retrying = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            await _pause.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (await GiveUpOnADeadSiteAsync().ConfigureAwait(false))
            {
                return false;
            }

            // Held-back URLs are handed out as the queue has room, so the checks above still run while a site that is
            // down lets one through a minute.
            if (retrying)
            {
                retrying = FeedDeferred();
            }

            var wantInternal = internalCapacity - _internalQueue.Reader.Count;
            var wantExternal = externalCapacity - _externalQueue.Reader.Count;
            if (wantInternal <= 0 && wantExternal <= 0)
            {
                await Task.Delay(IDLE_DELAY, _timeProvider, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Read before claiming: sitemap batches are queued to the writer before the task completes,
            // so a claim issued after this point is guaranteed to see every sitemap URL.
            var producersDone = sitemaps.IsCompleted;
            var claim = await _writer.ClaimAsync(Math.Max(0, wantInternal), Math.Max(0, wantExternal), cancellationToken).ConfigureAwait(false);

            foreach (var item in claim.Internal)
            {
                await _internalQueue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }

            foreach (var item in claim.External)
            {
                await _externalQueue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }

            if (claim.Internal.Count + claim.External.Count == 0)
            {
                // Read after claiming: workers only ever add to the held-back URLs, so if everything in flight at the
                // claim is held back now, nothing else is still being fetched.
                var deferred = _deferred.Count;
                if (claim.InFlight == deferred && producersDone && !retrying)
                {
                    if (deferred == 0)
                    {
                        return true;
                    }

                    if (!IsRobotsTxtCoolingDown())
                    {
                        _logger.LogInformation("Retrying {Count} URLs that got no response", deferred);
                        Interlocked.Exchange(ref _refusedByRobots, 0);
                        retrying = FeedDeferred();
                        continue;
                    }
                }

                await Task.Delay(IDLE_DELAY, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <summary>
    /// An origin of the site hasn't answered for <see cref="RetryingFetcher.GIVE_UP_AFTER"/>, paused time aside: stop
    /// rather than wait on it for good. Whatever wasn't recorded yet stays queued, so resuming once the site is back
    /// carries on where this left off.
    /// </summary>
    private async ValueTask<bool> GiveUpOnADeadSiteAsync()
    {
        if (_gates.LongestOutage(_timeProvider.GetUtcNow()) is not { } outage || outage.Duration < RetryingFetcher.GIVE_UP_AFTER)
        {
            return false;
        }

        _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Error, "site-down",
            $"{outage.Origin} hasn't answered for {outage.Duration.TotalMinutes:0} minutes, so the crawl stopped. Resume it once the site is back."));
        await _stop.CancelAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Nothing else can be claimed, so the URLs held back get their one more try: as many as the site's queue has room
    /// for. Returns true while some are still waiting their turn.
    /// </summary>
    private bool FeedDeferred()
    {
        while (_deferred.TryPeek(out var heldBack))
        {
            _retried.TryAdd(heldBack.Item.Id, heldBack);
            if (!_internalQueue.Writer.TryWrite(heldBack.Item))
            {
                return true;
            }

            _deferred.TryDequeue(out _);
        }

        return false;
    }

    /// <summary>
    /// A held-back URL was refused because robots.txt couldn't be read, and that robots.txt may not be asked again yet. Its
    /// one more try waits for that, as the notice promised, rather than meet the same failure a moment later.
    /// </summary>
    private bool IsRobotsTxtCoolingDown() => Volatile.Read(ref _refusedByRobots) > 0 && _robots.CoolDownEndsAt() is not null;

    /// <summary>
    /// The site's own URLs that got no response, or were refused because robots.txt couldn't be read, are held back
    /// (unrecorded) for one more try at the end of the crawl, when a site that was briefly down is usually back. URLs
    /// failed without a request ("Not checked") aren't: their origin is gone, and one more try would fail the same way.
    /// </summary>
    private bool Defer(PageRecord record)
    {
        var item = record.Item;
        var response = record.Response;
        var refusedByRobots = response.Outcome == FetchOutcome.RobotsDisallowed &&
                              response.Error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true;
        var gotNoResponse = response.Outcome is FetchOutcome.Timeout or FetchOutcome.NetworkError &&
                            response.Error?.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) != true;
        if (!item.IsInternal || !(gotNoResponse || refusedByRobots) || _retried.ContainsKey(item.Id) ||
            Interlocked.Increment(ref _deferredTotal) > MAX_DEFERRED)
        {
            return false;
        }

        // Counted first: once the URL is in the queue, the dispatcher may start the retry pass and read the count.
        if (refusedByRobots)
        {
            Interlocked.Increment(ref _refusedByRobots);
        }

        _deferred.Enqueue(record);
        return true;
    }

    /// <summary>
    /// A held-back URL whose origin was written off before its one more try comes back "Not checked". It was asked for,
    /// though, so it keeps how it ended then: its real error is what makes a link to it broken.
    /// </summary>
    private static PageRecord KeepTheRealError(PageRecord retried, PageRecord heldBack) =>
        retried.Response.Error?.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) == true ? heldBack : retried;

    private Task[] StartWorkers(ChannelReader<FrontierItem> queue, int count) =>
        Enumerable.Range(0, count).Select(_ => Task.Run(() => WorkAsync(queue, _stop.Token))).ToArray();

    private async Task WorkAsync(ChannelReader<FrontierItem> queue, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _pause.WaitAsync(cancellationToken).ConfigureAwait(false);

                PageRecord record;
                var isRetry = _retried.TryGetValue(item.Id, out var heldBack);
                try
                {
                    record = await _processor.ProcessAsync(item, cancellationToken, isRetry).ConfigureAwait(false);
                    record = heldBack is null ? record : KeepTheRealError(record, heldBack);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One unprocessable page must not end a crawl of thousands; it is recorded and logged.
                    _logger.LogError(exception, "Processing failed for {Url}", item.Url);
                    record = PageProcessor.Failed(item, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                        FetchOutcome.ProcessingError, exception.Message);
                }

                if (!Defer(record))
                {
                    await _writer.RecordAsync(record, cancellationToken).ConfigureAwait(false);
                }

                if (isRetry)
                {
                    // Its one more try is over, so its held-back record isn't needed any more.
                    _retried.TryRemove(item.Id, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping: anything claimed but unrecorded is re-queued when the crawl resumes.
        }
        catch (ChannelClosedException)
        {
            // The writer failed. Stop everything; the session reports the writer's error.
            await _stop.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task ReportProgressAsync(CancellationToken cancellationToken)
    {
        var samples = new Queue<(TimeSpan At, long Fetched)>();
        using var timer = new PeriodicTimer(PROGRESS_INTERVAL, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = _elapsed.Elapsed;
                var fetched = _counters.Fetched;
                samples.Enqueue((now, fetched));
                while (samples.Count > 1 && now - samples.Peek().At > TimeSpan.FromSeconds(RATE_WINDOW_SECONDS))
                {
                    samples.Dequeue();
                }

                var (oldestAt, oldestFetched) = samples.Peek();
                var seconds = (now - oldestAt).TotalSeconds;
                Volatile.Write(ref _pagesPerSecond, seconds > 0 ? (fetched - oldestFetched) / seconds : 0);
                _observer.OnProgress(Snapshot());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown of the progress loop.
        }
    }

    private async Task WhenAllQuietlyAsync(Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException)
        {
            // Expected while stopping.
        }
        catch (Exception exception)
        {
            _status = CrawlStatus.Failed;
            _logger.LogError(exception, "A crawl task failed");
        }
    }
}
