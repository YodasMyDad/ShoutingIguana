namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The pace of one section of a host that limits it apart from the rest (a shop's product pages, say), learned from its
/// 429s and from what got through. Its pages are asked one at a time, an interval apart: seeded from the first
/// Retry-After, doubled at each 429 after that (up to <see cref="MAX_INTERVAL"/>, and never sooner than the Retry-After
/// for the next page), and a tenth shorter each time a page gets through, so it settles near the pace the section
/// allows. Thread-safe.
/// </summary>
internal sealed class SectionPace(string folder)
{
    /// <summary>The first interval when the 429 didn't say how long to wait.</summary>
    public static readonly TimeSpan DEFAULT_INTERVAL = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The slowest a section is ever asked: one page every 20 minutes, as long as a section may go on after the rest of the
    /// crawl. A limit whose window each refused try renews (a shop's product pages, shut for five or ten minutes at a time)
    /// only opens after a long quiet spell, so the gap keeps doubling (one, two, four, eight, sixteen minutes) rather than
    /// stopping where tries still keep it shut.
    /// </summary>
    public static readonly TimeSpan MAX_INTERVAL = TimeSpan.FromMinutes(20);

    private const double FASTER = 0.9;

    private readonly Lock _sync = new();
    private TimeSpan _interval;
    private DateTimeOffset _nextDue = DateTimeOffset.MinValue;
    private DateTimeOffset _lastThrottle = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastTurn;
    private int _failuresInARow;

    // 429s in a row for pages tried at the slowest pace already.
    private int _failuresAtSlowest;

    /// <summary>The folder that names the section: "products" for /products/ and /collections/belts/products/.</summary>
    public string Folder => folder;

    /// <summary>How the section is named to people: "/products/".</summary>
    public string Path => folder.Length == 0 ? "/" : $"/{folder}/";

    /// <summary>The time between the section's pages now.</summary>
    public TimeSpan Interval
    {
        get
        {
            lock (_sync)
            {
                return _interval;
            }
        }
    }

    /// <summary>429s in a row, with no page through between them.</summary>
    public int FailuresInARow
    {
        get
        {
            lock (_sync)
            {
                return _failuresInARow;
            }
        }
    }

    /// <summary>When the section's next page may start.</summary>
    public DateTimeOffset NextDue
    {
        get
        {
            lock (_sync)
            {
                return _nextDue;
            }
        }
    }

    /// <summary>
    /// Several 429s in a row for pages tried at the slowest pace already: the section isn't letting anything through, and
    /// the pages still waiting are given up on.
    /// </summary>
    public bool IsRefusingEverything(int failures)
    {
        lock (_sync)
        {
            return _failuresAtSlowest >= failures;
        }
    }

    /// <summary>A page of the section may start now: if its turn has come, the turn is taken.</summary>
    public bool TryStart(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (now < _nextDue)
            {
                return false;
            }

            _nextDue = now + _interval;
            _lastTurn = now;
            return true;
        }
    }

    /// <summary>
    /// A 429 for a page that started at <paramref name="startedAt"/>. The first starts the interval at its Retry-After. After
    /// that, only a page the section let go at its turn doubles it: one sent before the last 429, or by the crawl before the
    /// section was limited, only waits out what it asks.
    /// </summary>
    public void Throttled(TimeSpan? retryAfter, DateTimeOffset startedAt, DateTimeOffset now)
    {
        var asked = retryAfter is { } wait && wait > TimeSpan.Zero ? (wait < MAX_INTERVAL ? wait : MAX_INTERVAL) : TimeSpan.Zero;
        lock (_sync)
        {
            var isFirst = _interval == TimeSpan.Zero;
            if (!isFirst && (startedAt < _lastThrottle || _lastTurn is not { } turn || startedAt < turn))
            {
                _nextDue = Max(_nextDue, now + asked);
                return;
            }

            if (_interval >= MAX_INTERVAL)
            {
                _failuresAtSlowest++;
            }

            var doubled = _interval == TimeSpan.Zero ? (asked > TimeSpan.Zero ? asked : DEFAULT_INTERVAL) : _interval * 2;
            _interval = doubled < MAX_INTERVAL ? doubled : MAX_INTERVAL;
            _failuresInARow++;
            _lastThrottle = now;
            _nextDue = Max(_nextDue, now + Max(asked, _interval));
        }
    }

    /// <summary>A page of the section got through: the pace it was asked at works, so the next is tried a little sooner.</summary>
    public void GotThrough()
    {
        lock (_sync)
        {
            _failuresInARow = 0;
            _failuresAtSlowest = 0;
            _interval *= FASTER;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
