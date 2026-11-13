namespace ShoutingIguana.Engine.Export.Models;

/// <summary>What an export writes.</summary>
public enum ExportKind
{
    /// <summary>The URL grid: every URL of the crawl the query matches.</summary>
    Urls,

    /// <summary>The links grid: every link the query matches.</summary>
    Links,

    /// <summary>The URL grid for one sitemap: the URLs it lists.</summary>
    SitemapUrls,

    /// <summary>The URLs robots.txt kept the crawl from on one origin.</summary>
    RobotsBlocked,

    /// <summary>A check's rows as the Issues screen's grid shows them: all of them, or one group's.</summary>
    IssueRows,

    /// <summary>One check's sheet of the workbook as a file of its own: every URL it flagged, with its title, its detail and the other URL involved.</summary>
    Check,

    /// <summary>The spreadsheet of everything: a summary sheet, then a sheet for each check that found something.</summary>
    Workbook,

    /// <summary>The report: one self-contained HTML document.</summary>
    Report,

    /// <summary>The list of sitemaps the crawl looked for, as the Sitemaps screen lists them: each with what became of it and its URLs counted.</summary>
    Sitemaps,

    /// <summary>The origins the crawl asked for a robots.txt, as the robots.txt screen lists them: all, or those a search and a filter match.</summary>
    RobotsOrigins
}
