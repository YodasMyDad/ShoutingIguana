namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a crawl made of a URL, as far as judging it goes (<see cref="CheckJudging"/>). Stored as its number in SQL reads.</summary>
public enum UrlReading
{
    /// <summary>The crawl doesn't have it, or a recheck let it go: nothing leads to it any more.</summary>
    NotFound = 0,

    /// <summary>
    /// Found and never asked for, or asked for without an answer that says anything: skipped (a limit, nofollow, found by
    /// a recheck), still queued, refused by robots.txt, or not checked (the host had stopped answering, kept asking the
    /// crawler to slow down, or is on a private address).
    /// </summary>
    NotRead = 1,

    /// <summary>A page of the site the analysis audits (<c>CrawlSchema.PAGE</c>).</summary>
    Page = 2,

    /// <summary>Any other status: a redirect, an error, a file, a page on another site.</summary>
    Answered = 3,

    /// <summary>No answer (a timeout, a failed connection), a block page in place of one, or a response the crawler failed to process.</summary>
    Failed = 4
}
