namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// The order a check's rows come in when none is asked for (<c>IssueGroupReader.ImportanceOrder</c>), in its grid, its
/// export, its report examples and Simple's list alike. Only how the check is shown.
/// </summary>
public enum IssueRowOrder
{
    /// <summary>The URLs that matter most to the site first: the most linked page, or the URL the most pages use.</summary>
    Importance,

    /// <summary>
    /// The pages links lead to least first, the fullest of those first: what a fix helps most, where a page's links are
    /// how it's found (a product missing from the sitemap matters more than the policy page every footer links).
    /// </summary>
    FewestLinksFirst
}
