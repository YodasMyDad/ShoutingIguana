using System.Text;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>The report as an export writes it, and as a caller that wants the document itself reads it.</summary>
public static class ReportExport
{
    /// <summary>The report of a crawl's last analysis, as one self-contained HTML document.</summary>
    /// <param name="crawlPath">The crawl file, which is only read.</param>
    /// <param name="outOfDateBy">
    /// The thresholds an analysis would run with now: the report then says when its results no longer stand. Null doesn't
    /// judge.
    /// </param>
    /// <exception cref="ExportException">
    /// <see cref="ExportFailure.NotAnalyzed"/> when the crawl has never been analysed: there is no report of it;
    /// <see cref="ExportFailure.InvalidRequest"/> for a number of examples out of range.
    /// </exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    public static string Build(string crawlPath, ReportOptions? options = null, ExportSettings? settings = null, AnalysisSettings? outOfDateBy = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ReportOptions();
        CrawlExporter.Check(new ExportRequest { Kind = ExportKind.Report, Format = ExportFormat.Html, Report = options });
        var full = options.Cut == ReportCut.Full;
        var model = ReportReader.Read(crawlPath, severity => !full ? 0 : severity == IssueSeverity.Notice ? options.LookExamples : options.Examples, settings ?? new ExportSettings(),
            outOfDateBy, cancellationToken, options);
        return ReportDocument.Write(model, options);
    }

    /// <summary>The policy the document carries in its head, for a host that serves the document and wants to send the same.</summary>
    public static string ContentSecurityPolicy => ReportDocument.ContentSecurityPolicy;

    internal static void Write(ExportRun run, Stream output)
    {
        var document = Build(run.CrawlPath, run.Request.Report, run.Settings, run.Settings.OutOfDateBy, run.Token);
        output.Write(Encoding.UTF8.GetBytes(document));
    }
}
