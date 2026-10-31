using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Meta description presence and length on indexable pages.</summary>
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
                RuleText.Count(descriptions.Count, "meta description", "meta descriptions"));
        }

        var length = RuleText.Length(descriptions[0]);
        if (length > settings.DescriptionMaxLength)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_TOO_LONG, page.UrlId, RuleText.Characters(length));
        }
        else if (length < settings.DescriptionMinLength)
        {
            yield return new IssueHit(IssueCatalog.DESCRIPTION_TOO_SHORT, page.UrlId, RuleText.Characters(length));
        }
    }
}
