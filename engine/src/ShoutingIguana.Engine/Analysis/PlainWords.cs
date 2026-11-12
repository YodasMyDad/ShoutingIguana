// Written by engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs from plain-words.json beside it.
// Don't edit this file: change the words in the data file and run the generator again. A test holds the two together.
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The categories and the reasons a check was skipped, in plain words (docs/v2-ui-simple.md, "Plain words"): the
/// companion of <see cref="IssuePlainTexts"/>, and like it no part of what an analysis stores. Every category and every
/// reason has its words; a reason with no entry (one a newer engine adds before its words are written) has none, and
/// hosts get null for it. A text may name a threshold as <c>{limit:name}</c>, which <see cref="PlainLimits"/> fills.
/// </summary>
public static class PlainWords
{
    /// <summary>By the analysis's reason code (<see cref="SkippedCheckReasons"/>).</summary>
    private static readonly Dictionary<string, SkipReasonPlainText> SKIP_REASONS = new(StringComparer.Ordinal)
    {
        [SkippedCheckReasons.SITEMAPS_OFF] = new(
            """This site check was set not to read sitemaps: the files that list your pages for search engines.""",
            """Switch sitemaps back on in the check's Advanced settings, then check the site again."""),

        [SkippedCheckReasons.CRAWL_UNFINISHED] = new(
            """The site check stopped before it finished, so it hasn't seen the whole site.""",
            """Continue the site check, and let it run to the end."""),

        [SkippedCheckReasons.SITEMAPS_UNFINISHED] = new(
            """The site check ended before every sitemap had been read.""",
            """Check the site again, and let it run to the end."""),

        [SkippedCheckReasons.NO_SITEMAP] = new(
            """No sitemap, the list of your pages for search engines, could be read, so there was nothing to compare with.""",
            """Add a sitemap, or fix the one that can't be read, then check the site again."""),

        [SkippedCheckReasons.SITEMAP_UNREADABLE] = new(
            """One of your sitemaps couldn't be read, so the check can't tell which pages it lists.""",
            """Fix the sitemap that can't be read (it's listed as a serious problem), then check the site again."""),

        [SkippedCheckReasons.SITEMAPS_NOT_READ] = new(
            """Your site has more sitemaps than Shouting Iguana reads: over 1,000 of them, or lists of sitemaps more than 3 layers deep.""",
            """Leave your site as it is: this version of Shouting Iguana can't run this check on it."""),

        [SkippedCheckReasons.DEPTH_LIMIT] = new(
            """The site check was set to stop a number of clicks from the start page, so it didn't read the whole site.""",
            """Remove the depth limit in the check's Advanced settings, then check the site again."""),

        [SkippedCheckReasons.URL_LIMIT] = new(
            """The site check stopped at its limit of addresses, so it didn't read the whole site.""",
            """Check the site again, and choose "The whole site" when you start it."""),

        [SkippedCheckReasons.START_FOLDER] = new(
            """The site check was kept to one section of the site, so links from the rest of it weren't read.""",
            """Start the check from your home page, so it reads the whole site."""),

        [SkippedCheckReasons.START_NOT_ROOT] = new(
            """This site check started from a page inside your site, not from its home page, so it can't judge the site as a whole.""",
            """Start the check from your home page, then check the site again."""),

        [SkippedCheckReasons.EXTERNAL_LINKS_OFF] = new(
            """This site check was set not to try links to other websites.""",
            """Check the site again, with "Links to other websites" ticked."""),

        [SkippedCheckReasons.RESOURCES_OFF] = new(
            """This site check was set not to load images and other files.""",
            """Check the site again, with "Images and other files" ticked."""),

        [SkippedCheckReasons.ROBOTS_TXT_IGNORED] = new(
            """This site check was set to ignore your site's rules files for search engines, so it can't say what they put off limits or whether each can be read.""",
            """Set the check to follow the rules files again in its Advanced settings, then check the site again."""),

        [SkippedCheckReasons.SITE_VERSIONS_UNCHECKED] = new(
            """The site's other addresses (with and without www, http and https) couldn't all be tried.""",
            """Check the site again to include it."""),

        [SkippedCheckReasons.RULE_FAILED] = new(
            """Shouting Iguana hit an error of its own while working out this result, so there is nothing to show.""",
            """Update the results, and if it happens again, treat it as a fault in Shouting Iguana, not in your site."""),

        [SkippedCheckReasons.EARLIER_VERSION] = new(
            """This site check was made with an older version of Shouting Iguana, which didn't save what this check compares.""",
            """Check the site again to include it."""),

        [SkippedCheckReasons.NEWER_THAN_ANALYSIS] = new(
            """This check was added to Shouting Iguana after these results were worked out.""",
            """Update the results to include it.""")
    };

    /// <summary>Every skip reason that has plain words, by its code.</summary>
    public static IReadOnlyDictionary<string, SkipReasonPlainText> SkipReasons => SKIP_REASONS;

    /// <summary>The category's title in plain words, such as "Pages that load" for response codes, or null when it has none yet.</summary>
    public static string? CategoryTitle(IssueCategory category) => category switch
    {
        IssueCategory.ResponseCodes => """Pages that load""",
        IssueCategory.Redirects => """Forwarding addresses""",
        IssueCategory.Links => """Links""",
        IssueCategory.Indexability => """Visibility in search""",
        IssueCategory.Canonicals => """Duplicate addresses""",
        IssueCategory.Sitemaps => """Sitemap""",
        IssueCategory.Titles => """Page titles""",
        IssueCategory.MetaDescriptions => """Search descriptions""",
        IssueCategory.Headings => """Headings""",
        IssueCategory.Content => """Content""",
        IssueCategory.Images => """Images""",
        IssueCategory.Hreflang => """Languages""",
        IssueCategory.StructuredData => """Extras in search results""",
        IssueCategory.Security => """Security""",
        IssueCategory.Urls => """Web addresses""",
        IssueCategory.Performance => """Speed""",
        IssueCategory.Html => """Page set-up""",
        _ => null
    };

    /// <summary>Why a check was skipped and what would let it run, in plain words, by the reason's code; null when it has none yet.</summary>
    public static SkipReasonPlainText? SkipReason(string code) => SKIP_REASONS.GetValueOrDefault(code);
}
