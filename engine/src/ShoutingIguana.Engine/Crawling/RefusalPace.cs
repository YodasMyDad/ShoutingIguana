using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// How one of the site's hosts is asked once it has started refusing the crawler after some of its pages were read: a
/// firewall's or bot check's answer (<c>FetchOutcome.Blocked</c>), not a 429, which the host's pace and
/// <see cref="SectionTrickle"/> deal with. <see cref="REFUSALS_TO_SLOW"/> refusals in a row, with no page through between
/// them, slow the host down: its pages are asked one at a time, <see cref="START_INTERVAL"/> after the last one ended to
/// start (twice that each time it has to slow down again in a crawl), twice as long after each refusal of a page asked at
/// the pace as it stands, up to <see cref="MAX_INTERVAL"/>, and half as long after every <see cref="THROUGH_TO_EASE"/> pages
/// in a row that get through, until it goes back to its own pace. <see cref="GIVE_UP_REFUSALS"/> refusals in a row at the
/// slowest pace give it up. One refused page among pages that get through is that page's own answer, and changes nothing.
/// The interval only ever adds to the crawl's own pace and the host's gate, never takes from them.
/// <para>Not thread-safe: <see cref="RefusalRetreat"/> calls it under its lock.</para>
/// </summary>
internal sealed class RefusalPace(string host)
{
    /// <summary>Refusals in a row, after a page of the host was read, that slow it down.</summary>
    public const int REFUSALS_TO_SLOW = 3;

    /// <summary>Pages through in a row, while it is slowed down, that halve the interval.</summary>
    public const int THROUGH_TO_EASE = 5;

    /// <summary>Refusals in a row at <see cref="MAX_INTERVAL"/> that give the host up.</summary>
    public const int GIVE_UP_REFUSALS = 3;

    /// <summary>
    /// The time between the end of one page and the start of the next when a host first slows down: about half the Polite
    /// speed's pace (two at once, two a second), one at a time.
    /// </summary>
    public static readonly TimeSpan START_INTERVAL = TimeSpan.FromSeconds(1);

    /// <summary>The slowest a host that refuses is asked: one page every 30 seconds.</summary>
    public static readonly TimeSpan MAX_INTERVAL = TimeSpan.FromSeconds(30);

    private int _refusedInARow;
    private int _throughInARow;
    private int _refusedAtSlowest;

    /// <summary>The host as progress names it.</summary>
    public string Host => host;

    /// <summary>One page at a time while it is slowed down.</summary>
    public SemaphoreSlim Turn { get; } = new(1, 1);

    /// <summary>The host's pages that got an answer of the site's own (not a refusal, not a 429).</summary>
    public int Read { get; private set; }

    /// <summary>Its pages are asked one at a time, <see cref="Interval"/> apart.</summary>
    public bool IsSlow { get; private set; }

    /// <summary>It refused every page at the slowest pace: its refusals are recorded as they come.</summary>
    public bool GaveUp { get; private set; }

    /// <summary>The time between the end of one page and the start of the next while it is slowed down.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>The longest <see cref="Interval"/> it has had in the crawl.</summary>
    public TimeSpan Slowest { get; private set; }

    /// <summary>How many times it has slowed down in the crawl.</summary>
    public int Slowdowns { get; private set; }

    /// <summary>Changes with each change of pace: a refusal of a page asked at another pace says nothing of this one.</summary>
    public int Generation { get; private set; }

    /// <summary>When its last page, asked while it was slowed down, ended.</summary>
    public DateTimeOffset LastEnd { get; set; } = DateTimeOffset.MinValue;

    /// <summary>Its refused URLs waiting to be asked again.</summary>
    public int Waiting { get; set; }

    /// <summary>What the browser had done about the host at its last refusal that it had done anything about.</summary>
    public FallbackVerdict Verdict { get; set; }

    /// <summary>Its refused URLs asked again, each counted once.</summary>
    public int Retried { get; set; }

    /// <summary>
    /// A page of the host was refused, asked at the pace of <paramref name="generation"/>. Returns true when this slows it
    /// down. A host none of whose pages has been read isn't slowed: it is refusing the crawler, not its pace.
    /// </summary>
    public bool Refused(int generation)
    {
        _throughInARow = 0;
        if (GaveUp || Read == 0)
        {
            return false;
        }

        if (!IsSlow)
        {
            if (++_refusedInARow < REFUSALS_TO_SLOW)
            {
                return false;
            }

            Slow();
            return true;
        }

        if (generation != Generation)
        {
            return false;
        }

        if (Interval >= MAX_INTERVAL)
        {
            if (++_refusedAtSlowest >= GIVE_UP_REFUSALS)
            {
                GiveUp();
            }

            return false;
        }

        Interval = Interval * 2 < MAX_INTERVAL ? Interval * 2 : MAX_INTERVAL;
        Slowest = Interval > Slowest ? Interval : Slowest;
        Generation++;
        return false;
    }

    /// <summary>
    /// A page of the host got an answer of the site's own. While it is slowed down, every <see cref="THROUGH_TO_EASE"/> in
    /// a row halve the interval; once it is back at the start and none of its refused URLs is still waiting, it goes back
    /// to its own pace.
    /// </summary>
    public void GotThrough()
    {
        Read++;
        _refusedInARow = 0;
        _refusedAtSlowest = 0;
        if (!IsSlow || ++_throughInARow < THROUGH_TO_EASE)
        {
            return;
        }

        _throughInARow = 0;
        if (Interval > START_INTERVAL)
        {
            Interval = Interval / 2 > START_INTERVAL ? Interval / 2 : START_INTERVAL;
            Generation++;
        }
        else if (Waiting == 0)
        {
            IsSlow = false;
            Generation++;
        }
    }

    /// <summary>Gives it up: what it refuses is recorded as it comes, at its own pace.</summary>
    public void GiveUp()
    {
        GaveUp = true;
        IsSlow = false;
        Generation++;
    }

    private void Slow()
    {
        var interval = START_INTERVAL * Math.Pow(2, Math.Min(Slowdowns, 5));
        Interval = interval < MAX_INTERVAL ? interval : MAX_INTERVAL;
        Slowest = Interval > Slowest ? Interval : Slowest;
        Slowdowns++;
        IsSlow = true;
        _refusedInARow = 0;
        _refusedAtSlowest = 0;
        Generation++;
    }
}
