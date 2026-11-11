using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The categories and the reasons a check was skipped, in plain words (docs/v2-ui-simple.md, "Plain words"): the
/// companion of <see cref="IssuePlainTexts"/>, and like it no part of what an analysis stores. A reason with no entry has
/// no plain words yet, and hosts get null for it.
/// </summary>
public static class PlainWords
{
    /// <summary>
    /// By the analysis's reason code (<see cref="SkippedCheckReasons"/>). Today the two the design document writes out;
    /// the rest are written with the checks' words.
    /// </summary>
    private static readonly Dictionary<string, SkipReasonPlainText> SKIP_REASONS = new(StringComparer.Ordinal)
    {
        [SkippedCheckReasons.NO_SITEMAP] = new(
            """Your site has no sitemap, the list of its pages for search engines, to compare with.""",
            """Add a sitemap, then check the site again."""),

        [SkippedCheckReasons.SITE_VERSIONS_UNCHECKED] = new(
            """The site's other addresses (with and without www, http and https) couldn't all be tried.""",
            """Check the site again to include it.""")
    };

    /// <summary>Every skip reason that has plain words, by its code.</summary>
    public static IReadOnlyDictionary<string, SkipReasonPlainText> SkipReasons => SKIP_REASONS;

    /// <summary>The category's title in plain words, such as "Pages that load" for response codes, or null when it has none yet.</summary>
    public static string? CategoryTitle(IssueCategory category) => category switch
    {
        IssueCategory.ResponseCodes => "Pages that load",
        IssueCategory.Redirects => "Forwarded addresses",
        IssueCategory.Links => "Links",
        IssueCategory.Indexability => "Visibility in search",
        IssueCategory.Canonicals => "Duplicate addresses",
        IssueCategory.Sitemaps => "Sitemap",
        IssueCategory.Titles => "Page titles",
        IssueCategory.MetaDescriptions => "Search descriptions",
        IssueCategory.Headings => "Headings",
        IssueCategory.Content => "Content",
        IssueCategory.Images => "Images",
        IssueCategory.Hreflang => "Languages",
        IssueCategory.StructuredData => "Rich results",
        IssueCategory.Security => "Security",
        IssueCategory.Urls => "Web addresses",
        IssueCategory.Performance => "Speed",
        IssueCategory.Html => "Page set-up",
        _ => null
    };

    /// <summary>Why a check was skipped and what would let it run, in plain words, by the reason's code; null when it has none yet.</summary>
    public static SkipReasonPlainText? SkipReason(string code) => SKIP_REASONS.GetValueOrDefault(code);
}
