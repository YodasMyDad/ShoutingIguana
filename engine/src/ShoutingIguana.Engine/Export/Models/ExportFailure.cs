namespace ShoutingIguana.Engine.Export.Models;

/// <summary>Why an export wrote no file.</summary>
public enum ExportFailure
{
    /// <summary>The path can't be used: it isn't a full path, names a folder, has the wrong extension for the format, or is the crawl itself.</summary>
    InvalidPath,

    /// <summary>The file can't be written there: the folder isn't there, or isn't one this user may write to.</summary>
    NotWritable,

    /// <summary>The disk filled up.</summary>
    DiskFull,

    /// <summary>A file of that name is open in another program, such as Excel, which keeps it from being replaced.</summary>
    FileInUse,

    /// <summary>The crawl changed while it was being exported: analysed again, continued or finished. A pause does not change it.</summary>
    CrawlChanged,

    /// <summary>More rows than an Excel workbook is given sheets for.</summary>
    TooLarge,

    /// <summary>The crawl has never been analysed, and this export is of what an analysis found.</summary>
    NotAnalyzed,

    /// <summary>The engine has no check with the id given.</summary>
    CheckNotFound,

    /// <summary>The last analysis didn't run the check, so it has no rows: not none, unknown.</summary>
    CheckSkipped,

    /// <summary>The crawl has no sitemap with the id given.</summary>
    SitemapNotFound,

    /// <summary>The crawl has no robots.txt for the origin given.</summary>
    OriginNotFound,

    /// <summary>The kind can't be written in the format asked for, or lacks what it needs (a check, a sitemap, an origin).</summary>
    InvalidRequest
}
