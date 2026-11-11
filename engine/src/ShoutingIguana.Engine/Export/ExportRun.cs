using ShoutingIguana.Engine.Export.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>One export as it runs: what was asked, how far it has got, and what it found out about the crawl on the way.</summary>
internal sealed class ExportRun(string crawlPath, ExportRequest request, ExportSettings settings, CancellationToken cancellationToken)
{
    public string CrawlPath => crawlPath;

    public ExportRequest Request => request;

    public ExportSettings Settings => settings;

    public CancellationToken Token => cancellationToken;

    /// <summary>Rows of data written so far.</summary>
    public long Rows { get; private set; }

    /// <summary>The rows it will write, once counted.</summary>
    public long? Expected { get; set; }

    /// <summary>Columns asked for that the crawl has no values for.</summary>
    public IReadOnlyList<string> Unavailable { get; set; } = [];

    /// <summary>The crawl was running when the export began.</summary>
    public bool CrawlWasRunning { get; set; }

    /// <summary>The crawl as the export's first page of rows found it, which every page after must find it too.</summary>
    public Storage.Models.GridExportMoment? Began { get; set; }

    /// <summary>More rows are written: whoever listens hears of it.</summary>
    public void Wrote(long rows)
    {
        Rows += rows;
        settings.Progress?.Report(new ExportProgress(Rows, Expected));
    }

    /// <summary>The count is known, before any row is written.</summary>
    public void Expect(long rows)
    {
        Expected = rows;
        settings.Progress?.Report(new ExportProgress(Rows, Expected));
    }
}
