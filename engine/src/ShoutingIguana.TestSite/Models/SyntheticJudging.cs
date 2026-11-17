namespace ShoutingIguana.TestSite.Models;

/// <summary>
/// What a check needs of a URL to judge it, which decides whether a crawl that no longer flags the URL judged it at all
/// (<see cref="SyntheticSite.GetChanges"/>). The engine's comparison draws the same line from its catalog.
/// </summary>
public enum SyntheticJudging
{
    /// <summary>A response-code check: any answer, or none, judges the URL. A 404 that now answers 500 has moved checks.</summary>
    Responses,

    /// <summary>A check of the URL itself (a redirect, a sitemap entry, robots.txt's rule): any status judges it; no answer, or a block, doesn't.</summary>
    Urls,

    /// <summary>A check of a page: only the page, read again, judges it. An error, a redirect or a file in its place doesn't.</summary>
    Pages
}
