using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Title presence and length on indexable pages. Length is the room a title takes in search results
/// (<see cref="DisplayWidth"/>). Duplicates are checked across pages by <see cref="DuplicateRules"/>.
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
            yield return new IssueHit(IssueCatalog.TITLE_MULTIPLE, page.UrlId, RuleText.Count(titles.Count, "title", "titles"));
        }

        var measured = DisplayWidth.Measure(titles[0]);
        if (measured.Width > settings.TitleMaxLength)
        {
            yield return new IssueHit(IssueCatalog.TITLE_TOO_LONG, page.UrlId, RuleText.Width(measured));
        }
        else if (measured.Width < settings.TitleMinLength)
        {
            yield return new IssueHit(IssueCatalog.TITLE_TOO_SHORT, page.UrlId, RuleText.Width(measured));
        }
    }
}
