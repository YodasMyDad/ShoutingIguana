namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>An analysis's steps, in the order it takes them. Each says what <see cref="AnalysisProgress"/> counts.</summary>
public enum AnalysisPhase
{
    /// <summary>The crawl's URLs, their statuses and what the checks need besides, read into memory. Counts URLs.</summary>
    Reading,

    /// <summary>Every page's checks, in one pass over the pages. Counts pages.</summary>
    PageRules,

    /// <summary>The checks across pages (duplicates, orphans, redirect chains, sitemaps...), once every page is read. Counts rules.</summary>
    SiteRules,

    /// <summary>The URL grids' table, one row per URL. Counts URLs.</summary>
    UrlSummary,

    /// <summary>The results committed and the file closed: one step.</summary>
    Writing
}
