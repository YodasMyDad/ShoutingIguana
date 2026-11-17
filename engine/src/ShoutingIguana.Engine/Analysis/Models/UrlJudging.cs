namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// What a check needs of a URL to judge it (<see cref="CheckJudging"/>): so a crawl that no longer flags a URL can say
/// whether it judged the URL at all, which "fixed" needs and "not checked" doesn't.
/// </summary>
public enum UrlJudging
{
    /// <summary>A response-code check: any answer, or none, judges the URL. A 404 that now answers 500 has moved checks.</summary>
    Responses,

    /// <summary>A check of the URL itself (a redirect, a sitemap entry, robots.txt's rule): any status judges it; no answer, or a block, doesn't.</summary>
    Urls,

    /// <summary>A check of a page: only the page, read again, judges it. An error, a redirect or a file in its place doesn't.</summary>
    Pages
}
