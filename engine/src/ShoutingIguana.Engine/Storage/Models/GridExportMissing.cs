namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a grid has no rows to export, where that isn't an empty grid.</summary>
internal enum GridExportMissing
{
    /// <summary>The crawl has never been analysed, so no check has rows.</summary>
    NotAnalyzed,

    /// <summary>The last analysis didn't run the check: it has no rows and no count, which isn't none.</summary>
    CheckSkipped,

    /// <summary>The crawl has no sitemap with the id asked for.</summary>
    SitemapNotFound,

    /// <summary>The crawl isn't as the export's first page found it: analysed again, continued, paused or finished.</summary>
    CrawlChanged
}
