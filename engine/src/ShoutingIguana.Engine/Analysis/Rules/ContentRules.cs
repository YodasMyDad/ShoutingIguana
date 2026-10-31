using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Thin pages and pages that look like "not found" errors but return 200, on indexable pages.</summary>
internal sealed partial class ContentRules(AnalysisSettings settings) : IPageRule
{
    /// <summary>Real error pages are short; a long article that mentions "404" isn't one.</summary>
    private const int SOFT_404_MAX_WORDS = 300;

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var facts = page.Facts;
        if (facts.WordCount < settings.MinWordCount)
        {
            yield return new IssueHit(IssueCatalog.LOW_WORD_COUNT, page.UrlId, RuleText.Count(facts.WordCount, "word", "words"));
        }

        var h1 = facts.Headings.FirstOrDefault(heading => heading.Level == 1)?.Text;
        if (facts.WordCount < SOFT_404_MAX_WORDS &&
            ((facts.Titles.Count > 0 && NotFoundPhrase().IsMatch(facts.Titles[0])) || (h1 is not null && NotFoundPhrase().IsMatch(h1))))
        {
            yield return new IssueHit(IssueCatalog.SOFT_404, page.UrlId, "The title or H1 says the page wasn't found");
        }
    }

    /// <summary>
    /// Error-page wording. A bare "404" isn't enough ("How to fix a 404"), so it has to lead the title or sit next to
    /// "error", "page" or "not found".
    /// </summary>
    [GeneratedRegex(@"^\s*404\b|\berror\s*404\b|\b404\s*(?:error|page|not found)\b|\bnot found\b|" +
                    @"\bpage (?:does not|doesn['’]t|did not|could not|cannot|can['’]t) (?:exist|be found)\b|" +
                    @"\bno longer (?:exists|available)\b|\bhas been (?:removed|deleted)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotFoundPhrase();
}
