using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// The crawl slowing down by itself for a host of the site that starts refusing it after some of its pages were read (a
/// firewall's or bot check's answer, plain HTTP's or the browser's), where it would otherwise record the rest blocked and
/// leave the person to start again more slowly. Its refusals are held back, not recorded. A refused page among pages that
/// get through is that page's own answer, recorded as it was; <see cref="RefusalPace.REFUSALS_TO_SLOW"/> in a row slow the
/// host down (<see cref="RefusalPace"/>): its pages go one at a time, further apart while it refuses and closer as they get
/// through, and the refused URLs are asked again at that pace, each up to <see cref="MAX_RETRIES"/> times. A URL is
/// recorded blocked when its tries are over, or when the host has refused every page at the slowest pace and is given up;
/// and every host still held back <see cref="GIVE_UP_AFTER"/> after the rest of the crawl is over (time paused aside) is
/// given up then. A host none of whose pages was read goes the way it always has: the browser, a person, or blocked.
/// <para>
/// robots.txt, its Crawl-delay and the host's gate apply to every page as they always do: the wait between pages only adds
/// to them, and a host is never asked faster than the crawl's own settings. A check the computer's own browser meets on a
/// host whose pages it had read is not shown to a person at once: the crawl slows down first, and only a check that still
/// doesn't clear by itself at the slowest pace is shown to one, when the crawl may ask (<see cref="JavaScriptRenderer"/>).
/// A person who didn't complete it, or whose check didn't let the crawl in, gives the host up at once. Held-back URLs are claimed but unrecorded, so a stopped crawl asks for them again when it resumes.
/// </para>
/// </summary>
internal sealed class RefusalRetreat(
    PageProcessor processor,
    JavaScriptRenderer renderer,
    CrawlScope scope,
    ICrawlObserver observer,
    PauseGate pause,
    TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>How many times a refused URL is asked again before it is recorded blocked.</summary>
    public const int MAX_RETRIES = 3;

    /// <summary>At most this many refused URLs are held back at once, so memory stays flat: the rest are recorded as they come.</summary>
    public const int MAX_HELD = 1_000;

    /// <summary>How long the held-back URLs may go on once the rest of the crawl is over.</summary>
    public static readonly TimeSpan GIVE_UP_AFTER = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan IDLE = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LONGEST_WAIT = TimeSpan.FromSeconds(1);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, RefusalPace> _hosts = new(StringComparer.Ordinal);
    private readonly LinkedList<HeldUrl> _line = new();
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);
    private int _held;
    private int _heldLinked;
    private DateTimeOffset? _restOverAt;
    private TimeSpan _pausedSinceRestOver;

    /// <summary>Refused URLs held back now, not counting one being asked again.</summary>
    public int Held => Volatile.Read(ref _held);

    /// <summary>Those of them that pages link to, as opposed to ones only sitemaps list: the URLs only sitemaps list don't wait for them.</summary>
    public int HeldLinked => Volatile.Read(ref _heldLinked);

    /// <summary>
    /// Whether a page of <paramref name="uri"/>'s host has been read: its refusals are then this crawl's to deal with, and
    /// its <c>blocked</c> alert is said as a refused URL is recorded, with what was done about it first.
    /// </summary>
    public bool WasRead(Uri uri)
    {
        lock (_sync)
        {
            return _hosts.TryGetValue(KeyOf(uri), out var pace) && pace.Read > 0;
        }
    }

    /// <summary>What was done about the host's refusals, once it has had to slow down for them; null until then.</summary>
    public RetreatFacts? FactsFor(Uri uri)
    {
        lock (_sync)
        {
            return _hosts.TryGetValue(KeyOf(uri), out var pace) && pace.Slowdowns > 0 ? new RetreatFacts(pace.Retried, pace.Slowest) : null;
        }
    }

    /// <summary>
    /// What the browser had done about the host when it last refused a page (a page read since may have changed what it
    /// says now), for the <c>blocked</c> alert; null when it had done nothing.
    /// </summary>
    public FallbackVerdict? VerdictAtRefusal(Uri uri)
    {
        lock (_sync)
        {
            return _hosts.TryGetValue(KeyOf(uri), out var pace) && pace.Verdict != FallbackVerdict.None ? pace.Verdict : null;
        }
    }

    /// <summary>
    /// The page's turn on its host: at once on a host asked at its own pace; on one slowed down, once the page before it
    /// has ended and the wait after it is over. Dispose it as the page ends.
    /// </summary>
    public async ValueTask<RetreatTurn> TurnAsync(FrontierItem item, CancellationToken cancellationToken)
    {
        if (!Applies(item))
        {
            return new RetreatTurn(0);
        }

        RefusalPace? pace;
        lock (_sync)
        {
            if (!_hosts.TryGetValue(KeyOf(new Uri(item.Url)), out pace) || !pace.IsSlow)
            {
                return new RetreatTurn(pace?.Generation ?? 0);
            }
        }

        await pace.Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                // The clock is read outside the lock: a test's fake clock runs timers' callbacks as it is read.
                var now = timeProvider.GetUtcNow();
                TimeSpan wait;
                lock (_sync)
                {
                    if (!pace.IsSlow)
                    {
                        pace.Turn.Release();
                        return new RetreatTurn(pace.Generation);
                    }

                    var due = pace.LastEnd + pace.Interval;
                    if (due <= now)
                    {
                        return new RetreatTurn(pace.Generation, () => End(pace));
                    }

                    // At most a second at a time, so a host given up meanwhile lets its pages go at once.
                    wait = due - now < LONGEST_WAIT ? TimeSpan.FromMilliseconds(Math.Ceiling((due - now).TotalMilliseconds)) : LONGEST_WAIT;
                }

                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            pace.Turn.Release();
            throw;
        }
    }

    /// <summary>
    /// How a page asked in its <paramref name="turn"/> ended. Returns true when it was refused and is held back to be asked
    /// again (it isn't recorded now); false when it is the caller's to record.
    /// </summary>
    public bool TryHold(PageRecord record, RetreatTurn turn) => Answered(record, turn.Generation, null);

    /// <summary>Nothing is left but held-back URLs: the time they may still take starts now.</summary>
    public void RestIsOver(DateTimeOffset now)
    {
        lock (_sync)
        {
            _restOverAt ??= now;
        }
    }

    /// <summary>Asks the held-back URLs again as their hosts' turns come, and records those done with, until stopped.</summary>
    /// <param name="record">Stores a URL's record, once it's done with.</param>
    /// <param name="answered">Hears how each try ended, as it does.</param>
    public async Task RunAsync(Func<PageRecord, CancellationToken, Task> record, Action<PageRecord> answered, CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            foreach (var done in TakeDone(timeProvider.GetUtcNow()))
            {
                await record(done, cancellationToken).ConfigureAwait(false);
            }

            if (TakeRetry() is not { } next)
            {
                await Task.Delay(IDLE, timeProvider, cancellationToken).ConfigureAwait(false);
                continue;
            }

            PageRecord result;
            using (var turn = await TurnAsync(next.Item, cancellationToken).ConfigureAwait(false))
            {
                if (IsGivenUp(next.Key))
                {
                    // Given up on while it waited its turn: it keeps the refusal it had.
                    result = next.Last;
                }
                else
                {
                    try
                    {
                        result = await processor.ProcessAsync(next.Item, cancellationToken, isRetry: true).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogError(exception, "Processing failed for {Url}", next.Item.Url);
                        result = PageProcessor.Failed(next.Item, timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), FetchOutcome.ProcessingError, exception.Message);
                    }

                    answered(result);
                    if (Answered(result, turn.Generation, next))
                    {
                        continue;
                    }
                }
            }

            await record(result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What a page's answer says of its host. A refusal of a host that was read is held back, if there's room and it has
    /// tries left, and may slow the host down; a page that got through eases it, and ends a run of refusals that didn't
    /// slow it: those were the pages' own answers, and are recorded as they were.
    /// </summary>
    /// <param name="retry">The held-back URL this asked again, or null for a page asked for the first time.</param>
    private bool Answered(PageRecord record, int generation, HeldUrl? retry)
    {
        var item = record.Item;
        if (!Applies(item))
        {
            return false;
        }

        var refused = IsRefusal(record);
        if (!refused && !GotAnswer(record))
        {
            // No answer, or a 429: nothing said of the firewall either way.
            return false;
        }

        var uri = new Uri(item.Url);
        var key = KeyOf(uri);
        bool slowed;
        bool held;
        RefusalPace pace;
        lock (_sync)
        {
            pace = PaceOf(key, uri);
            if (!refused)
            {
                pace.GotThrough();
                if (!pace.IsSlow)
                {
                    ReleaseWaiting(key);
                }

                return false;
            }

            if (pace.Read == 0 || pace.GaveUp)
            {
                return false;
            }

            if (renderer.VerdictFor(key) is not FallbackVerdict.None and var verdict)
            {
                pace.Verdict = verdict;
            }

            if (PersonSaidNo(key))
            {
                // A person was asked to complete the host's check and it didn't let the crawl in: waiting won't either.
                GiveUp(key, pace, "a person was asked to complete its check, and that didn't let the crawl in");
                return false;
            }

            // At the slowest pace, a check this browser met after it had read the host's pages, which still doesn't clear by
            // itself, is shown to a person before the host is given up, when the crawl may ask one: the next page asks.
            if (pace.IsSlow && pace.Interval >= RefusalPace.MAX_INTERVAL && generation == pace.Generation &&
                renderer.VerdictFor(key) == FallbackVerdict.NeedsPerson && renderer.AskAPersonNext(key))
            {
                logger.LogInformation("{Host} still checks the crawler at one page every {Seconds:0} s: its next page asks a person", pace.Host,
                    RefusalPace.MAX_INTERVAL.TotalSeconds);
                slowed = false;
            }
            else
            {
                slowed = pace.Refused(generation);
            }

            if (pace.GaveUp)
            {
                logger.LogInformation("Giving up on {Host}: it refused {Count} pages in a row at one page every {Seconds:0} s", pace.Host,
                    RefusalPace.GIVE_UP_REFUSALS, RefusalPace.MAX_INTERVAL.TotalSeconds);
                GiveUp(key, pace, null);
                return false;
            }

            var tries = retry is null ? 0 : retry.Retries + 1;
            held = tries < MAX_RETRIES && (retry is not null || Volatile.Read(ref _held) < MAX_HELD);
            if (held)
            {
                Hold(new HeldUrl(key, item, record, tries) { PersonAsked = retry?.PersonAsked ?? false }, pace);
            }
        }

        if (slowed)
        {
            SaySlowingDown(pace, record.Response.BlockReason ?? "refused");
        }

        return held;
    }

    /// <summary>The host's pace, made the first time one of its pages is answered. Under the lock.</summary>
    private RefusalPace PaceOf(string key, Uri uri)
    {
        if (!_hosts.TryGetValue(key, out var pace))
        {
            pace = new RefusalPace(scope.HostName(UrlNormalizer.GetHost(uri)));
            _hosts[key] = pace;
        }

        return pace;
    }

    /// <summary>The held-back URLs done with: given up on, released by a page that got through, or whose time ran out.</summary>
    private List<PageRecord> TakeDone(DateTimeOffset now)
    {
        List<PageRecord> done = [];
        lock (_sync)
        {
            if (_line.Count == 0)
            {
                return done;
            }

            var timeUp = _restOverAt is { } over && now >= over + GIVE_UP_AFTER + _pausedSinceRestOver;
            for (var node = _line.First; node is not null;)
            {
                var next = node.Next;
                var held = node.Value;
                var pace = _hosts[held.Key];
                if (timeUp && !pace.GaveUp)
                {
                    GiveUp(held.Key, pace, $"the {GIVE_UP_AFTER.TotalMinutes:0} minutes its refused pages are given once the rest of the crawl is over ran out");
                }

                // Once the rest is over, a run of refusals that never slowed the host down is over too: the pages' own answers.
                if (held.Released || pace.GaveUp || (_restOverAt is not null && !pace.IsSlow && !held.PersonAsked))
                {
                    if (!pace.GaveUp && MayAskAPerson(held, pace))
                    {
                        // The page's own check, which doesn't clear by itself: shown to a person once, as it was before
                        // the browser had read the host's pages, rather than recorded blocked.
                        held.Released = false;
                        held.PersonAsked = true;
                        renderer.AskAPersonNext(held.Key);
                    }
                    else
                    {
                        _line.Remove(node);
                        Count(held.Item, pace, -1);
                        done.Add(held.Last);
                    }
                }

                node = next;
            }
        }

        return done;
    }

    /// <summary>The first held-back URL of a host slowed down, to ask again: counted as asked again the first time.</summary>
    private HeldUrl? TakeRetry()
    {
        lock (_sync)
        {
            for (var node = _line.First; node is not null; node = node.Next)
            {
                var pace = _hosts[node.Value.Key];
                if (!(pace.IsSlow || node.Value.PersonAsked) || node.Value.Released)
                {
                    continue;
                }

                _line.Remove(node);
                Count(node.Value.Item, pace, -1);
                if (node.Value.Retries == 0)
                {
                    pace.Retried++;
                }

                return node.Value;
            }

            return null;
        }
    }

    /// <summary>A held-back URL's check needs a person, who may be asked, and hasn't been for it. Under the lock.</summary>
    private bool MayAskAPerson(HeldUrl held, RefusalPace pace) =>
        !held.PersonAsked && pace.Verdict == FallbackVerdict.NeedsPerson && renderer.MayAskAPerson(held.Key);

    private bool IsGivenUp(string key)
    {
        lock (_sync)
        {
            return _hosts[key].GaveUp;
        }
    }

    /// <summary>Gives a host up: its held-back URLs are recorded as they ended, and the browser stops trying its check. Under the lock.</summary>
    private void GiveUp(string key, RefusalPace pace, string? why)
    {
        if (why is not null)
        {
            logger.LogInformation("Giving up on {Host}: {Why}", pace.Host, why);
        }

        pace.GiveUp();
        ReleaseWaiting(key);
        renderer.GiveUp(key);
    }

    /// <summary>The host's held-back URLs are done with, to be recorded as they ended. Under the lock.</summary>
    private void ReleaseWaiting(string key)
    {
        foreach (var held in _line.Where(held => held.Key == key))
        {
            held.Released = true;
        }
    }

    private void Hold(HeldUrl held, RefusalPace pace)
    {
        _line.AddLast(held);
        Count(held.Item, pace, 1);
    }

    private void Count(FrontierItem item, RefusalPace pace, int change)
    {
        pace.Waiting += change;
        Interlocked.Add(ref _held, change);
        if (item.Depth < CrawlDepth.UNLINKED)
        {
            Interlocked.Add(ref _heldLinked, change);
        }
    }

    private void End(RefusalPace pace)
    {
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            pace.LastEnd = now;
        }

        pace.Turn.Release();
    }

    /// <summary>Waits out a pause, and counts it once the rest is over: the held-back URLs' time doesn't run while the crawl is paused.</summary>
    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        if (!pause.IsPaused)
        {
            return;
        }

        var pausedAt = timeProvider.GetUtcNow();
        await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            if (_restOverAt is not null)
            {
                _pausedSinceRestOver += now - pausedAt;
            }
        }
    }

    /// <summary>
    /// A person was asked to complete the host's check, or it was completed, and the crawl still wasn't let in: the window
    /// wasn't used in time, was closed, the site refused the page all the same, or the page went elsewhere.
    /// </summary>
    private bool PersonSaidNo(string key) => renderer.VerdictFor(key) is FallbackVerdict.PersonDidNotFinish or FallbackVerdict.PersonClosedWindow
        or FallbackVerdict.RefusedAfterCheck or FallbackVerdict.WentElsewhere;

    /// <summary>Said once per host, the first time it slows down.</summary>
    private void SaySlowingDown(RefusalPace pace, string reason)
    {
        TimeSpan interval;
        int read;
        lock (_sync)
        {
            interval = pace.Interval;
            read = pace.Read;
            if (!_said.Add(pace.Host))
            {
                logger.LogInformation("{Host} refused the crawler again ({Reason}), so it slows down again: one page at a time, {Seconds:0} s apart",
                    pace.Host, reason, interval.TotalSeconds);
                return;
            }
        }

        logger.LogInformation("{Host} started refusing the crawler ({Reason}) after {Read} pages were read; slowing down to one page at a time, {Seconds:0} s apart",
            pace.Host, reason, read, interval.TotalSeconds);
        var pages = read == 1 ? "1 of its pages was" : $"{read:N0} of its pages were";
        observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Info, "blocked-slowdown",
            $"{pace.Host} started refusing the crawler ({reason}) after {pages} read, so the crawl slowed down by itself: its pages are read one at a time, " +
            $"{HostPace.DescribeWait(interval)} apart to start, further apart while it refuses and closer again as pages get through. The URLs it refused are " +
            $"asked again, and any still refused at one page every {HostPace.DescribeWait(RefusalPace.MAX_INTERVAL)} are recorded as blocked.")
        {
            Data = new CrawlNoticeData(Host: pace.Host, Reason: reason, Count: RefusalPace.REFUSALS_TO_SLOW, Duration: interval)
        });
    }

    /// <summary>
    /// The site's own pages: not a probe of another version of it, which never holds up a crawl, and not a file's status
    /// check, which plain HTTP asks for even of a host the browser reads.
    /// </summary>
    private static bool Applies(FrontierItem item) => item is { IsInternal: true, Mode: FetchMode.Crawl } && item.Source != DiscoverySource.SiteVersion;

    /// <summary>A firewall's or bot check's answer. A 429 is the site asking for less, which the host's pace deals with.</summary>
    private static bool IsRefusal(PageRecord record) =>
        record.Response.Outcome == FetchOutcome.Blocked && !RateLimitedErrors.IsRateLimited(record.Response.Error);

    /// <summary>An answer of the site's own: whatever its status, the firewall let it through.</summary>
    private static bool GotAnswer(PageRecord record) =>
        record.Response.StatusCode is > 0 && !RateLimitedErrors.IsRateLimited(record.Response.Error);

    /// <summary>example.com and www.example.com are one host here, as they are to the browser fallback.</summary>
    private static string KeyOf(Uri uri) => CrawlScope.SiteHost(UrlNormalizer.GetHost(uri));

    /// <summary>A refused URL held back: its host, what it was refused with last, and how many times it has been asked again.</summary>
    private sealed class HeldUrl(string key, FrontierItem item, PageRecord last, int retries)
    {
        public string Key => key;
        public FrontierItem Item => item;
        public PageRecord Last => last;
        public int Retries => retries;

        /// <summary>Done with: to be recorded as it ended.</summary>
        public bool Released { get; set; }

        /// <summary>Its check is to be shown to a person, once, as it is asked again.</summary>
        public bool PersonAsked { get; set; }
    }
}
