using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// The site's URLs in a section its host limits apart from the rest (<see cref="SectionPace"/>): held back from the
/// workers, so they never tie one up, and asked one at a time at the section's interval, during the crawl and after it.
/// One answered 429 again goes back to the front of its section's line; nothing is given up while pages still get
/// through. A section is given up on after <see cref="GIVE_UP_FAILURES"/> 429s in a row at its slowest pace (one page
/// every <see cref="SectionPace.MAX_INTERVAL"/>, which the cap leaves no room for), and every
/// section <see cref="GIVE_UP_AFTER"/> after the rest of the crawl is over: what is left is stored as not checked, as
/// refused if it was asked and answered 429, and as never asked (<see cref="RetryingFetcher.NEVER_ASKED_ERROR"/>) if its
/// turn hadn't come. A try that would come after that comes as the time runs out instead, so each section has its last
/// try then, and no wait says a try comes later than that. Time the crawl spends paused is no part of those 20 minutes.
/// <para>
/// A Shopify store refuses a crawler without a signature for as long as it asks, and every try renews its penalty, so
/// waiting gets nothing: a section on a store the crawl has no signature for (<see cref="ShopifyWatch.IsUnsignedShopify"/>)
/// isn't asked while the rest of the crawl goes on, and once that is over it has one more try, after the pause the store
/// asked for (at most <see cref="SHOPIFY_RETRY_CAP"/>), and is given up on. A signed crawl the store refuses keeps the
/// polite path.
/// </para>
/// </summary>
internal sealed class SectionTrickle(
    HostGateRegistry gates,
    PageProcessor processor,
    CrawlScope scope,
    ICrawlObserver observer,
    PauseGate pause,
    TimeProvider timeProvider,
    ILogger logger,
    Func<TimeSpan>? timeCrawled = null)
{
    /// <summary>How long the held-back sections may go on once the rest of the crawl is over: each has its last try as it ends.</summary>
    public static readonly TimeSpan GIVE_UP_AFTER = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The longest an unsigned crawl of a Shopify store waits, once the rest is over, for a limited section's one more try:
    /// the Retry-After the store asked for (a minute, as Shopify asks), at most this.
    /// </summary>
    public static readonly TimeSpan SHOPIFY_RETRY_CAP = TimeSpan.FromMinutes(2);

    /// <summary>429s in a row, at a section's slowest pace, that give it up.</summary>
    public const int GIVE_UP_FAILURES = 3;

    private static readonly TimeSpan IDLE = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LONGEST_SLEEP = TimeSpan.FromSeconds(1);

    private readonly Lock _sync = new();
    private readonly Dictionary<SectionPace, Line> _lines = [];

    // The sections given up on with URLs never asked, for the notice at the end of the crawl.
    private readonly Dictionary<SectionPace, SectionGivenUp> _givenUp = [];
    private int _held;
    private int _heldLinked;
    private DateTimeOffset? _restOverAt;

    // Time spent paused since the rest was over, by the run's own clock of the time it spent crawling: the sections' time
    // runs out that much later.
    private TimeSpan _pausedSinceRestOver;
    private TimeSpan _crawledAtRestOver;

    /// <summary>URLs held back now, not counting one being asked.</summary>
    public int Held => Volatile.Read(ref _held);

    /// <summary>Those of them that pages link to, as opposed to ones only sitemaps list: the URLs only sitemaps list don't wait for them.</summary>
    public int HeldLinked => Volatile.Read(ref _heldLinked);

    /// <summary>The sections given up on while URLs in them were still waiting their turn, and why.</summary>
    public List<SectionGivenUp> GivenUp
    {
        get
        {
            lock (_sync)
            {
                return [.. _givenUp.Values];
            }
        }
    }

    /// <summary>
    /// Holds the item back when its URL is in a section its host limits: one of the site's own, not a probe. <paramref
    /// name="last"/> is how it last ended, when it was asked.
    /// </summary>
    public bool TryHold(FrontierItem item, PageRecord? last)
    {
        if (!item.IsInternal || item.Source == DiscoverySource.SiteVersion)
        {
            return false;
        }

        var uri = new Uri(item.Url);
        if (gates.For(uri).LimitedSectionOf(uri) is not { } section)
        {
            return false;
        }

        bool isNew;
        lock (_sync)
        {
            isNew = !_lines.TryGetValue(section, out var line);
            if (line is null)
            {
                line = new Line(scope.HostName(UrlNormalizer.GetHost(uri)), uri);
                _lines[section] = line;
            }

            line.Waiting.AddLast((item, last));
            Count(item, 1);
        }

        if (isNew)
        {
            SayTheSectionIsLimited(section, scope.HostName(UrlNormalizer.GetHost(uri)));
        }

        return true;
    }

    /// <summary>Nothing is left but the held-back sections: the time they may still take starts now.</summary>
    public void RestIsOver(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_restOverAt is null)
            {
                _restOverAt = now;
                _crawledAtRestOver = timeCrawled?.Invoke() ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>Each section with URLs waiting: how many, and how long until the next is tried, for progress to say.</summary>
    public List<CrawlWait> Waits()
    {
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            return
            [
                .. _lines.Where(line => line.Value.Waiting.Count > 0).Select(line => (Line: line.Value, Section: line.Key, Due: DueAt(line.Key, line.Value)))
                    .Select(line => new CrawlWait(CrawlWaitReason.SectionLimited, line.Line.Host, Urls: line.Line.Waiting.Count,
                        Duration: line.Due is not { } due ? null : due > now ? due - now : TimeSpan.Zero, Section: line.Section.Path))
            ];
        }
    }

    /// <summary>Asks the held-back URLs, one at a time per section, as their turns come, until stopped.</summary>
    /// <param name="record">Stores a URL's record, once it's done with.</param>
    /// <param name="answered">Hears how each try ended, as it does: a URL refused again is put back, not recorded.</param>
    public async Task RunAsync(Func<PageRecord, CancellationToken, Task> record, Action<PageRecord> answered, CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow();
            foreach (var givenUp in GiveUp(now))
            {
                await record(givenUp, cancellationToken).ConfigureAwait(false);
            }

            if (Take(now) is not var (section, item, last))
            {
                await Task.Delay(SleepFor(now), timeProvider, cancellationToken).ConfigureAwait(false);
                continue;
            }

            PageRecord result;
            try
            {
                result = await processor.ProcessAsync(item, cancellationToken, isRetry: true).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Processing failed for {Url}", item.Url);
                result = PageProcessor.Failed(item, timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), FetchOutcome.ProcessingError, exception.Message);
            }

            answered(result);
            if (IsRateLimited(result) && !section.IsRefusingEverything(GIVE_UP_FAILURES))
            {
                // Still refused: back to the front of the line, for its section's next turn.
                PutBack(section, item, result);
                continue;
            }

            await record(result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits out a pause, and counts it once the rest is over: the sections' time doesn't run while the crawl is paused.</summary>
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
            if (_restOverAt is not { } over)
            {
                return;
            }

            // The run's clock of the time spent crawling stops as the pause begins, which may be a little before this loop
            // saw it: the time since the rest was over that wasn't crawling was paused. In whole seconds, so it moves only
            // with a pause. Without that clock, the pause as seen here.
            var paused = timeCrawled is null
                ? _pausedSinceRestOver + (now - pausedAt)
                : now - over - (timeCrawled() - _crawledAtRestOver);
            var whole = TimeSpan.FromSeconds(Math.Floor(paused.TotalSeconds));
            if (whole > _pausedSinceRestOver)
            {
                _pausedSinceRestOver = whole;
            }
        }
    }

    /// <summary>A URL whose turn has come, taken from the front of its section's line: at the latest as the time runs out.</summary>
    private (SectionPace Section, FrontierItem Item, PageRecord? Last)? Take(DateTimeOffset now)
    {
        lock (_sync)
        {
            foreach (var (section, line) in _lines)
            {
                if (IsWaitingForTheRest(line))
                {
                    continue;
                }

                if (line.Waiting.First is { } first && section.TryStart(now, TimeRunsOutAt(section, line)))
                {
                    line.Waiting.RemoveFirst();
                    Count(first.Value.Item, -1);
                    return (section, first.Value.Item, first.Value.Last);
                }
            }

            return null;
        }
    }

    private void PutBack(SectionPace section, FrontierItem item, PageRecord last)
    {
        lock (_sync)
        {
            _lines[section].Waiting.AddFirst((item, last));
            Count(item, 1);
        }
    }

    /// <summary>
    /// The URLs of sections that refuse everything at their slowest pace, and of every section once the rest of the crawl
    /// has been over for <see cref="GIVE_UP_AFTER"/> and it has had its last try: not checked, the site having asked the
    /// crawler to slow down. One asked and refused keeps that record; one whose turn hadn't come is stored as never asked.
    /// </summary>
    private List<PageRecord> GiveUp(DateTimeOffset now)
    {
        List<PageRecord> givenUp = [];
        lock (_sync)
        {
            foreach (var (section, line) in _lines.Where(line => line.Value.Waiting.Count > 0))
            {
                var timeRunsOut = TimeRunsOutAt(section, line);
                // As the time runs out, a section's last try comes first: it is given up on once that try has started.
                var timeRanOut = now >= timeRunsOut && section.LastTurn >= timeRunsOut;
                if (!timeRanOut && !section.IsRefusingEverything(GIVE_UP_FAILURES))
                {
                    continue;
                }

                logger.LogInformation("Giving up on {Count} URLs in {Section} of {Host}", line.Waiting.Count, section.Path, line.Host);
                var neverAsked = 0;
                foreach (var (item, last) in line.Waiting)
                {
                    Count(item, -1);
                    if (last is not null && IsRateLimited(last))
                    {
                        givenUp.Add(last);
                        continue;
                    }

                    neverAsked++;
                    givenUp.Add(PageProcessor.Failed(item, now.ToUnixTimeMilliseconds(), FetchOutcome.NetworkError, RetryingFetcher.NEVER_ASKED_ERROR));
                }

                if (neverAsked > 0)
                {
                    _givenUp[section] = new SectionGivenUp(line.Host, section.Path, timeRanOut);
                }

                line.Waiting.Clear();
            }
        }

        return givenUp;
    }

    /// <summary>Until the next section's turn, at most a second, so a pause or a give-up is seen soon.</summary>
    private TimeSpan SleepFor(DateTimeOffset now)
    {
        lock (_sync)
        {
            var next = _lines.Where(line => line.Value.Waiting.Count > 0).Select(line => DueAt(line.Key, line.Value) ?? DateTimeOffset.MaxValue)
                .DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (next == DateTimeOffset.MaxValue)
            {
                return IDLE;
            }

            var wait = next - now;
            return wait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : wait < LONGEST_SLEEP ? TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds)) : LONGEST_SLEEP;
        }
    }

    /// <summary>
    /// When a held-back section's time runs out, once the rest of the crawl is over, paused time aside: 20 minutes on, or for
    /// a Shopify store the crawl has no signature for, the pause it asked for (<see cref="SHOPIFY_RETRY_CAP"/> at most).
    /// Called under the lock.
    /// </summary>
    private DateTimeOffset? TimeRunsOutAt(SectionPace section, Line line) =>
        _restOverAt + (IsUnsignedShopify(line) ? ShopifyWait(section) : GIVE_UP_AFTER) + _pausedSinceRestOver;

    /// <summary>When a section's next try comes, or null while it waits for the rest of the crawl. Called under the lock.</summary>
    private DateTimeOffset? DueAt(SectionPace section, Line line) =>
        IsWaitingForTheRest(line) ? null : section.NextDueBy(TimeRunsOutAt(section, line));

    /// <summary>A section of an unsigned Shopify store isn't asked until the rest of the crawl is over. Called under the lock.</summary>
    private bool IsWaitingForTheRest(Line line) => _restOverAt is null && IsUnsignedShopify(line);

    private bool IsUnsignedShopify(Line line) => processor.Shopify.IsUnsignedShopify(line.Sample);

    /// <summary>The pause the store asked for, as the section learned it, at least a second and at most <see cref="SHOPIFY_RETRY_CAP"/>.</summary>
    private static TimeSpan ShopifyWait(SectionPace section) =>
        section.Interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : section.Interval > SHOPIFY_RETRY_CAP ? SHOPIFY_RETRY_CAP : section.Interval;

    private void Count(FrontierItem item, int change)
    {
        Interlocked.Add(ref _held, change);
        if (item.Depth < CrawlDepth.UNLINKED)
        {
            Interlocked.Add(ref _heldLinked, change);
        }
    }

    private void SayTheSectionIsLimited(SectionPace section, string host) =>
        observer.OnNotice(new CrawlNotice(CrawlNoticeLevel.Info, "section-rate-limited",
            $"{host} limits its {section.Path} pages ({HttpStatusNames.Describe(429)}), not the rest of the site. They are read one at a time, about {HostPace.DescribeWait(section.Interval)} apart to start, sooner as they get through, while the rest of the site goes on at its own pace.")
        {
            Data = new CrawlNoticeData(Host: host, Url: section.Path, Reason: HttpStatusNames.Describe(429), Duration: section.Interval)
        });

    private static bool IsRateLimited(PageRecord record) => RateLimitedErrors.IsRateLimited(record.Response.Error);

    /// <summary>One section's URLs waiting their turn, oldest first, its host as progress names it, and one of its URLs.</summary>
    private sealed class Line(string host, Uri sample)
    {
        public string Host => host;

        /// <summary>A URL of the section, for what is known of its host (a Shopify store's, signed or not).</summary>
        public Uri Sample => sample;

        public LinkedList<(FrontierItem Item, PageRecord? Last)> Waiting { get; } = new();
    }
}
