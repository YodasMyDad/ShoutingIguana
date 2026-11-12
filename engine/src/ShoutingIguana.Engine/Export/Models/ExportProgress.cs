namespace ShoutingIguana.Engine.Export.Models;

/// <summary>How far an export has got.</summary>
/// <param name="RowsWritten">Rows of data written so far, headers apart.</param>
/// <param name="RowsExpected">
/// The rows the export will write, as counted when it began; null until that's known. A running crawl's is its count at
/// the first page, so the rows written can end a little short of it.
/// </param>
public readonly record struct ExportProgress(long RowsWritten, long? RowsExpected);
