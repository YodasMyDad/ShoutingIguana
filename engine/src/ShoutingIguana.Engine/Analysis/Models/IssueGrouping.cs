namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// How a check's issues group on the Issues screen, so a fault shows once rather than once per page. Each issue has a
/// short detail and maybe another URL involved; which of them names the fault is the check's to say.
/// </summary>
public enum IssueGrouping
{
    /// <summary>Not grouped: the detail is a measurement ("78 characters", "1,250 ms", "3 images"), or the same on every issue.</summary>
    None,

    /// <summary>
    /// By detail and other URL, the detail naming a fault in the page's own HTML ("Missing og:image"): a group most of the
    /// check's pages are in is likely the site's template.
    /// </summary>
    PageDetail,

    /// <summary>
    /// By detail and other URL, the detail saying how the URL itself answered ("404 Not Found", "Timed out"): however big a
    /// group, it says nothing of a template.
    /// </summary>
    ResponseDetail,

    /// <summary>
    /// By the other URL alone, the detail being a count ("3 broken URLs"): the same other URL on many pages (a link, a
    /// canonical, an image) is one fault.
    /// </summary>
    RelatedUrl
}
