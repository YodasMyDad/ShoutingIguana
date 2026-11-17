using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Politeness for one host: every request to it goes through here, pages, assets, robots.txt, sitemaps and the icon
/// alike. It keeps to the pace the host's answers have taught (<see cref="HostPace"/>: a slow start, then slower when
/// the host asks), never faster than the crawl's settings: so many requests at once, their starts spaced by the interval,
/// a robots.txt Crawl-delay, and a pause after a Retry-After or while the host doesn't answer. The spacing is worked out
/// when a request is due, from when the last one actually started and the pace as it stands then.
/// </summary>
internal sealed class HostGate
{
    /// <summary>URLs in a row, each after its retries, that got no response before an origin counts as unreachable.</summary>
    public const int UNREACHABLE_AFTER_URLS = 3;

    /// <summary>
    /// Sections answered 429 within <see cref="SPREAD_WINDOW"/> that make the limit the host's own rather than a section's:
    /// the whole host then slows down.
    /// </summary>
    public const int SPREAD_SECTIONS = 3;

    private static readonly TimeSpan SPREAD_WINDOW = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _slots;
    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _configuredInterval;
    private readonly HostPace _pace;
    private TimeSpan _minimumInterval;
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _waitingForRobotsTxt;

    // Requests allowed at once, as the pace has it; and slots that come back while the pace allows fewer than are out,
    // which are kept rather than handed on.
    private int _window;
    private int _withheld;

    // Sections the host limits apart from the rest, by the folder that names them, and when each section last sent a 429.
    private readonly Dictionary<string, SectionPace> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _throttledSections = new(StringComparer.Ordinal);

    /// <param name="maxConcurrency">The most requests at once: the crawl's setting.</param>
    /// <param name="minimumInterval">The least time between request starts: the crawl's rate, or zero for none.</param>
    public HostGate(int maxConcurrency, TimeSpan minimumInterval, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _configuredInterval = minimumInterval;
        _minimumInterval = minimumInterval;
        _pace = new HostPace(maxConcurrency, minimumInterval > TimeSpan.Zero ? 1 / minimumInterval.TotalSeconds : null);
        _window = _pace.Window;
        _slots = new SemaphoreSlim(_window, maxConcurrency);
    }

    /// <summary>Requests allowed at once now. Tests read it.</summary>
    public int Window
    {
        get
        {
            lock (_sync)
            {
                return _window;
            }
        }
    }

    /// <summary>The time between request starts the host's pace asks for now, a Crawl-delay aside. Tests read it.</summary>
    public TimeSpan PaceInterval
    {
        get
        {
            lock (_sync)
            {
                return _pace.Interval;
            }
        }
    }

    /// <summary>Holds the pace's recovery, or lets it go on (<see cref="HostPace.IsRecoveryHeld"/>).</summary>
    public void HoldRecovery(bool held)
    {
        lock (_sync)
        {
            _pace.IsRecoveryHeld = held;
        }
    }

