namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What a finished export wrote.</summary>
/// <param name="Rows">Rows of data, headers apart; the workbook's are its checks' rows, and the report has none.</param>
/// <param name="Bytes">The file's size.</param>
/// <param name="Sheets">An Excel file's sheets: more than one for a grid of over 1,048,575 rows, which goes on to further sheets. 0 for the other formats.</param>
/// <param name="UnavailableColumns">Columns asked for that are in the file with no values, as the crawl has no URL summary to read them from.</param>
/// <param name="TruncatedCells">An Excel file's cells whose text was cut to the 32,767 characters a cell holds.</param>
/// <param name="CrawlWasRunning">The crawl was running: the file holds what it had when the export began.</param>
internal sealed record ExportResultDto(long Rows, long Bytes, int Sheets, IReadOnlyList<string> UnavailableColumns, long TruncatedCells, bool CrawlWasRunning);
