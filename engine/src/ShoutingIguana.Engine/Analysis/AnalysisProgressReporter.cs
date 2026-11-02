using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Reports an analysis's progress at a pace a UI can show: each phase as it starts and as it ends, and at most
/// <see cref="INTERVAL"/> apart between. Called on the analysis's own thread, so reports arrive in order, and a report
/// that isn't due costs a clock read. Without a listener it does nothing.
/// </summary>
internal sealed class AnalysisProgressReporter(IProgress<AnalysisProgress>? progress, TimeProvider timeProvider)
{
    public static readonly TimeSpan INTERVAL = TimeSpan.FromMilliseconds(250);

    private AnalysisPhase _phase;
    private long _total;
    private long _done;
    private long _lastReport;

    public void Start(AnalysisPhase phase, long total)
    {
        _phase = phase;
        _total = total;
        _done = 0;
        Report(0);
    }

    /// <summary>The phase has done <paramref name="done"/>: reported if the last report was long enough ago.</summary>
    public void Advance(long done)
    {
        _done = done;
        if (progress is not null && timeProvider.GetElapsedTime(_lastReport) >= INTERVAL)
        {
            Report(done);
        }
    }

    /// <summary>
    /// The phase is over, having done <paramref name="done"/>: that is its total now. One counted ahead of time (the
    /// pages, say) can turn out a little different in a file another program changed.
    /// </summary>
    public void Finish(long done)
    {
        _total = done;
        Report(done);
    }

    /// <summary>The phase is over, having done as much as it last advanced to.</summary>
    public void Finish() => Finish(_done);

    private void Report(long done)
    {
        if (progress is null)
        {
            return;
        }

        _lastReport = timeProvider.GetTimestamp();
        progress.Report(new AnalysisProgress(_phase, Math.Min(done, _total), _total));
    }
}
