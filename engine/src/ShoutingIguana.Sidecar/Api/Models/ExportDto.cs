using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Sidecar.Errors.Models;
using ShoutingIguana.Sidecar.Exports.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// An export, as the call that starts it answers, <c>GET /api/exports/{exportId}</c> reads it, and every <c>export</c>
/// event carries it: one shape wherever the UI meets it.
/// </summary>
/// <param name="Path">The file it writes, as the full path the sidecar resolved.</param>
/// <param name="RowsWritten">Rows of data written so far.</param>
/// <param name="RowsExpected">The rows it will write, once counted; null before that, and for the report.</param>
/// <param name="Result">What it wrote, once <see cref="Status"/> is done.</param>
/// <param name="Error">Why it failed, as the problem a call would have been answered with, once <see cref="Status"/> is failed.</param>
internal sealed record ExportDto(
    string ExportId,
    string CrawlId,
    ExportKind Kind,
    ExportFormat Format,
    string Path,
    ExportStatus Status,
    long RowsWritten,
    long? RowsExpected,
    DateTime StartedAt,
    DateTime? FinishedAt,
    ExportResultDto? Result,
    ProblemDto? Error);
