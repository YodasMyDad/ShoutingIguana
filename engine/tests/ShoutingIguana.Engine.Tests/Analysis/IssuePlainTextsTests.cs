using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The plain words, held to the design's template and rules (docs/v2-ui-simple.md, "Plain words"): the shape of each
/// text, its limits, and the words that are never bare. Whoever writes the rest of the checks' words adds entries to
/// <see cref="IssuePlainTexts"/>; these tests judge each one as it arrives.
/// </summary>
public sealed partial class IssuePlainTextsTests
{
    private const int TITLE_MAX_CHARACTERS = 60;
    private const int PASSED_MAX_CHARACTERS = 60;
    private const int FIRST_SENTENCE_MAX_CHARACTERS = 120;
    private const int WHAT_MAX_WORDS = 70;
    private const int SENTENCE_MAX_WORDS = 30;
    private const int STEP_MAX_WORDS = 25;

    /// <summary>
    /// The design's "never bare" list. A text may use one only where the check's words first give it in quotation marks,
    /// as the term to search for or to brief a developer with: the short text under a page's title (a "meta description").
    /// </summary>
    private static readonly string[] NEVER_BARE =
    [
        "canonical", "indexable", "crawl", "crawler", "status code", "3xx", "4xx", "5xx", "redirect", "noindex", "nofollow", "robots.txt", "meta", "tag",
        "attribute", "header", "anchor text", "hreflang", "schema", "JSON-LD", "TTFB", "DOM", "render", "directive", "link equity", "crawl budget"
    ];

    /// <summary>The design's "never" list: say what to look at instead.</summary>
    private static readonly string[] NEVER = ["investigate", "look into", "requires further investigation"];

    /// <summary>
    /// Every check has all of its plain words or none: the title, what's wrong, how to fix it, who fixes it and the good
    /// news are written together, and a check with none says null everywhere.
    /// </summary>
    [Fact]
    public void ACheckHasAllOfItsPlainWordsOrNone()
    {
        Assert.All(IssueCatalog.All, check =>
        {
            Assert.Same(IssuePlainTexts.Find(check.Id), check.Plain);
            if (check.Plain is not { } plain)
            {
                return;
            }

            Assert.False(string.IsNullOrWhiteSpace(plain.Title), check.Id);
            Assert.False(string.IsNullOrWhiteSpace(plain.Passed), check.Id);
            Assert.True(Enum.IsDefined(plain.Fixer), check.Id);
            Assert.NotEmpty(plain.What);
            Assert.NotEmpty(plain.Fix);
            Assert.All(plain.What.Concat(plain.Fix), text => Assert.False(string.IsNullOrWhiteSpace(text), check.Id));
            Assert.True(plain.OneFix is null || !string.IsNullOrWhiteSpace(plain.OneFix), check.Id);
        });

        // No entry is for a check the catalog doesn't have: a mistyped id would leave its check without words.
        Assert.All(IssuePlainTexts.All.Keys, id => Assert.NotNull(IssueCatalog.Find(id)));
        Assert.Null(IssuePlainTexts.Find("no-such-check"));
    }

    /// <summary>The template's limits, for every check that has plain words.</summary>
    [Fact]
    public void EveryPlainTextKeepsToTheTemplate()
    {
        Assert.NotEmpty(IssuePlainTexts.All);
        Assert.All(IssuePlainTexts.All, entry =>
        {
            var (id, plain) = (entry.Key, entry.Value);

            // The title: the problem as its owner would say it, in sentence case, with no full stop.
            Assert.True(plain.Title.Length <= TITLE_MAX_CHARACTERS, $"{id}: the title is {plain.Title.Length} characters");
            Assert.True(char.IsUpper(plain.Title[0]) && !plain.Title.EndsWith('.'), $"{id}: {plain.Title}");

            // What's wrong: 2 to 4 sentences, a sentence a string. The first stands alone, as lists show only it.
            Assert.InRange(plain.What.Count, 2, 4);
            Assert.True(plain.What[0].Length <= FIRST_SENTENCE_MAX_CHARACTERS, $"{id}: sentence 1 is {plain.What[0].Length} characters");
            Assert.True(plain.What.Sum(Words) <= WHAT_MAX_WORDS, $"{id}: what's wrong runs to {plain.What.Sum(Words)} words");
            Assert.All(plain.What, sentence =>
            {
                Assert.True(IsOneSentence(sentence), $"{id}: not one sentence: {sentence}");
                Assert.True(Words(sentence) <= SENTENCE_MAX_WORDS, $"{id}: a sentence of {Words(sentence)} words: {sentence}");
            });

            // How to fix it: 1 to 4 steps, each one sentence.
            Assert.InRange(plain.Fix.Count, 1, 4);
            Assert.All(plain.Fix, step =>
            {
                Assert.True(IsOneSentence(step), $"{id}: not one sentence: {step}");
                Assert.True(Words(step) <= STEP_MAX_WORDS, $"{id}: a step of {Words(step)} words: {step}");
            });

            // The good news, as a fact.
            Assert.True(plain.Passed.Length <= PASSED_MAX_CHARACTERS, $"{id}: the good news is {plain.Passed.Length} characters");
            Assert.True(char.IsUpper(plain.Passed[0]) && !plain.Passed.EndsWith('.'), $"{id}: {plain.Passed}");
            Assert.True(plain.OneFix is null || IsOneSentence(plain.OneFix), $"{id}: {plain.OneFix}");
        });
    }

