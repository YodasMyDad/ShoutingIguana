using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Export.Models;

/// <summary>How an export runs: what it shares with the grids' reads, who hears how far it has got, and its clock.</summary>
public sealed record ExportSettings
{
    /// <summary>
    /// The grids' counts, kept between reads: given the grids' own, an export counts nothing the grid already counted.
    /// Without one, an export keeps its own for as long as it runs.
    /// </summary>
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

    /// <summary>
    /// Told of each file the export is part-way through writing, before it's created and once it's gone, so that whoever
    /// runs exports can clear up after a process that was killed mid-export. Without one, nothing is noted.
    /// </summary>
    public IExportJournal? Journal { get; init; }

    /// <summary>
    /// The rows of data an Excel export may hold: more are refused before anything is written. At most, and unless set,
    /// what the workbook's sheets hold (<see cref="CrawlExporter.MAX_XLSX_ROWS"/>).
    /// </summary>
    public long MaxXlsxRows
    {
        get => _maxXlsxRows;
        init => _maxXlsxRows = Math.Clamp(value, 0, CrawlExporter.MAX_XLSX_ROWS);
    }

    private readonly long _maxXlsxRows = CrawlExporter.MAX_XLSX_ROWS;
}
