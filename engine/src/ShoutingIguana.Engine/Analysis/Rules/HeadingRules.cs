using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>H1 presence and count on indexable pages.</summary>
internal sealed class HeadingRules : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var h1s = page.Facts.Headings.Where(heading => heading.Level == 1).ToList();
        if (h1s.All(heading => heading.Text.Length == 0))
        {
            yield return new IssueHit(IssueCatalog.H1_MISSING, page.UrlId, h1s.Count > 0 ? "The H1 has no text" : null);
        }

        if (h1s.Count > 1)
        {
            yield return new IssueHit(IssueCatalog.H1_MULTIPLE, page.UrlId, RuleText.Listed(h1s.Count, "H1", "H1s", h1s.Select(heading => heading.Text)));
        }
    }
}
