namespace ShoutingIguana.Engine.Export.Models;

/// <summary>A finished export: the file is whole, and at its path.</summary>
/// <param name="Path">The file written.</param>
/// <param name="Rows">Rows of data written, headers apart (the workbook: its checks' rows, the summary sheet apart). 0 for the report.</param>
/// <param name="Bytes">The file's size.</param>
public sealed record ExportResult(string Path, long Rows, long Bytes)
{
    /// <summary>
    /// For an Excel file: the sheets written. A sheet holds 1,048,576 rows, so a grid with more rows than that goes on to
    /// further sheets ("Links (2)"), each under the same header row; this is then more than 1. 0 for the other formats.
    /// </summary>
    public int Sheets { get; init; }

    /// <summary>Columns asked for that are in the file with no values, as the crawl has no URL summary to read them from.</summary>
    public IReadOnlyList<string> UnavailableColumns { get; init; } = [];

    /// <summary>For an Excel file: cells whose text was cut to the 32,767 characters a cell holds.</summary>
    public long TruncatedCells { get; init; }

    /// <summary>
    /// The crawl was running: the file holds the rows the crawl had when the export began, each as it stood when it was
    /// read, and nothing the crawl found later.
    /// </summary>
    public bool CrawlWasRunning { get; init; }
}
