using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// The time one run of a crawl spends crawling, in stretches: one begins when the run starts or resumes after a pause, and
/// ends when it's paused or the run ends, so paused time is in none of them. Each is timed by a clock that only runs
/// forward, in whole milliseconds, which is what the crawl file keeps: the run's time here and the file's are the same
/// number. Safe from any thread.
/// <para>
/// Time the machine spent asleep isn't crawling. On Linux and macOS the clock that only runs forward stops while the
/// machine sleeps; on Windows it runs on (<c>QueryPerformanceCounter</c> counts standby and hibernation). The session
/// reads this clock twice a second while it crawls, so a gap between two readings longer than <see cref="ASLEEP"/> is
/// the machine asleep (or the process held still), on any system, and is left out of the stretch it falls in.
/// </para>
/// </summary>
internal sealed class RunClock(TimeProvider timeProvider)
{
    /// <summary>A gap between two readings of the clock longer than this is not crawling: six hundred times the session's half second.</summary>
    internal static readonly TimeSpan ASLEEP = TimeSpan.FromMinutes(5);

    private readonly Lock _sync = new();
    private DateTimeOffset _startedAt;
    private long _openedAt;
    private long _readAt;
    private TimeSpan _asleep;
    private long _endedMs;
    private int _index = -1;
    private bool _isOpen;

    /// <summary>The time spent crawling in this run so far: its stretches that ended, and the one going now.</summary>
    public long ElapsedMs
    {
        get
        {
            lock (_sync)
            {
                return _endedMs + (_isOpen ? OpenMs() : 0);
            }
        }
    }

    /// <summary>The stretch going now, as long as it is so far; null when none is (the run is paused, or over).</summary>
    public RunStretch? Current
    {
        get
        {
            lock (_sync)
            {
                return _isOpen ? new RunStretch(_index, _startedAt, OpenMs(), Ended: false) : null;
            }
        }
    }

    /// <summary>Begins a stretch, and returns it; null when one is going already.</summary>
    public RunStretch? Begin()
    {
        lock (_sync)
        {
            if (_isOpen)
            {
                return null;
            }

            _isOpen = true;
            _index++;
            _openedAt = _readAt = timeProvider.GetTimestamp();
            _asleep = TimeSpan.Zero;

            // To the millisecond, as the crawl file keeps times.
            _startedAt = DateTimeOffset.FromUnixTimeMilliseconds(timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            return new RunStretch(_index, _startedAt, 0, Ended: false);
        }
    }

    /// <summary>Ends the stretch going now, and returns it as it ended; null when none is.</summary>
    public RunStretch? End()
    {
        lock (_sync)
        {
            if (!_isOpen)
            {
                return null;
            }

            var crawledMs = OpenMs();
            _isOpen = false;
            _endedMs += crawledMs;
            return new RunStretch(_index, _startedAt, crawledMs, Ended: true);
        }
    }

    /// <summary>The stretch going now, so far, less what it spent asleep: each reading notes how long it is since the last.</summary>
    private long OpenMs()
    {
        var now = timeProvider.GetTimestamp();
        var sinceRead = timeProvider.GetElapsedTime(_readAt, now);
        if (sinceRead > ASLEEP)
        {
            _asleep += sinceRead;
        }

        _readAt = now;
        return Math.Max(0, (long)(timeProvider.GetElapsedTime(_openedAt, now) - _asleep).TotalMilliseconds);
    }
}
