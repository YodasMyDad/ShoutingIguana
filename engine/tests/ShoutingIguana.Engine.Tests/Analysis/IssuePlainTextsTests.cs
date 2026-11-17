using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The plain words, held to the design's template and rules (docs/v2-ui-simple.md, "Plain words"): the shape of each
/// text, its limits, and the words that are never bare. The words are written in one data file
/// (<c>PlainWords/plain-words.json</c>), which <see cref="IssuePlainTexts"/> and <see cref="PlainWords"/> are generated
/// from; these tests judge every one of them.
/// </summary>
public sealed partial class IssuePlainTextsTests
{
    private const int TITLE_MAX_CHARACTERS = 60;
    private const int PASSED_MAX_CHARACTERS = 60;
    private const int LOOKS_FOR_MAX_CHARACTERS = 60;
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
    /// Every check's plain words as a reader gets them: each limit a text names filled in, at the default thresholds. The
    /// template's limits and the banned words are judged on these, as "{limit:titleMaxLength}" reads "60 characters".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IssuePlainText> FILLED =
        IssuePlainTexts.All.ToDictionary(entry => entry.Key, entry => PlainLimits.Fill(entry.Value, new AnalysisSettings()), StringComparer.Ordinal);

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

            // The two optional parts are absent or written, never empty.
            Assert.True(plain.OneFix is null || !string.IsNullOrWhiteSpace(plain.OneFix), check.Id);
            Assert.True(plain.LooksFor is null || !string.IsNullOrWhiteSpace(plain.LooksFor), check.Id);
        });

        // No entry is for a check the catalog doesn't have: a mistyped id would leave its check without words.
        Assert.All(IssuePlainTexts.All.Keys, id => Assert.NotNull(IssueCatalog.Find(id)));
        Assert.Null(IssuePlainTexts.Find("no-such-check"));
    }

    /// <summary>The template's limits, for every check that has plain words.</summary>
    [Fact]
    public void EveryPlainTextKeepsToTheTemplate()
    {
        Assert.NotEmpty(FILLED);
        Assert.All(FILLED, entry =>
        {
            var (id, plain) = (entry.Key, entry.Value);

            // The title: the problem as its owner would say it, in sentence case, with no full stop and no count.
            Assert.True(plain.Title.Length <= TITLE_MAX_CHARACTERS, $"{id}: the title is {plain.Title.Length} characters");
            Assert.True(char.IsUpper(plain.Title[0]) && !plain.Title.EndsWith('.'), $"{id}: {plain.Title}");
            Assert.False(plain.Title.Any(char.IsAsciiDigit), $"{id}: a number in the title: {plain.Title}");

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

            // What the check looks for: a short noun phrase, not a sentence.
            Assert.True(plain.LooksFor is null || IsANounPhrase(plain.LooksFor), $"{id}: {plain.LooksFor}");
        });
    }

    /// <summary>The design's reading age: sentences of 20 words on average, across every check's account of what's wrong.</summary>
    [Fact]
    public void SentencesAverageTwentyWordsOrFewer()
    {
        var sentences = FILLED.Values.SelectMany(plain => plain.What).ToList();

        Assert.True(sentences.Count > 250);
        Assert.True(sentences.Average(Words) <= 20, $"Sentences average {sentences.Average(Words):F1} words");
    }

    /// <summary>What a check looks for is a short noun phrase ("A missing sitemap"): 60 characters at most, sentence case, no full stop.</summary>
    [Fact]
    public void WhatACheckLooksForIsAShortNounPhrase()
    {
        Assert.True(IsANounPhrase("A missing sitemap"));
        Assert.True(IsANounPhrase("Titles over 60 characters"));
        Assert.False(IsANounPhrase("The check looks for a sitemap."));
        Assert.False(IsANounPhrase("a missing sitemap"));
        Assert.False(IsANounPhrase(new string('a', LOOKS_FOR_MAX_CHARACTERS + 1)));
        Assert.True(IsANounPhrase("A" + new string('a', LOOKS_FOR_MAX_CHARACTERS - 1)));

        // It is optional: a check whose title already names what it looks for has none, and says null.
        Assert.Contains(IssuePlainTexts.All.Values, plain => plain.LooksFor is null);
        Assert.Contains(IssuePlainTexts.All.Values, plain => plain.LooksFor is not null);
        Assert.All(IssuePlainTexts.All.Values, plain => Assert.True(plain.LooksFor is null || IsANounPhrase(PlainLimits.Fill(plain.LooksFor, new AnalysisSettings()))));
    }

    /// <summary>
    /// A plain text names a threshold as {limit:name}. Every text the engine has, of every check, category and skip reason,
    /// fills without error at any settings: a name no setting has would throw here, and a token that is misspelt would be
    /// left in the text, braces and all.
    /// </summary>
    [Fact]
    public void EveryLimitAPlainTextNamesIsAThresholdTheSettingsHave()
    {
        var settings = new AnalysisSettings { TitleMaxLength = 70, LargeImageBytes = 300 * 1024, SlowResponseMs = 1_500 };
        List<string> filled = [];
        foreach (var plain in IssuePlainTexts.All.Values.Select(plain => PlainLimits.Fill(plain, settings)))
        {
            filled.AddRange([plain.Title, .. plain.What, .. plain.Fix, plain.Passed]);
            filled.AddRange(new[] { plain.OneFix, plain.LooksFor }.OfType<string>());
        }

        filled.AddRange(PlainWords.SkipReasons.Values.Select(reason => PlainLimits.Fill(reason, settings)).SelectMany(reason => new[] { reason.Text, reason.Action }));
        filled.AddRange(Enum.GetValues<IssueCategory>().Select(PlainWords.CategoryTitle).OfType<string>().Select(title => PlainLimits.Fill(title, settings)));

        Assert.True(filled.Count > 100);
        Assert.All(filled, text => Assert.True(!text.Contains('{') && !text.Contains('}'), $"A token was left in: {text}"));
    }

    /// <summary>
    /// No plain text holds a word the design's rules ban: a technical term is never bare (it comes once, in quotation
    /// marks, where the reader needs it), never in a title or the good news, and nothing says "investigate".
    /// </summary>
    [Fact]
    public void NoPlainTextHoldsAWordTheRulesBan()
    {
        Assert.All(FILLED, entry =>
        {
            var (id, plain) = (entry.Key, entry.Value);
            List<string> texts = [plain.Title, .. plain.What, .. plain.Fix, plain.Passed, .. new[] { plain.OneFix, plain.LooksFor }.OfType<string>()];
            var quoted = string.Join('\n', texts.SelectMany(text => Quoted().Matches(text).Select(match => match.Value)));

            Assert.All(NEVER_BARE, term =>
            {
                Assert.False(Holds(plain.Title, term) || Holds(plain.Passed, term) || Holds(plain.LooksFor ?? string.Empty, term),
                    $"{id}: \"{term}\" in a title, the good news or what the check looks for");
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

        // A heading of one to four words, in sentence case: "Languages", "Extras in search results".
        Assert.All(titles, title => Assert.Matches(new Regex("^[A-Z][A-Za-z-]*( [a-z-]+){0,3}$"), title));
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
        Assert.Equal(
            new SkipReasonPlainText(
                "No sitemap, the list of your pages for search engines, could be read, so there was nothing to compare with.",
                "Add a sitemap, or fix the one that can't be read, then check the site again."),
            PlainWords.SkipReason(SkippedCheckReasons.NO_SITEMAP));
        Assert.Null(PlainWords.SkipReason("no-such-reason"));
    }

    /// <summary>Every check, every category, and every reason a check can be skipped for, has its plain words.</summary>
    [Fact]
    public void EveryCheckAndEverySkipReasonHasPlainWords()
    {
        var without = IssueCatalog.All.Where(check => check.Plain is null).Select(check => check.Id).ToList();
        Assert.True(without.Count == 0, $"{without.Count} of {IssueCatalog.All.Count} checks have no plain words: {string.Join(", ", without)}");

        var reasons = SkipReasonCodes().Where(code => PlainWords.SkipReason(code) is null).ToList();
        Assert.True(reasons.Count == 0, $"Skip reasons with no plain words: {string.Join(", ", reasons)}");

        Assert.All(Enum.GetValues<IssueCategory>(), category => Assert.False(string.IsNullOrWhiteSpace(PlainWords.CategoryTitle(category)), category.ToString()));
        Assert.Equal((87, 18, 17), (IssuePlainTexts.All.Count, PlainWords.SkipReasons.Count, Enum.GetValues<IssueCategory>().Length));
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

        // Every check in it has its words, and they are no part of it.
        Assert.All(IssueCatalog.All, check => Assert.NotNull(check.Plain));
    }

    /// <summary>Every reason code the analysis can store (<see cref="SkippedCheckReasons"/>'s constants).</summary>
    private static List<string> SkipReasonCodes() =>
    [
        .. typeof(SkippedCheckReasons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
    ];

    private static int Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsANounPhrase(string text) => text.Length <= LOOKS_FOR_MAX_CHARACTERS && char.IsUpper(text[0]) && !text.EndsWith('.');

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
