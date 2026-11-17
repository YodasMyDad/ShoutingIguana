using System.Globalization;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The pace one host is asked at, learned from how it answers; <see cref="HostGate"/> keeps to it. The crawl's settings
/// are the ceiling, never the starting pace.
/// <list type="bullet">
/// <item>Slow start: one request at a time, at <see cref="START_RATE"/> a second. Each healthy answer doubles the rate and
/// allows one more request at once, up to the ceiling, so a healthy site is at full speed within about a second. Answers
/// that come back much slower than the host's first ones hold the pace where it is, for a few answers.</item>
/// <item>A host that asks to slow down (429, or 503 with Retry-After) is paused for its Retry-After (or
/// <see cref="DEFAULT_PAUSE"/>, doubling while it keeps asking, up to <see cref="MAX_PAUSE"/>), then resumes one request at
/// a time at half the pace that tripped it, so nothing queued bursts out after the pause. Answers to requests sent
/// before that slowdown don't slow it again: they were asked at the old pace.</item>
/// <item>Failures (502, 504, and 503 without Retry-After) slow it the same way once two come in a row, without a pause, at
/// most once every <see cref="RECOVERY_STEP"/>, and never below <see cref="START_RATE"/>. No answer at all, and a
/// firewall's block page, say nothing about pace.</item>
/// <item>It recovers additively: every <see cref="RECOVERY_STEP"/> without a slowdown, a tenth of the pace it slowed from,
/// and one more request at once; and for <see cref="TRIP_MEMORY"/> after a host asked to slow down, never above nine
/// tenths of the pace that tripped it.</item>
/// </list>
/// Not thread-safe: the gate calls it under its lock, with the time.
/// </summary>
internal sealed class HostPace
{
    /// <summary>Requests a second at the start of a crawl, and the floor of a pace slowed by failures.</summary>
    public const double START_RATE = 1;

    /// <summary>The slowest pace a host is ever asked at: one request every ten seconds.</summary>
    public const double MIN_RATE = 0.1;

    /// <summary>A learned pace above this many requests a second, with no rate set, is no limit at all.</summary>
    private const double UNCAPPED_ABOVE = 256;

    private const double SLOWDOWN = 0.5;
    private const double RECOVERY_SHARE = 0.1;
    private const double TRIP_CEILING_SHARE = 0.9;
    private const int FAILURES_BEFORE_SLOWING = 2;

    /// <summary>The host's first answers set what it normally takes to answer.</summary>
    private const int BASELINE_ANSWERS = 5;

    private const double LATENCY_SMOOTHING = 0.2;

    /// <summary>
    /// Slow answers in a row that hold the pace before they become the host's usual time: a host whose pages take longer
    /// than its robots.txt, or a busy machine, mustn't hold a crawl back for good. Explicit signals still slow it.
    /// </summary>
    private const int HOLD_ANSWERS = 8;
    private const int STARTS_MEASURED = 32;

    public static readonly TimeSpan RECOVERY_STEP = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TRIP_MEMORY = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DEFAULT_PAUSE = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MAX_PAUSE = TimeSpan.FromMinutes(2);

    /// <summary>Answers this much slower than twice the host's usual time to answer hold the pace.</summary>
    private static readonly TimeSpan LATENCY_SLACK = TimeSpan.FromMilliseconds(500);

    private readonly int _maxConcurrency;
    private readonly double? _maxRate;
    private readonly DateTimeOffset[] _starts = new DateTimeOffset[STARTS_MEASURED];
    private long _startCount;
    private double? _rate;
    private bool _slowStart = true;
    private double _reference;
    private double _tripPace;
    private DateTimeOffset _lastSlowdown = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTrip = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStep = DateTimeOffset.MinValue;
    private int _failuresInARow;
    private int _tripsInARow;
    private int _baselineAnswers;
    private double _baselineMs;
    private double _smoothedMs;
    private int _slowAnswers;

    /// <param name="maxConcurrency">The ceiling on requests at once: the crawl's setting.</param>
    /// <param name="maxRate">The ceiling on requests a second, or null for none: the crawl's setting.</param>
    public HostPace(int maxConcurrency, double? maxRate)
    {
        _maxConcurrency = maxConcurrency;
        _maxRate = maxRate;
        _rate = Math.Min(START_RATE, maxRate ?? START_RATE);
    }