    /// <summary>
    /// No plain text holds a word the design's rules ban: a technical term is never bare (it comes once, in quotation
    /// marks, where the reader needs it), never in a title or the good news, and nothing says "investigate".
    /// </summary>
    [Fact]
    public void NoPlainTextHoldsAWordTheRulesBan()
    {
        Assert.All(IssuePlainTexts.All, entry =>
        {
            var (id, plain) = (entry.Key, entry.Value);
            List<string> texts = [plain.Title, .. plain.What, .. plain.Fix, plain.Passed, .. plain.OneFix is { } oneFix ? [oneFix] : Array.Empty<string>()];
            var quoted = string.Join('\n', texts.SelectMany(text => Quoted().Matches(text).Select(match => match.Value)));

            Assert.All(NEVER_BARE, term =>
            {
                Assert.False(Holds(plain.Title, term) || Holds(plain.Passed, term), $"{id}: \"{term}\" in a title or the good news");
                Assert.True(!texts.Any(text => Holds(text, term)) || Holds(quoted, term), $"{id}: \"{term}\" is used but never given in quotation marks");
            });
            Assert.All(NEVER, phrase => Assert.DoesNotContain(texts, text => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)));
        });

        // The categories and the skip reasons have no brackets to introduce a term in: none is used at all.
        var plainWords = Enum.GetValues<IssueCategory>().Select(PlainWords.CategoryTitle).OfType<string>()
            .Concat(PlainWords.SkipReasons.Values.SelectMany(reason => new[] { reason.Text, reason.Action })).ToList();
        Assert.All(plainWords, text =>
        {
            Assert.All(NEVER_BARE, term => Assert.False(Holds(text, term), $"\"{term}\" in: {text}"));
            Assert.All(NEVER, phrase => Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>The test itself: a bare term is caught, in any of its forms, and one given in quotation marks first is let through.</summary>
    [Fact]
    public void ABareTermIsCaughtWhateverItsForm()
    {
        Assert.True(Holds("The page redirects to another.", "redirect"));
        Assert.True(Holds("These pages are non-indexable.", "indexable"));
        Assert.True(Holds("Search engines stopped crawling it.", "crawl"));
        Assert.True(Holds("Its headers say otherwise.", "header"));
        Assert.True(Holds("Open /robots.txt in a browser.", "robots.txt"));
        Assert.True(Holds("The DOM is large.", "DOM"));

        // Other words that merely contain one.
        Assert.False(Holds("The next stage of the kingdom.", "tag"));
        Assert.False(Holds("The next stage of the kingdom.", "DOM"));
        Assert.False(Holds("Metal garden furniture.", "meta"));
        Assert.False(Holds("Pages that can't be found (a 404).", "4xx"));
    }

    /// <summary>Every category has a plain title of its own, short enough for a heading.</summary>
    [Fact]
    public void EveryCategoryHasAPlainTitleOfItsOwn()
    {
        var titles = Enum.GetValues<IssueCategory>().Select(PlainWords.CategoryTitle).ToList();

        Assert.All(titles, title => Assert.Matches(new Regex("^[A-Z][A-Za-z-]*( [a-z-]+){0,2}$"), title));
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(("Pages that load", "Duplicate addresses", "Languages"),
            (PlainWords.CategoryTitle(IssueCategory.ResponseCodes), PlainWords.CategoryTitle(IssueCategory.Canonicals), PlainWords.CategoryTitle(IssueCategory.Hreflang)));
        Assert.Null(PlainWords.CategoryTitle((IssueCategory)200));
    }

    /// <summary>A skip reason's plain words are a sentence saying why and a sentence saying what would let the check run, by a code the analysis stores.</summary>
    [Fact]
    public void ASkipReasonsPlainWordsAreTwoSentencesByAKnownCode()
    {
        var codes = SkipReasonCodes();

        Assert.NotEmpty(PlainWords.SkipReasons);
        Assert.All(PlainWords.SkipReasons, entry =>
        {
            Assert.Contains(entry.Key, codes);
            Assert.Same(entry.Value, PlainWords.SkipReason(entry.Key));
            Assert.True(IsOneSentence(entry.Value.Text) && Words(entry.Value.Text) <= SENTENCE_MAX_WORDS, entry.Value.Text);
            Assert.True(IsOneSentence(entry.Value.Action) && Words(entry.Value.Action) <= SENTENCE_MAX_WORDS, entry.Value.Action);
        });
        Assert.Equal(new SkipReasonPlainText("Your site has no sitemap, the list of its pages for search engines, to compare with.", "Add a sitemap, then check the site again."),
            PlainWords.SkipReason(SkippedCheckReasons.NO_SITEMAP));
        Assert.Null(PlainWords.SkipReason("no-such-reason"));
    }

    /// <summary>
    /// What round 4 turns on, by deleting the Skip: every check, and every reason a check can be skipped for, has its
    /// plain words.
    /// </summary>
    [Fact(Skip = "round 4 writes the texts")]
    public void EveryCheckAndEverySkipReasonHasPlainWords()
    {
        var without = IssueCatalog.All.Where(check => check.Plain is null).Select(check => check.Id).ToList();
        Assert.True(without.Count == 0, $"{without.Count} of {IssueCatalog.All.Count} checks have no plain words: {string.Join(", ", without)}");

        var reasons = SkipReasonCodes().Where(code => PlainWords.SkipReason(code) is null).ToList();
        Assert.True(reasons.Count == 0, $"Skip reasons with no plain words: {string.Join(", ", reasons)}");
    }

    /// <summary>
    /// The words are no part of what an analysis stores, so writing one makes no analysis out of date: the checks'
    /// fingerprint is of their ids, categories and severities and nothing else, worked out here from those alone.
    /// </summary>
    [Fact]
    public void PlainWordsAreNotPartOfTheChecksFingerprint()
    {
        var lines = IssueCatalog.All.Select(check => $"{check.Id}\t{check.Category}\t{check.Severity}\n").Order(StringComparer.Ordinal);
        var fromThoseAlone = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines))))[..16];

