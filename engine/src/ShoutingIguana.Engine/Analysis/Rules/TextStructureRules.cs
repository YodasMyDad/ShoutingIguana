using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// How an indexable page's text is built (batch 8 of docs/v2-new-checks.md): heading levels skipped, an H1 that's an
/// image, headings that run on, a long page without an H2, placeholder text, English that's very hard to read, and a page
/// that's mostly its template.
/// </summary>
internal sealed class TextStructureRules(AnalysisSettings settings) : IPageRule
{
    /// <summary>Main-content words of all the page's words below which a page is mostly its template (ours).</summary>
    private const double BOILERPLATE_SHARE = 0.25;

    private const int READABILITY_MIN_WORDS = 100;
    private const int READABILITY_MIN_SENTENCES = 5;
    private const string PLACEHOLDER = "lorem ipsum";

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            yield break;
        }

        var facts = page.Facts;
        var headings = facts.Headings;
        var h1 = headings.FirstOrDefault(heading => heading.Level == 1);
        if (h1 is not null && SkippedLevel(headings) is { } skipped)
        {
            yield return new IssueHit(IssueCatalog.HEADING_LEVELS_SKIPPED, page.UrlId, skipped);
        }

        if (h1 is { FromImageAlt: true })
        {
            yield return new IssueHit(IssueCatalog.H1_IMAGE_ONLY, page.UrlId, RuleText.Fit($"The H1 is an image: {RuleText.Quote(h1.Text, 60)}"));
        }

        var tooLong = headings.Where(heading => heading == h1 || heading.Level == 2)
            .FirstOrDefault(heading => RuleText.Length(heading.Text) > settings.HeadingMaxLength);
        if (tooLong is not null)
        {
            // The heading itself, as much as fits: the one to shorten may be an H2, which nothing else on the row shows.
            var measured = $"H{tooLong.Level} of {RuleText.Over(RuleText.Characters(RuleText.Length(tooLong.Text)), settings.HeadingMaxLength)}";
            yield return new IssueHit(IssueCatalog.HEADING_TOO_LONG, page.UrlId, $"{measured}: {RuleText.Quote(tooLong.Text, RuleText.DETAIL_MAX - measured.Length - 4)}");
        }

        if (facts.WordCount >= settings.H2MinWords && headings.All(heading => heading.Level != 2))
        {
            yield return new IssueHit(IssueCatalog.H2_MISSING, page.UrlId, string.Create(CultureInfo.InvariantCulture, $"{facts.WordCount:N0} words, no H2"));
        }

        var placeholder = facts.PlaceholderText ??
                          facts.Titles.Concat(facts.MetaDescriptions).FirstOrDefault(text => text.Contains(PLACEHOLDER, StringComparison.OrdinalIgnoreCase));
        if (placeholder is not null)
        {
            yield return new IssueHit(IssueCatalog.PLACEHOLDER_TEXT, page.UrlId, RuleText.Quote(placeholder, 60));
        }

        if (ReadingEase(facts) is { } ease && ease < settings.ReadabilityMinScore)
        {
            yield return new IssueHit(IssueCatalog.READABILITY_LOW, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"Reading ease {Math.Round(ease):0}, under {settings.ReadabilityMinScore} (very difficult)"));
        }

        // A page low-word-count flags is that check's: this one is about the share, on a page with words enough.
        if (facts.TotalWordCount is { } total and > 0 && facts.WordCount >= settings.MinWordCount && facts.WordCount < total * BOILERPLATE_SHARE)
        {
            yield return new IssueHit(IssueCatalog.BOILERPLATE_HEAVY, page.UrlId,
                string.Create(CultureInfo.InvariantCulture, $"{facts.WordCount:N0} of {total:N0} words are the page's own ({100.0 * facts.WordCount / total:0}%)"));
        }
    }

    /// <summary>From the first H1 on, the first heading more than one level below the one before it ("H2 → H4").</summary>
    internal static string? SkippedLevel(IReadOnlyList<Parsing.Models.HeadingFact> headings)
    {
        // From the first H1 on: headings before it (a theme's hidden cart, a menu's column titles) skip no level of the
        // page's outline, and the rule cited doesn't forbid them (ruled after the real-site audit).
        var first = -1;
        for (var index = 0; index < headings.Count && first < 0; index++)
        {
            first = headings[index].Level == 1 ? index : -1;
        }

        if (first < 0)
        {
            return null;
        }

        for (var index = first + 1; index < headings.Count; index++)
        {
            if (headings[index].Level > headings[index - 1].Level + 1)
            {
                return $"H{headings[index - 1].Level} → H{headings[index].Level}";
            }
        }

        return null;
    }

    /// <summary>
    /// Flesch's reading ease of an English page's main text (206.835 − 1.015 × words per sentence − 84.6 × syllables per
    /// word), when it's prose enough to judge: 100 words and 5 sentences or more. Null for another language or too little.
    /// </summary>
    internal static double? ReadingEase(Parsing.Models.PageFacts facts)
    {
        var language = facts.Language?.Trim();
        var english = language is not null && (language.Equals("en", StringComparison.OrdinalIgnoreCase) || language.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
        if (!english || facts.WordCount < READABILITY_MIN_WORDS || facts.SentenceCount < READABILITY_MIN_SENTENCES || facts.SyllableCount == 0)
        {
            return null;
        }

        return 206.835 - 1.015 * facts.WordCount / facts.SentenceCount - 84.6 * facts.SyllableCount / facts.WordCount;
    }
}
