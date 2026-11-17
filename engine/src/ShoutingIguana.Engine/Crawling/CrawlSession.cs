using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Favicons;
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
/// then send records back to the writer. The site's URLs that got no response, were asked to slow down (429, 503) or
/// met an unreadable robots.txt are held back and tried once more when nothing else can be claimed, at the pace the
/// host has taught and after any pause it asked for. That comes before the URLs only
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

    /// <summary>
    /// Tries at the end of the crawl for a URL the site keeps answering 429, each after the pause the host asked for: a
    /// limit that lets one page through a minute lets them all through one by one, where a single try lost them.
    /// </summary>
    private const int MAX_RATE_LIMITED_TRIES = 3;

    private static readonly TimeSpan IDLE_DELAY = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan PROGRESS_INTERVAL = TimeSpan.FromMilliseconds(500);

    /// <summary>A recheck is a few pages: a site that stops answering ends it after a minute, not a crawl's ten.</summary>
    internal static readonly TimeSpan RECHECK_GIVE_UP_AFTER = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the time spent crawling is stored while a stretch of it goes on, in progress reports: every five seconds,
    /// which is as much of it as a run cut short (the process died) can lose.
    /// </summary>
    private const int RUN_TIME_EVERY = 10;

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
    private readonly RunClock _crawling;
    private readonly CrawlWriter _writer;
    private readonly HttpFetcher _fetcher;
    private readonly OutLinkFactory _links;
    private readonly PageProcessor _processor;
    private readonly RobotsCache _robots;
    private readonly SitemapDiscoverer _sitemaps;
    private readonly FaviconFetcher _favicon;
    private readonly SectionTrickle _trickle;
    private readonly HostGateRegistry _gates;
    private readonly CrawlScope _scope;
    private readonly Channel<FrontierItem> _internalQueue;
    private readonly Channel<FrontierItem> _externalQueue;

    // A recheck's run (the fix loop): its plan, and how long a site that stops answering is waited for.
    private readonly RecheckPlan? _recheck;
    private readonly TimeSpan _giveUpAfter;

    // Held back with how they ended, which is what gets recorded if their origin is written off before their one more try.
    private readonly ConcurrentQueue<PageRecord> _deferred = new();
    private readonly ConcurrentDictionary<int, PageRecord> _retried = new();

    // The end-of-crawl tries each URL answered 429 has had.
    private readonly ConcurrentDictionary<int, int> _rateLimitedTries = new();
    private readonly Lock _pauseSync = new();
    private volatile CrawlStatus _status = CrawlStatus.Running;
    private volatile CrawlPhase _phase = CrawlPhase.Starting;
    private double _pagesPerSecond;
    private int _deferredTotal;
    private DateTimeOffset? _pausedAt;

    // Between the run's start and its end: only then does a pause end a stretch of crawling, and a resume begin one.
    private bool _isTimed;

    // Held back since the last retry pass began because robots.txt couldn't be read.
    private int _refusedByRobots;

    // The site's URLs stored as not checked because it kept asking the crawler to slow down, and the host of the first.
    private int _rateLimited;
    private string? _rateLimitedHost;

    private CrawlSession(CrawlRequest request, RecheckPlan? recheck, ICrawlObserver observer, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        _request = request;
        _recheck = recheck;
        _giveUpAfter = recheck is null ? RetryingFetcher.GIVE_UP_AFTER : RECHECK_GIVE_UP_AFTER;
        _options = request.Options;
        _timeProvider = timeProvider;
        _crawling = new RunClock(timeProvider);
        _logger = loggerFactory.CreateLogger<CrawlSession>();

        UrlNormalizer.TryNormalize(_options.StartUrl, out var startUrl);
        _startUri = new Uri(startUrl);
        var scope = new CrawlScope(_startUri, _options.IncludeSubdomains, _options.StayInStartFolder);
        _scope = scope;
        var userAgent = UserAgents.Resolve(_options.UserAgent, _options.CustomUserAgent);
        var interval = _options.MaxRequestsPerSecond > 0 ? TimeSpan.FromSeconds(1 / _options.MaxRequestsPerSecond) : TimeSpan.Zero;
        _gates = new HostGateRegistry(scope, isInternal => isInternal
            ? new HostGate(_options.Concurrency, interval, timeProvider)
            : new HostGate(EXTERNAL_CONCURRENCY_PER_HOST, TimeSpan.Zero, timeProvider));

        _writer = recheck is null
            ? CrawlWriter.Open(request.DatabasePath, _options, request.Resume, _counters, loggerFactory.CreateLogger<CrawlWriter>())
            : CrawlWriter.OpenForRecheck(request.DatabasePath, _options, recheck, _counters, loggerFactory.CreateLogger<CrawlWriter>());

        // Every notice is kept with the crawl, then handed on: the host hears of it with the id the file keeps it under.
        _observer = new NoticeRecorder(new SafeCrawlObserver(observer, _logger), _writer, timeProvider);
        _fetcher = new HttpFetcher(_options, userAgent, scope, timeProvider);
        var fetcher = new RetryingFetcher(_fetcher, _gates, scope, _options.MaxRetries, _observer, timeProvider);
        _robots = new RobotsCache(fetcher, _gates, _writer, scope, _options, userAgent.RobotsToken, _observer,
            timeProvider, loggerFactory.CreateLogger<RobotsCache>(), _stop.Token);
        _links = new OutLinkFactory(scope);
        _favicon = new FaviconFetcher(_options, _fetcher, _gates, _robots, _writer, scope, _pause, timeProvider, loggerFactory.CreateLogger<FaviconFetcher>(), _stop.Token);
        _processor = new PageProcessor(_options, userAgent, fetcher, _robots, _links, _observer, scope, _gates, _favicon, timeProvider);
        _trickle = new SectionTrickle(_gates, _processor, scope, _observer, _pause, timeProvider, loggerFactory.CreateLogger<SectionTrickle>());
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

        return new CrawlSession(request, null, observer, loggerFactory, timeProvider);
    }

    /// <summary>Validates the options and opens the crawl file for a recheck's run (the fix loop).</summary>
    public static CrawlSession OpenRecheck(RecheckRequest request, ICrawlObserver observer, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var errors = request.Options.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(request));
        }

        if (request.Plan.Refused is { } refused)
        {
            throw new InvalidOperationException(refused.Title);
        }

        return new CrawlSession(new CrawlRequest(request.Options, request.DatabasePath, Resume: true), request.Plan, observer, loggerFactory, timeProvider);
    }

    public void Pause()
    {
        lock (_pauseSync)
        {
            _pausedAt ??= _timeProvider.GetUtcNow();
            _pause.Pause();
            KeepRunTime(_crawling.End());
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
            if (_isTimed)
            {
                KeepRunTime(_crawling.Begin());
            }
        }
    }

    /// <summary>Has the writer keep a stretch of the run's crawling as it stands, if there is one.</summary>
    private void KeepRunTime(RunStretch? stretch)
    {
        if (stretch is { } known)
        {
            _writer.RecordRunTime(known);
        }
    }

    public void RequestStop()
    {
        _stop.Cancel();
        _pause.Resume();
    }

    /// <summary>
    /// The crawl's counts as they stand, what it is doing, and what holds it up: counters and a few gates, nothing that
    /// grows with the crawl.
    /// </summary>
    public CrawlProgress Snapshot() => Snapshot(_counters.Current);

    /// <summary>
    /// Every count comes from <paramref name="counts"/>, the one record the writer published last, so one report's numbers
    /// agree with each other; the workers' two counts come from one read too.
    /// </summary>
    private CrawlProgress Snapshot(CrawlCounts counts)
    {
        var status = _status;
        var phase = _phase;
        var work = _processor.Work;

        // Nothing is being fetched, and something waits for robots.txt: that is all the crawl is doing.
        if (phase == CrawlPhase.Crawling && work.WaitingForRobotsTxt > 0 && work.Fetching == 0)
        {
            phase = CrawlPhase.ReadingRobotsTxt;
        }

        // Once it is finishing, nothing holds the crawl up any more, whatever its hosts' gates still say.
        var (waits, moreWaits) = phase == CrawlPhase.Finishing ? CrawlWaits.Cap([]) : CrawlWaits.Cap(Waits(work));
        return new CrawlProgress(
            status == CrawlStatus.Running && _pause.IsPaused ? CrawlStatus.Paused : status,
            _elapsed.Elapsed,
            counts.Discovered,
            counts.Queued,
            counts.InFlight,
            counts.Fetched,
            counts.HtmlPages,
            counts.Redirects,
            counts.Errors,
            counts.Blocked,
            counts.RobotsDisallowed,
            counts.Skipped,
            counts.Bytes,
            Volatile.Read(ref _pagesPerSecond))
        {
            Phase = phase,
            StatusClasses = counts.StatusClasses,
            StatusCodes = counts.StatusCodes,
            NoResponse = counts.NoResponse,
            SiteVersions = counts.SiteVersions,
            TimeCrawling = _writer.TimeCrawledBefore is { } before ? before + TimeSpan.FromMilliseconds(_crawling.ElapsedMs) : null,
            InternalFound = counts.InternalFound,
            InternalFetched = counts.InternalFetched,
            MaxUrls = _options.MaxUrls > 0 ? _options.MaxUrls : null,
            Waits = waits,
            MoreWaits = moreWaits,
            Recheck = _writer.RecheckProgress
        };
    }

    /// <summary>
    /// What holds the crawl up now. The site's gates are a handful (hundreds at most, with every subdomain included, which
    /// <see cref="CrawlWaits.Cap"/> brings down to a few); the outages are read only while there are some, and robots.txt's
    /// cool-down, which looks at every origin's robots.txt, only while something waits for it.
    /// </summary>
    private List<CrawlWait> Waits(WorkInProgress work)
    {
        List<CrawlWait> waits = [];
        foreach (var (host, state) in _gates.ReadSiteStates())
        {
            if (state.WaitingForRobotsTxt > 0)
            {
                waits.Add(new CrawlWait(CrawlWaitReason.RobotsTxt, host, Urls: state.WaitingForRobotsTxt));
            }

            if (state.CrawlDelay is { } delay)
            {
                waits.Add(new CrawlWait(CrawlWaitReason.CrawlDelay, host, Duration: delay));
            }

            if (state.SlowedTo is { } slowedTo)
            {
                waits.Add(new CrawlWait(CrawlWaitReason.Backoff, host, Duration: slowedTo));
            }

            if (state.HeldFor > TimeSpan.Zero)
            {
                waits.Add(new CrawlWait(CrawlWaitReason.HeldBack, host, Duration: state.HeldFor));
            }
        }

        if (work.OthersWaitingForRobotsTxt > 0)
        {
            waits.Add(new CrawlWait(CrawlWaitReason.OtherSitesRobotsTxt, Urls: work.OthersWaitingForRobotsTxt));
        }

        var now = _timeProvider.GetUtcNow();
        if (_gates.LongestOutage(now) is { } outage)
        {
            waits.Add(new CrawlWait(CrawlWaitReason.SiteDown, outage.Origin, Duration: outage.Duration));
        }

        if (RobotsTxtCoolDown(now) is { } coolDown)
        {
            waits.Add(coolDown);
        }

        waits.AddRange(_trickle.Waits());
        return waits;
    }

    /// <summary>
    /// robots.txt's cool-down, in whatever phase, while something waits for it: URLs it refused, held back for a last try
    /// that comes after it, or the sitemap discovery, which reads the sitemaps robots.txt declares once it's asked again.
    /// </summary>
    private CrawlWait? RobotsTxtCoolDown(DateTimeOffset now)
    {
        var refused = Volatile.Read(ref _refusedByRobots);
        if (refused == 0 && !_robots.IsCoolDownWaitedOut)
        {
            return null;
        }

        return _robots.CoolDownEndsAt() is { } ends && ends > now
            ? new CrawlWait(CrawlWaitReason.RobotsTxtCoolDown, Urls: refused > 0 ? refused : null, Duration: ends - now)
            : null;
    }

    public async Task<CrawlSummary> RunAsync()
    {
        _elapsed.Start();
        lock (_pauseSync)
        {
            // The run's first stretch of crawling begins here, unless the crawl was paused before it began.
            _isTimed = true;
            if (!_pause.IsPaused)
            {
                KeepRunTime(_crawling.Begin());
            }
        }

        using var progressStop = new CancellationTokenSource();
        using var trickleStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var progress = ReportProgressAsync(progressStop.Token);
        var sitemaps = Task.CompletedTask;
        Task[] workers = [];
        string? error = null;

        try
        {
            // Opening the writer has already marked the crawl running; a recheck's has handed out its first phase.
            if (_recheck is null)
            {
                await SeedAsync(_stop.Token).ConfigureAwait(false);
                sitemaps = DiscoverSitemapsAsync(_stop.Token);
            }
            else
            {
                await ReadRobotsTxtAgainAsync(_recheck).ConfigureAwait(false);
                sitemaps = _recheck.Kinds.Contains(RecheckKind.Sitemaps) ? ReadSitemapsAgainAsync(_stop.Token) : Task.CompletedTask;
            }

            workers =
            [
                .. StartWorkers(_internalQueue.Reader, _options.Concurrency),
                .. StartWorkers(_externalQueue.Reader, _options.ExternalConcurrency),
                Task.Run(() => TrickleAsync(trickleStop.Token))
            ];

            var exhausted = await DispatchAsync(sitemaps, _stop.Token).ConfigureAwait(false);

            // A recheck's second phase, the pages referring to what still fails, once its first is done.
            if (exhausted && _recheck is not null && await _writer.NextRecheckPhaseAsync(_stop.Token).ConfigureAwait(false) > 0)
            {
                exhausted = await DispatchAsync(sitemaps, _stop.Token).ConfigureAwait(false);
            }

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
            _phase = CrawlPhase.Finishing;
            if (_status == CrawlStatus.Failed)
            {
                await _stop.CancelAsync().ConfigureAwait(false);
            }

            _internalQueue.Writer.TryComplete();
            _externalQueue.Writer.TryComplete();
            await trickleStop.CancelAsync().ConfigureAwait(false);
            await WhenAllQuietlyAsync([.. workers, sitemaps]).ConfigureAwait(false);
            SayWhatTheRateLimitLeftUnchecked();

            // The site's icon, if it is still on its way: a crawl that completed gives it a moment, any other none.
            await _favicon.FinishAsync(_status == CrawlStatus.Completed).ConfigureAwait(false);
            await progressStop.CancelAsync().ConfigureAwait(false);
            await WhenAllQuietlyAsync([progress]).ConfigureAwait(false);
            _elapsed.Stop();
            lock (_pauseSync)
            {
                // The run is over: its last stretch of crawling ends, and is stored as the file closes.
                _isTimed = false;
                KeepRunTime(_crawling.End());
            }

            await CloseAsync().ConfigureAwait(false);
        }

        if (_status == CrawlStatus.Failed)
        {
            error = _writer.Completion.Exception?.GetBaseException().Message ?? error;
        }

        var final = Snapshot();
        _observer.OnProgress(final);
        _logger.LogInformation("Crawl {Status}: {Fetched} URLs fetched in {Elapsed}", _status, final.Fetched, final.Elapsed);
        return new CrawlSummary(_status, final, _request.DatabasePath, error) { RecheckId = _writer.RecheckId };
    }

    /// <summary>
    /// The site kept asking the crawler to slow down even at the end, so some of its URLs were stored as not checked: said
    /// once, with what to do, only when there are some.
    /// </summary>
    private void SayWhatTheRateLimitLeftUnchecked()
    {
        var count = Volatile.Read(ref _rateLimited);
        if (count == 0 || _writer.Completion.IsCompleted)
        {
            return;
        }

        var host = Volatile.Read(ref _rateLimitedHost) ?? _scope.HostName(UrlNormalizer.GetHost(_startUri));
        var urls = count == 1 ? "1 URL wasn't" : $"{count:N0} URLs weren't";
        _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Warning, "rate-limited-unchecked",
            $"{urls} checked because {host} kept asking the crawler to slow down ({HttpStatusNames.Describe(429)}), even when they were asked for again at the end, three times, each after the pause it asked for. Check the site again later, or at the Polite speed.")
        {
            Data = new CrawlNoticeData(Host: host, Reason: HttpStatusNames.Describe(429), Count: count)
        });
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

    /// <summary>
    /// The start URL, then the same path on the site's other versions (bare domain or www, http or https), so the analysis
    /// can tell whether they redirect to the site. They are claimed right behind the start URL, so even a crawl stopped
    /// early has the answer. On a resume, URLs already known are left as they are.
    /// </summary>
    private async ValueTask SeedAsync(CancellationToken cancellationToken)
    {
        var seed = _links.Create(_startUri, LinkKind.Hyperlink, LinkFlags.None, LinkPosition.Content, null);
        await _writer.DiscoverAsync([seed], DiscoverySource.Seed, null, cancellationToken).ConfigureAwait(false);

        var versions = SiteVersions.Of(_startUri)
            .Select(url => _links.Create(new Uri(url), LinkKind.Hyperlink, LinkFlags.None, LinkPosition.Content, null))
            .ToList();
        await _writer.DiscoverAsync(versions, DiscoverySource.SiteVersion, null, cancellationToken).ConfigureAwait(false);
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
            _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Warning, "sitemaps-failed", $"Sitemap discovery failed: {exception.Message}")
            {
                Data = new CrawlNoticeData(Reason: exception.Message)
            });
        }
    }

    /// <summary>
    /// A recheck of robots.txt reads each origin's again (every run reads robots.txt afresh), which stores it over the old:
    /// the origins the plan names, and the start URL's.
    /// </summary>
    private async Task ReadRobotsTxtAgainAsync(RecheckPlan recheck)
    {
        if (!recheck.Kinds.Contains(RecheckKind.RobotsTxt))
        {
            return;
        }

        foreach (var origin in recheck.RobotsOrigins.Append(_startUri.GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.Ordinal))
        {
            await _robots.GetAsync(new Uri(origin + "/")).ConfigureAwait(false);
        }
    }

    /// <summary>A recheck of the sitemaps reads them all again, as the crawl found them: robots.txt's, the extra ones, or the conventional one.</summary>
    private async Task ReadSitemapsAgainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sitemaps.RunAsync(_startUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ChannelClosedException))
        {
            _logger.LogWarning(exception, "Reading the sitemaps again failed");
            _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Warning, "sitemaps-failed", $"Reading the sitemaps again failed: {exception.Message}")
            {
                Data = new CrawlNoticeData(Reason: exception.Message)
            });
        }
    }

    /// <summary>Returns true when the frontier was exhausted, false if the crawl was stopped first.</summary>
    private async Task<bool> DispatchAsync(Task sitemaps, CancellationToken cancellationToken)
    {
        var internalCapacity = _options.Concurrency * 2;
        var externalCapacity = _options.ExternalConcurrency * 2;
        var retrying = false;
        _phase = CrawlPhase.Crawling;

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
            var claim = await _writer.ClaimAsync(Math.Max(0, wantInternal), Math.Max(0, wantExternal), cancellationToken, _trickle.HeldLinked)
                .ConfigureAwait(false);

            // Said before the URLs are handed out, so none is fetched under the phase it ends.
            if (claim.Internal.Count + claim.External.Count > 0)
            {
                // Something to fetch again: what the sitemaps list, as they're read; or, once the last tries are over (a
                // retried URL leaves the held-back ones before it is recorded), whatever is left, such as those URLs.
                if (_phase == CrawlPhase.ReadingSitemaps || (_phase == CrawlPhase.Retrying && !retrying && _retried.IsEmpty))
                {
                    _gates.HoldRecovery(false);
                    _phase = CrawlPhase.Crawling;
                }
            }

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
                var held = _trickle.Held;
                if (claim.InFlight == deferred + held && !producersDone && _phase == CrawlPhase.Crawling)
                {
                    _phase = CrawlPhase.ReadingSitemaps;
                }

                if (claim.InFlight == deferred + held && producersDone && !retrying)
                {
                    if (deferred + held == 0)
                    {
                        return true;
                    }

                    if (deferred == 0)
                    {
                        // Only sections the site limits are left: they go on at their pace, for a while yet.
                        _phase = CrawlPhase.Retrying;
                        _trickle.RestIsOver(_timeProvider.GetUtcNow());
                        await Task.Delay(IDLE_DELAY, _timeProvider, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // The held-back URLs' one more try, after robots.txt's cool-down if it's due, until the last is over, at
                    // the pace each host has taught.
                    _phase = CrawlPhase.Retrying;
                    _gates.HoldRecovery(true);
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
        if (_gates.LongestOutage(_timeProvider.GetUtcNow()) is not { } outage || outage.Duration < _giveUpAfter)
        {
            return false;
        }

        _observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Error, "site-down", _recheck is null
            ? $"{outage.Origin} hasn't answered for {outage.Duration.TotalMinutes:0} minutes, so the crawl stopped. Resume it once the site is back."
            : $"{outage.Origin} hasn't answered for a minute, so the recheck stopped. The pages it didn't reach weren't rechecked.")
        {
            Data = new CrawlNoticeData(Origin: outage.Origin, Duration: outage.Duration)
        });
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
    /// The site's own URLs that got no response, were asked to slow down (429, or 503), or were refused because robots.txt
    /// couldn't be read, are held back (unrecorded) for one more try at the end of the crawl, when a site that was briefly
    /// down is usually back, and one that asked for less has had its pause; one answered 429 again gets up to
    /// <see cref="MAX_RATE_LIMITED_TRIES"/>. URLs failed without a request ("Not checked") aren't: their origin is gone,
    /// and one more try would fail the same way. Nor are probes of the site's other versions, which never hold up a
    /// crawl: one that got no answer is dead.
    /// </summary>
    private bool Defer(PageRecord record)
    {
        var item = record.Item;
        var response = record.Response;
        var refusedByRobots = response.Outcome == FetchOutcome.RobotsDisallowed &&
                              response.Error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true;
        var gotNoResponse = response.Outcome is FetchOutcome.Timeout or FetchOutcome.NetworkError &&
                            response.Error?.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) != true;
        var throttled = IsRateLimited(response) || response is { Outcome: FetchOutcome.ServerError, StatusCode: 503 };
        if (!item.IsInternal || item.Source == DiscoverySource.SiteVersion || !(gotNoResponse || refusedByRobots || throttled) ||
            _retried.ContainsKey(item.Id) && !HasRateLimitedTriesLeft(item.Id, response) || Interlocked.Increment(ref _deferredTotal) > MAX_DEFERRED)
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
    /// though, so it keeps how it ended then: its real error is what makes a link to it broken. One the site asked the
    /// crawler to slow down for, once more, is what it is: not checked.
    /// </summary>
    private static PageRecord KeepTheRealError(PageRecord retried, PageRecord heldBack) =>
        retried.Response.Error?.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) == true && !IsRateLimited(retried.Response)
            ? heldBack
            : retried;

    /// <summary>A URL answered 429 at an end-of-crawl try, with tries left: this one is counted.</summary>
    private bool HasRateLimitedTriesLeft(int id, ResponseRow response) =>
        IsRateLimited(response) && _rateLimitedTries.AddOrUpdate(id, 1, static (_, tries) => tries + 1) < MAX_RATE_LIMITED_TRIES;

    /// <summary>Stored as not checked because the site answered 429 Too Many Requests.</summary>
    private static bool IsRateLimited(ResponseRow response) =>
        response.Error?.StartsWith(RetryingFetcher.RATE_LIMITED_ERROR, StringComparison.Ordinal) == true;

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

                // In a section its host limits apart from the rest: asked at that section's pace, never tying a worker up.
                if (_trickle.TryHold(item, heldBack))
                {
                    _retried.TryRemove(item.Id, out _);
                    continue;
                }

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

                // A 429 that limited its section: the URL waits its turn there.
                if (IsRateLimited(record.Response) && _trickle.TryHold(item, record))
                {
                    _retried.TryRemove(item.Id, out _);
                    continue;
                }

                var deferred = Defer(record);
                if (isRetry)
                {
                    // Its one more try is over, so its held-back record isn't needed any more. Dropped before it's
                    // recorded (Defer has read it), so a claim that returns what it led to finds the last tries over.
                    _retried.TryRemove(item.Id, out _);
                }

                if (!deferred)
                {
                    await RecordAsync(record, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Stores a URL's record, counting the site's own left unchecked by a 429 for the end's notice.</summary>
    private async Task RecordAsync(PageRecord record, CancellationToken cancellationToken)
    {
        var item = record.Item;
        if (item.IsInternal && IsRateLimited(record.Response) && Interlocked.Increment(ref _rateLimited) == 1)
        {
            Volatile.Write(ref _rateLimitedHost, _scope.HostName(UrlNormalizer.GetHost(new Uri(item.Url))));
        }

        await _writer.RecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The held-back sections' URLs, at their pace, until the crawl ends or stops.</summary>
    private async Task TrickleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _trickle.RunAsync(RecordAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The crawl is over, or stopping: anything still held back is re-queued when it resumes.
        }
        catch (ChannelClosedException)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task ReportProgressAsync(CancellationToken cancellationToken)
    {
        var samples = new Queue<(TimeSpan At, long Fetched)>();
        using var timer = new PeriodicTimer(PROGRESS_INTERVAL, _timeProvider);
        var reports = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (++reports % RUN_TIME_EVERY == 0)
                {
                    KeepRunTime(_crawling.Current);
                }

                // One record of counts for the rate and the report, so the rate is of the numbers it goes out with.
                var counts = _counters.Current;
                var now = _elapsed.Elapsed;
                samples.Enqueue((now, counts.Fetched));
                while (samples.Count > 1 && now - samples.Peek().At > TimeSpan.FromSeconds(RATE_WINDOW_SECONDS))
                {
                    samples.Dequeue();
                }

                var (oldestAt, oldestFetched) = samples.Peek();
                var seconds = (now - oldestAt).TotalSeconds;
                Volatile.Write(ref _pagesPerSecond, seconds > 0 ? (counts.Fetched - oldestFetched) / seconds : 0);
                _observer.OnProgress(Snapshot(counts));
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
