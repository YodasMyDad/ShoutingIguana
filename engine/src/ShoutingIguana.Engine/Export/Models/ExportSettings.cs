using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Export.Models;

/// <summary>How an export runs: what it shares with the grids' reads, who hears how far it has got, and its clock.</summary>
public sealed record ExportSettings
{
    /// <summary>The grids' counts, kept between reads: an export of a grid counts what the grid already counted once.</summary>
    public ReadCache? Cache { get; init; }

    /// <summary>
    /// Told how far the export has got, on the export's own thread, after each page of rows: at most a few thousand rows
    /// apart, so whoever passes it on decides how often.
    /// </summary>
    public IProgress<ExportProgress>? Progress { get; init; }

    /// <summary>What time it is, and in which time zone a date is written: a report says when it was made, in its reader's own time.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// The thresholds an analysis would run with now. Given them, a report says when its results no longer stand (the
    /// crawl went on, or the checks changed); without them it doesn't judge.
    /// </summary>
    public Analysis.Models.AnalysisSettings? OutOfDateBy { get; init; }
}