    /// <summary>Requests allowed at once now.</summary>
    public int Window { get; private set; } = 1;

    /// <summary>Requests a second allowed now, or null for no limit.</summary>
    public double? Rate => _rate;

    /// <summary>The time between request starts the pace asks for now.</summary>
    public TimeSpan Interval => _rate is { } rate ? TimeSpan.FromSeconds(1 / rate) : TimeSpan.Zero;

    /// <summary>
    /// The time between request starts when the host's answers have slowed it below the crawl's settings (it asked to
    /// slow down, or kept failing); null while it hasn't, or has recovered, and during the slow start.
    /// </summary>
    public TimeSpan? SlowedTo => !_slowStart && _rate is { } rate && (_maxRate is not { } max || rate < max) ? Interval : null;

    /// <summary>A pace in words: "2 requests a second", "0.5 requests a second", "one request every 10 seconds".</summary>
    public static string DescribePace(TimeSpan interval)
    {
        var perSecond = 1 / interval.TotalSeconds;
        return perSecond switch
        {
            >= 0.95 and < 1.05 => "1 request a second",
            >= 0.5 => string.Create(CultureInfo.InvariantCulture, $"{perSecond:0.#} requests a second"),
            _ => $"one request every {DescribeWait(interval)}"
        };
    }

    /// <summary>A wait in words, to the second: "60 seconds", "2 minutes", "1 second".</summary>
    public static string DescribeWait(TimeSpan wait)
    {
        var seconds = (int)Math.Round(wait.TotalSeconds);
        return seconds switch
        {
            <= 1 => "1 second",
            < 120 => $"{seconds} seconds",
            _ when seconds % 60 == 0 => $"{seconds / 60} minutes",
            _ => $"{seconds / 60} min {seconds % 60} s"
        };
    }

    /// <summary>A request starts now: the pace that trips a host is measured from these.</summary>
    public void Started(DateTimeOffset now) => _starts[_startCount++ % STARTS_MEASURED] = now;

