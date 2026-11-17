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
/// section <see cref="GIVE_UP_AFTER"/> after the rest of the crawl is over: what is left is stored as not checked.
/// </summary>
internal sealed class SectionTrickle(
    HostGateRegistry gates,
    PageProcessor processor,
    CrawlScope scope,
    ICrawlObserver observer,
    PauseGate pause,
    TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>How long the held-back sections may go on once the rest of the crawl is over.</summary>
    public static readonly TimeSpan GIVE_UP_AFTER = TimeSpan.FromMinutes(20);

    /// <summary>429s in a row, at a section's slowest pace, that give it up.</summary>
    public const int GIVE_UP_FAILURES = 3;

    private static readonly TimeSpan IDLE = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LONGEST_SLEEP = TimeSpan.FromSeconds(1);

    private readonly Lock _sync = new();
    private readonly Dictionary<SectionPace, Line> _lines = [];
    private int _held;
    private int _heldLinked;
    private DateTimeOffset? _restOverAt;

    /// <summary>URLs held back now, not counting one being asked.</summary>
    public int Held => Volatile.Read(ref _held);

    /// <summary>Those of them that pages link to, as opposed to ones only sitemaps list: the URLs only sitemaps list don't wait for them.</summary>
    public int HeldLinked => Volatile.Read(ref _heldLinked);

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
                line = new Line(scope.HostName(UrlNormalizer.GetHost(uri)));
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
            _restOverAt ??= now;
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
                .. _lines.Where(line => line.Value.Waiting.Count > 0).Select(line =>
                    new CrawlWait(CrawlWaitReason.SectionLimited, line.Value.Host, Urls: line.Value.Waiting.Count,
                        Duration: line.Key.NextDue > now ? line.Key.NextDue - now : TimeSpan.Zero, Section: line.Key.Path))
            ];
        }
    }

    /// <summary>Asks the held-back URLs, one at a time per section, as their turns come, until stopped.</summary>
    /// <param name="record">Stores a URL's record, once it's done with.</param>
    public async Task RunAsync(Func<PageRecord, CancellationToken, Task> record, CancellationToken cancellationToken)
    {
        while (true)
        {
            await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            if (IsRateLimited(result) && !section.IsRefusingEverything(GIVE_UP_FAILURES))
            {
                // Still refused: back to the front of the line, for its section's next turn.
                PutBack(section, item, result);
                continue;
            }

            await record(result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A URL whose turn has come, taken from the front of its section's line.</summary>
    private (SectionPace Section, FrontierItem Item, PageRecord? Last)? Take(DateTimeOffset now)
    {
        lock (_sync)
        {
            foreach (var (section, line) in _lines)
            {
                if (line.Waiting.First is { } first && section.TryStart(now))
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
    /// has been over for <see cref="GIVE_UP_AFTER"/>: not checked, the site having asked the crawler to slow down.
    /// </summary>
    private List<PageRecord> GiveUp(DateTimeOffset now)
    {
        List<PageRecord> givenUp = [];
        lock (_sync)
        {
            var atTheCap = _restOverAt is { } over && now - over >= GIVE_UP_AFTER;
            foreach (var (section, line) in _lines.Where(line => line.Value.Waiting.Count > 0))
            {
                if (!atTheCap && !section.IsRefusingEverything(GIVE_UP_FAILURES))
                {
                    continue;
                }

                logger.LogInformation("Giving up on {Count} URLs in {Section} of {Host}", line.Waiting.Count, section.Path, line.Host);
                foreach (var (item, last) in line.Waiting)
                {
                    Count(item, -1);
                    givenUp.Add(last is not null && IsRateLimited(last)
                        ? last
                        : PageProcessor.Failed(item, now.ToUnixTimeMilliseconds(), FetchOutcome.NetworkError,
                            $"{RetryingFetcher.RATE_LIMITED_ERROR} ({HttpStatusNames.Describe(429)})"));
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
            var next = _lines.Where(line => line.Value.Waiting.Count > 0).Select(line => line.Key.NextDue).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (next == DateTimeOffset.MaxValue)
            {
                return IDLE;
            }

            var wait = next - now;
            return wait <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : wait < LONGEST_SLEEP ? TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds)) : LONGEST_SLEEP;
        }
    }

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

    private static bool IsRateLimited(PageRecord record) =>
        record.Response.Error?.StartsWith(RetryingFetcher.RATE_LIMITED_ERROR, StringComparison.Ordinal) == true;

    /// <summary>One section's URLs waiting their turn, oldest first, and its host as progress names it.</summary>
    private sealed class Line(string host)
    {
        public string Host => host;

        public LinkedList<(FrontierItem Item, PageRecord? Last)> Waiting { get; } = new();
    }
}
