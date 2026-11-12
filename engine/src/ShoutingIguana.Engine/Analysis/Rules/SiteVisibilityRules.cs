using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Whether the site as a whole asks to stay out of search results: its home page is noindex, and so are more than half
/// of the pages that stand for themselves. One fault of the whole site, reported once, on the home page. A noindex page
/// is a notice, as it is often meant; without this, a site whose every page is noindex would pass as clean.
/// <list type="bullet">
/// <item>
/// The home page is the page the start URL ends on, after its redirects (<see cref="UrlTable.HomeId"/>), and it must be
/// noindex itself, as the noindex check counts it. A shop that keeps its many filtered listings out of search and its
/// home page in isn't hiding itself, however many those listings are. Where the crawl read no home page (the start URL
/// failed, or was refused, and a sitemap led to the rest) nothing says the site hides itself, and nothing is reported.
/// </item>
/// <item>
/// A page is one the page pass audits, as the analysis counts its pages (<see cref="AnalysisSummary.PagesAnalyzed"/>):
/// an internal HTML page the crawler parsed. One that moves on with an instant meta refresh is read, and never hidden:
/// search engines treat it as a redirect, whatever else it says.
/// </item>
/// <item>
/// A page whose canonical names another URL is a variant of that page, not a page that stands for itself: it counts
/// neither as hidden nor in the total. So a shop whose crawl reads more filtered and sorted listings than main pages
/// isn't hidden while its main pages can be indexed, and is when most of those are noindex.
/// </item>
/// <item>
/// Noindex is counted as the noindex check counts it, so the first number of the detail is that check's count. A noindex
/// page is noindex whatever its canonical says: the noindex wins, as it does for the page's indexability.
/// </item>
/// <item>
/// A canonical makes a variant where the analysis has the page as canonicalised: not one that leads back to the page or
/// to a broken URL (<see cref="UrlTable"/>). Nor one that names the same page on another version of the site (www or
/// not, http or https): that page stands for itself, at its other address, and the other version answering is the
/// duplicate-site-version check's to report.
/// </item>
/// <item>More than half is a strict majority, so a site of one page is hidden when that page is.</item>
/// <item>With every page a variant there is no page to judge, and nothing is reported.</item>
/// <item>A crawl stopped early is judged on the pages it read, as every page check is.</item>
/// <item>A URL robots.txt kept the crawler out of wasn't read, so it is neither a page nor hidden here.</item>
/// </list>
/// </summary>
internal sealed class SiteVisibilityRules(UrlTable urls) : ISiteRule
{
    private int _pages;
    private int _noindex;
    private int _variants;
    private bool _homeIsNoindex;

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.SITE_HIDDEN_FROM_SEARCH];

    public void Observe(AnalyzedPage page)
    {
        _pages++;
        if (page.IsRedirect)
        {
            return;
        }

        if (page.Facts.Robots.NoIndex)
        {
            _noindex++;
            _homeIsNoindex |= page.UrlId == urls.HomeId;
        }
        else if (page.IndexabilityReason == IndexabilityReason.Canonicalised && !NamesItselfOnAnotherVersion(page))
        {
            _variants++;
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        // The pages that stand for themselves. A noindex page is always one of them, so with none there is none noindex.
        // A home page seen noindex is a page that was read, so there is a home page to report on.
        var standing = _pages - _variants;
        if (!_homeIsNoindex || _noindex * 2L <= standing)
        {
            yield break;
        }

        // "212 of 240 pages are noindex"
        yield return new IssueHit(IssueCatalog.SITE_HIDDEN_FROM_SEARCH, urls.HomeId,
            string.Create(CultureInfo.InvariantCulture, $"{_noindex:N0} of {RuleText.Count(standing, "page", "pages")} {(standing == 1 ? "is" : "are")} noindex"));
    }

    /// <summary>The canonical that decides the page's indexability (the first Google honours) is the page's own URL on another version of the site.</summary>
    private static bool NamesItselfOnAnotherVersion(AnalyzedPage page) =>
        page.Facts.Canonicals.FirstOrDefault(canonical => canonical.Url is not null && (canonical.InHead || canonical.FromHttpHeader)) is { Url: { } target } &&
        SiteVersions.Of(page.Uri).Contains(target, StringComparer.Ordinal);
}
