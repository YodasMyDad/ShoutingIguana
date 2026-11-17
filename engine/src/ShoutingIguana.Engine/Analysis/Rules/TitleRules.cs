using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Title presence and width on indexable pages: how wide the title is in Google's desktop results, in pixels
/// (<see cref="SearchResultWidth"/>). Duplicates are checked across pages by <see cref="DuplicateRules"/>.
/// </summary>
internal sealed class TitleRules(AnalysisSettings settings) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var titles = page.Facts.Titles;
        if (titles.Count == 0 || titles[0].Length == 0)
        {
            yield return new IssueHit(IssueCatalog.TITLE_MISSING, page.UrlId);
            yield break;
        }

        if (titles.Count > 1)
        {
            yield return new IssueHit(IssueCatalog.TITLE_MULTIPLE, page.UrlId, RuleText.Listed(titles.Count, "title", "titles", titles));
        }

        var measured = SearchResultWidth.Measure(titles[0], SearchResultText.Title);
        if (measured.Pixels > settings.TitleMaxPixels)
        {
            yield return new IssueHit(IssueCatalog.TITLE_TOO_LONG, page.UrlId,
                RuleText.WithCharacters(RuleText.Over(RuleText.Pixels(measured.Pixels), settings.TitleMaxPixels), measured.Characters));
        }
        else if (measured.Pixels < settings.TitleMinPixels)
        {
            yield return new IssueHit(IssueCatalog.TITLE_TOO_SHORT, page.UrlId,
                RuleText.WithCharacters(RuleText.Under(RuleText.Pixels(measured.Pixels), settings.TitleMinPixels), measured.Characters));
        }
    }
}