        Assert.Equal(fromThoseAlone, AnalysisFreshness.ChecksFingerprint);

        // Checks with words and checks without are both in it.
        Assert.Contains(IssueCatalog.All, check => check.Plain is not null);
        Assert.Contains(IssueCatalog.All, check => check.Plain is null);
    }

    /// <summary>Every reason code the analysis can store (<see cref="SkippedCheckReasons"/>'s constants).</summary>
    private static List<string> SkipReasonCodes() =>
    [
        .. typeof(SkippedCheckReasons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
    ];

    private static int Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>It ends with a full stop, and no full stop inside it is followed by the start of another sentence.</summary>
    private static bool IsOneSentence(string text) => text.EndsWith('.') && !SentenceBreak().IsMatch(text[..^1]);

    /// <summary>
    /// Whether the text holds the term as a word of its own, in any of its forms ("redirects", "non-indexable", "crawling").
    /// An acronym is matched as written; the rest whatever their case.
    /// </summary>
    private static bool Holds(string text, string term)
    {
        var acronym = term.Any(char.IsUpper);
        return Regex.IsMatch(text, $@"(?<![A-Za-z0-9]){Regex.Escape(term)}(s|ed|ing|er|ers)?(?![A-Za-z0-9])", acronym ? RegexOptions.None : RegexOptions.IgnoreCase);
    }

    [GeneratedRegex("\"[^\"]*\"")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[.!?]\s+[A-Z""]")]
    private static partial Regex SentenceBreak();
}