    /// <summary>
    /// How the host answered a request that started at <paramref name="startedAt"/>. Returns how long to pause the whole
    /// host from now, or zero.
    /// </summary>
    /// <param name="latency">Its time to first byte, for answers that came: rising latency holds the pace.</param>
    public TimeSpan Answered(HostSignal signal, TimeSpan? retryAfter, DateTimeOffset startedAt, DateTimeOffset now, TimeSpan? latency = null)
    {
        var asked = retryAfter is { } wait && wait > TimeSpan.Zero ? (wait < MAX_PAUSE ? wait : MAX_PAUSE) : (TimeSpan?)null;
        var sentAtThisPace = startedAt >= _lastSlowdown;
        switch (signal)
        {
            case HostSignal.Throttled:
                _failuresInARow = 0;
                if (!sentAtThisPace)
                {
                    // Sent at the pace already slowed from: it asks again for what was asked for, and is waited out.
                    if (asked is { } longer && now + longer > _lastStep)
                    {
                        _lastStep = now + longer;
                    }

                    return asked ?? TimeSpan.Zero;
                }

                _tripsInARow++;
                _tripPace = SlowDown(now, MIN_RATE);
                _lastTrip = now;

                // Recovery is counted from the end of the pause: time spent waiting isn't time the host answered well.
                var pause = asked ?? DefaultPause();
                _lastStep = now + pause;
                return pause;
            case HostSignal.Failed:
                if (++_failuresInARow >= FAILURES_BEFORE_SLOWING && sentAtThisPace && now - _lastSlowdown >= RECOVERY_STEP)
                {
                    SlowDown(now, START_RATE);
                    _failuresInARow = 0;
                }

                return TimeSpan.Zero;
            case HostSignal.Refused:
                // A firewall's answer says who the crawler is, not how fast it goes.
                _failuresInARow = 0;
                return TimeSpan.Zero;
            case HostSignal.NoAnswer:
                return TimeSpan.Zero;
            default:
                _failuresInARow = 0;
                if (latency is { } answeredIn && IsSlowerThanUsual(answeredIn))
                {
                    return TimeSpan.Zero;
                }

                SpeedUp(now);
                return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Halves the pace the host was asked at (what was allowed, or what was actually sent if that was less), not below
    /// <paramref name="floor"/> unless it was already, one request at a time. Returns the pace it slowed from.
    /// </summary>
    private double SlowDown(DateTimeOffset now, double floor)
    {
        var allowed = _rate ?? UNCAPPED_ABOVE;
        var from = Math.Min(allowed, MeasuredRate() ?? double.MaxValue);

        // Still recovering from an earlier slowdown, the steps back stay a share of the pace before it.
        if (_slowStart || allowed >= _reference)
        {
            _reference = from;
        }

        _rate = Math.Min(allowed, Math.Max(floor, from * SLOWDOWN));
        _slowStart = false;
        Window = 1;
        _lastSlowdown = now;
        _lastStep = now;
        return from;
    }

    private void SpeedUp(DateTimeOffset now)
    {
        if (_slowStart)
        {
            Window = Math.Min(_maxConcurrency, Window + 1);
            _rate = Capped(_rate is { } rate ? rate * 2 : null);
            return;
        }

        if (now - _lastStep < RECOVERY_STEP)
        {
            return;
        }

        _lastStep = now;
        _tripsInARow = 0;
        Window = Math.Min(_maxConcurrency, Window + 1);
        var faster = _rate is { } current ? current + RECOVERY_SHARE * _reference : (double?)null;
        if (faster is { } next && now - _lastTrip < TRIP_MEMORY)
        {
            faster = Math.Max(_rate ?? 0, Math.Min(next, TRIP_CEILING_SHARE * _tripPace));
        }

        _rate = Capped(faster);
    }

    /// <summary>A pace held to the crawl's own rate; with none, past <see cref="UNCAPPED_ABOVE"/> it is no limit.</summary>
    private double? Capped(double? rate) => rate switch
    {
        null => _maxRate,
        { } value when _maxRate is { } max => Math.Min(value, max),
        > UNCAPPED_ABOVE => null,
        var value => value
    };

    /// <summary>Requests a second actually started, over the last <see cref="STARTS_MEASURED"/>; null with too few to tell.</summary>
    private double? MeasuredRate()
    {
        var count = (int)Math.Min(_startCount, STARTS_MEASURED);
        if (count < 2)
        {
            return null;
        }

        var newest = _starts[(_startCount - 1) % STARTS_MEASURED];
        var oldest = _starts[(_startCount - count) % STARTS_MEASURED];
        var seconds = (newest - oldest).TotalSeconds;
        return seconds > 0 ? (count - 1) / seconds : null;
    }

    /// <summary>
    /// The first answers set the host's usual time to answer; after that, answers that take on average well over twice
    /// as long (a server slowing under the load) hold the pace, for <see cref="HOLD_ANSWERS"/> answers at most: then that
    /// is the host's usual time, and the pace grows again until answers slow again.
    /// </summary>
    private bool IsSlowerThanUsual(TimeSpan latency)
    {
        var milliseconds = latency.TotalMilliseconds;
        if (_baselineAnswers < BASELINE_ANSWERS)
        {
            _baselineAnswers++;
            _baselineMs += (milliseconds - _baselineMs) / _baselineAnswers;
            _smoothedMs = _baselineMs;
            return false;
        }

        _smoothedMs += LATENCY_SMOOTHING * (milliseconds - _smoothedMs);
        if (_smoothedMs <= 2 * _baselineMs + LATENCY_SLACK.TotalMilliseconds)
        {
            _slowAnswers = 0;
            return false;
        }

        if (++_slowAnswers < HOLD_ANSWERS)
        {
            return true;
        }

        _baselineMs = _smoothedMs;
        _slowAnswers = 0;
        return false;
    }

    /// <summary>A 429 without Retry-After: ten seconds, doubling while the host keeps asking, up to two minutes.</summary>
    private TimeSpan DefaultPause()
    {
        var pause = DEFAULT_PAUSE * Math.Pow(2, Math.Min(_tripsInARow - 1, 8));
        return pause < MAX_PAUSE ? pause : MAX_PAUSE;
    }
}