    /// <summary>What holds this host's requests up now, beyond the crawl's settings, for its progress to say.</summary>
    public HostGateState ReadState()
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            return new HostGateState(
                _minimumInterval > _configuredInterval ? _minimumInterval : null,
                _pace.SlowedTo,
                _pausedUntil > now ? _pausedUntil - now : TimeSpan.Zero,
                Volatile.Read(ref _waitingForRobotsTxt));
        }
    }

    /// <summary>
    /// A URL of this host starts (1) or stops (-1) waiting for its origin's robots.txt, which only the first URLs of an
    /// origin ever do: counted for progress, which says whose robots.txt the crawl waits for.
    /// </summary>
    public void AddRobotsTxtWaiter(int change) => Interlocked.Add(ref _waitingForRobotsTxt, change);

    /// <summary>Holds every request back for <paramref name="duration"/>, e.g. while the host is unreachable.</summary>
    public void Pause(TimeSpan duration)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            _pausedUntil = Max(_pausedUntil, now + duration);
        }
    }

    /// <summary>Applies a robots.txt Crawl-delay; never lowers an interval the user configured.</summary>
    public void RaiseMinimumInterval(TimeSpan interval)
    {
        lock (_sync)
        {
            if (interval > _minimumInterval)
            {
                _minimumInterval = interval;
            }
        }
    }

    public async ValueTask<HostGateLease> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Worked out again after every wait: a pace that changed, or a Retry-After that arrived, while this request
            // waited applies to it as it stands when it is due.
            DateTimeOffset startedAt;
            while (!TryStart(out var wait, out startedAt))
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return new HostGateLease(this, startedAt);
        }
        catch
        {
            Release();
            throw;
        }
    }

    /// <summary>The sections the host limits apart from the rest, as they stand.</summary>
    public IReadOnlyList<SectionPace> Sections
    {
        get
        {
            lock (_sync)
            {
                return [.. _sections.Values];
            }
        }
    }

    /// <summary>
    /// The section the host limits apart from the rest that <paramref name="uri"/> is in, or null: one named by any folder of
    /// its path (/collections/belts/products/belt is in /products/), or the root's for a URL in no folder.
    /// </summary>
    public SectionPace? LimitedSectionOf(Uri uri)
    {
        lock (_sync)
        {
            return LimitedSectionOfLocked(uri);
        }
    }

    /// <summary>How the host answered a request; <paramref name="startedAt"/> is when it started, or now when not known.</summary>
    /// <param name="latency">Its time to first byte: a host answering ever slower holds its pace.</param>
    /// <param name="uri">
    /// What was asked. A 429 for it limits its section alone (<see cref="SectionPace"/>) and leaves the host's pace as it
    /// is, unless 429s have come from <see cref="SPREAD_SECTIONS"/> sections lately: then the limit is the host's.
    /// </param>
    public void Report(HostSignal signal, TimeSpan? retryAfter = null, DateTimeOffset? startedAt = null, TimeSpan? latency = null, Uri? uri = null)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            var section = uri is null ? null : LimitedSectionOfLocked(uri);
            if (signal == HostSignal.Throttled && uri is not null)
            {
                var folder = section?.Folder ?? LastFolder(uri);
                if (!IsSpread(folder, now))
                {
                    if (section is null)
                    {
                        section = new SectionPace(folder);
                        _sections[folder] = section;
                    }

                    section.Throttled(retryAfter, startedAt ?? now, now);
                    return;
                }
            }
            else if (signal == HostSignal.Success)
            {
                section?.GotThrough();
            }

            var pause = _pace.Answered(signal, retryAfter, startedAt ?? now, now, latency);
            if (pause > TimeSpan.Zero)
            {
                _pausedUntil = Max(_pausedUntil, now + pause);
            }

            ResizeWindow(_pace.Window);
        }
    }

    /// <summary>
    /// A request is over: its slot goes back, unless the pace now allows fewer at once than are out. Under the lock, so a
    /// resize never counts a slot that is on its way back as out; the semaphore's waiters resume on their own threads.
    /// </summary>
    internal void Release()
    {
        lock (_sync)
        {
            if (_withheld > 0)
            {
                _withheld--;
                return;
            }

            _slots.Release();
        }
    }

    /// <summary>Starts a request now if the spacing since the last start and any pause allow it; otherwise says how long to wait.</summary>
    private bool TryStart(out TimeSpan wait, out DateTimeOffset startedAt)
    {
        // The clock is read outside the lock: a test's fake clock runs timers' callbacks as it is read.
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            var spacing = _pace.Interval > _minimumInterval ? _pace.Interval : _minimumInterval;
            var due = Max(_lastStart + spacing, _pausedUntil);
            startedAt = now;
            if (due <= now)
            {
                _lastStart = now;
                _pace.Started(now);
                wait = TimeSpan.Zero;
                return true;
            }

            // Whole milliseconds, rounded up: Task.Delay drops the rest, and a wait it cut to nothing would spin.
            wait = TimeSpan.FromMilliseconds(Math.Ceiling((due - now).TotalMilliseconds));
            return false;
        }
    }

    /// <summary>
    /// Puts slots into play, or takes them out: free ones at once, ones in use as they come back. Under the lock; the
    /// semaphore's waiters resume on their own threads, never inside it.
    /// </summary>
    private void ResizeWindow(int window)
    {
        for (; _window < window; _window++)
        {
            if (_withheld > 0)
            {
                _withheld--;
            }
            else
            {
                _slots.Release();
            }
        }

        for (; _window > window; _window--)
        {
            if (!_slots.Wait(0))
            {
                _withheld++;
            }
        }
    }

    /// <summary>A section 429'd now; whether, with the others that have lately, the limit is spread over the host.</summary>
    private bool IsSpread(string folder, DateTimeOffset now)
    {
        _throttledSections[folder] = now;
        foreach (var stale in _throttledSections.Where(section => now - section.Value > SPREAD_WINDOW).Select(section => section.Key).ToList())
        {
            _throttledSections.Remove(stale);
        }

        return _throttledSections.Count >= SPREAD_SECTIONS;
    }

    private SectionPace? LimitedSectionOfLocked(Uri uri)
    {
        if (_sections.Count == 0)
        {
            return null;
        }

        var folders = FoldersOf(uri);
        if (folders.Count == 0)
        {
            return _sections.GetValueOrDefault(string.Empty);
        }

        for (var index = folders.Count - 1; index >= 0; index--)
        {
            if (_sections.TryGetValue(folders[index], out var section))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>The folder a 429 is put down to when no limited section holds its URL: its last ("" in none).</summary>
    private static string LastFolder(Uri uri) => FoldersOf(uri) is [.., var last] ? last : string.Empty;

    /// <summary>The path's folders: /collections/belts/products/belt has collections, belts and products; /cart has none.</summary>
    private static List<string> FoldersOf(Uri uri)
    {
        var path = uri.AbsolutePath;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folders = path.EndsWith('/') ? segments.Length : segments.Length - 1;
        return [.. segments.Take(Math.Max(0, folders))];
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
