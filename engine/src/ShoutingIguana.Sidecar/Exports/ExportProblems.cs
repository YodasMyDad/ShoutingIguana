using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Errors.Models;

namespace ShoutingIguana.Sidecar.Exports;

/// <summary>
/// What goes wrong with an export, as the problem the UI keys on: the same problem whether a call is refused with it at
/// once, or an export that had already started fails with it and carries it as its error.
/// </summary>
internal static class ExportProblems
{
    /// <summary>The problem for whatever an export threw; null for a bug, which the caller logs and answers as one.</summary>
    public static ProblemDto? For(Exception exception) => exception switch
    {
        ExportException export => For(export),
        UrlQueryException query => Problems.InvalidQuery(query.Errors).Problem,
        NeedsAnalysisException needs => Problems.NeedsAnalysis(needs.Reason, needs.Columns).Problem,
        _ when EngineErrors.IsExpected(exception) => EngineErrors.ForRead(exception).Problem,
        _ => null
    };

    public static ProblemDto For(ExportException exception) => exception.Failure switch
    {
        ExportFailure.InvalidPath => Problems.InvalidExport("path", exception.Message).Problem,
        ExportFailure.InvalidRequest => Problems.InvalidExport(exception.Field ?? "kind", exception.Message).Problem,
        ExportFailure.NotWritable => new ProblemDto("export-not-writable", exception.Message, StatusCodes.Status400BadRequest),
        ExportFailure.DiskFull => new ProblemDto("export-disk-full", exception.Message, StatusCodes.Status507InsufficientStorage),
        ExportFailure.FileInUse => new ProblemDto("export-file-in-use", exception.Message, StatusCodes.Status409Conflict),
        ExportFailure.CrawlChanged => new ProblemDto("export-crawl-changed", exception.Message, StatusCodes.Status409Conflict),
        ExportFailure.TooLarge => new ProblemDto("export-too-large", exception.Message, StatusCodes.Status422UnprocessableEntity),
        ExportFailure.NotAnalyzed => Problems.NotAnalyzed().Problem,
        ExportFailure.CheckNotFound => new ProblemDto("check-not-found", exception.Message, StatusCodes.Status404NotFound),
        ExportFailure.CheckSkipped => new ProblemDto("check-skipped", exception.Message, StatusCodes.Status409Conflict),
        ExportFailure.SitemapNotFound => Problems.SitemapNotFound().Problem,
        ExportFailure.OriginNotFound => Problems.OriginNotFound().Problem,
        _ => Problems.Internal(exception)
    };
}
