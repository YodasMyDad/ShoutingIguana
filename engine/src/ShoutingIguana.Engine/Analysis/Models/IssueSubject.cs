namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// What the URLs a check flags are, so a host can say "pages affected, of the pages crawled" only where that's true: a
/// check's count is a part of the pages crawled only when everything it flags is a page.
/// </summary>
public enum IssueSubject
{
    /// <summary>
    /// Every URL the check flags is a page, as every count of pages goes by: an internal URL that answered with HTML the
    /// crawler read and parsed. Its rule reports on the pages the analysis audits, or tests for one.
    /// </summary>
    Page,

    /// <summary>
    /// The check can flag any URL the crawl has: a file, a redirect, a URL that didn't answer or wasn't fetched, a URL on
    /// another host or another version of the site; or it is reported once for the site, on the start URL, which needn't be
    /// a page. Its count is of URLs, and can pass the pages crawled.
    /// </summary>
    Url
}
