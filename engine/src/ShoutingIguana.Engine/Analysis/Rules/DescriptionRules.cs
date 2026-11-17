using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Meta description presence and width on indexable pages, in pixels as Google's desktop results show it (<see cref="SearchResultWidth"/>).</summary>
internal sealed class DescriptionRules(AnalysisSettings settings) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var descriptions = page.Facts.MetaDescriptions;
        if (descriptions.Count == 0 || descriptions[0].Length == 0)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_MISSING, page.UrlId);
            yield break;
        }

        if (descriptions.Count > 1)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_MULTIPLE, page.UrlId,
                RuleText.Listed(descriptions.Count, "meta description", "meta descriptions", descriptions));
        }

        var measured = SearchResultWidth.Measure(descriptions[0], SearchResultText.Description);
        if (measured.Pixels > settings.DescriptionMaxPixels)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_TOO_LONG, page.UrlId,
                RuleText.WithCharacters(RuleText.Over(RuleText.Pixels(measured.Pixels), settings.DescriptionMaxPixels), measured.Characters));
        }
        else if (measured.Pixels < settings.DescriptionMinPixels)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_TOO_SHORT, page.UrlId,
                RuleText.WithCharacters(RuleText.Under(RuleText.Pixels(measured.Pixels), settings.DescriptionMinPixels), measured.Characters));
        }
    }
}
