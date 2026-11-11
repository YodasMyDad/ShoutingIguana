using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Whether the site as a whole asks to stay out of search results: more than half of the pages read are noindex, or name
/// another URL as canonical. One fault of the whole site, reported once, on the home page (on the start URL when the crawl
/// read no home page). A noindex page is a notice, as it is often meant; without this, a site whose every page is
/// noindex would pass as clean.
/// <list type="bullet">
/// <item>
/// A page is one the page pass audits, as the analysis counts its pages (<see cref="AnalysisSummary.PagesAnalyzed"/>):
/// an internal HTML page the crawler parsed. One that moves on with an instant meta refresh is read, and never hidden:
/// search engines treat it as a redirect, whatever else it says.
/// </item>
/// <item>More than half is a strict majority, so a site of one page is hidden when that page is.</item>
/// <item>A crawl stopped early is judged on the pages it read, as every page check is.</item>
/// <item>A URL robots.txt kept the crawler out of wasn't read, so it is neither a page nor hidden here.</item>
/// <item>
/// Noindex is counted as the noindex check counts it. A canonical counts where the analysis has the page as
/// canonicalised: not one that leads back to the page or to a broken URL (<see cref="UrlTable"/>). Nor one that names the
/// same page on another version of the site (www or not, http or https): that page can appear in search, at its other
/// address, and the other version answering is the duplicate-site-version check's to report.
/// </item>
/// </list>
/// </summary>
internal sealed class SiteVisibilityRules(UrlTable urls) : ISiteRule
{
    private int _pages;
    private int _noindex;
    private int _canonicalised;

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
        }
        else if (page.IndexabilityReason == IndexabilityReason.Canonicalised && !NamesItselfOnAnotherVersion(page))
        {
            _canonicalised++;
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        var hidden = _noindex + _canonicalised;
        if (urls.HomeId == 0 || hidden * 2L <= _pages)
        {
            yield break;
        }

        // "212 of 265 pages: 200 noindex, 12 canonicalised", a part that is none left out.
        var parts = new[] { (Count: _noindex, What: "noindex"), (Count: _canonicalised, What: "canonicalised") }
            .Where(part => part.Count > 0)
            .Select(part => string.Create(CultureInfo.InvariantCulture, $"{part.Count:N0} {part.What}"));
        yield return new IssueHit(IssueCatalog.SITE_HIDDEN_FROM_SEARCH, urls.HomeId,
            string.Create(CultureInfo.InvariantCulture, $"{hidden:N0} of {RuleText.Count(_pages, "page", "pages")}: {string.Join(", ", parts)}"));
    }

    /// <summary>The canonical that decides the page's indexability (the first Google honours) is the page's own URL on another version of the site.</summary>
    private static bool NamesItselfOnAnotherVersion(AnalyzedPage page) =>
        page.Facts.Canonicals.FirstOrDefault(canonical => canonical.Url is not null && (canonical.InHead || canonical.FromHttpHeader)) is { Url: { } target } &&
        SiteVersions.Of(page.Uri).Contains(target, StringComparer.Ordinal);
}
