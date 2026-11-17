using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Where the plain words come from. They are written in one data file (<c>PlainWords/plain-words.json</c>), which
/// <c>generate.mjs</c> beside it turns into <see cref="IssuePlainTexts"/> and <see cref="PlainWords"/>: the compiled words
/// are held to the file here, so one edited without the other fails. And the ten checks the design document writes out
/// (docs/v2-ui-simple.md, "Ten checks, written out") are served as it writes them, word for word.
/// </summary>
public sealed partial class PlainWordsDataTests
{
    private static readonly JsonNode DATA = JsonNode.Parse(RepoFiles.Read("engine", "tests", "ShoutingIguana.Engine.Tests", "Analysis", "PlainWords", "plain-words.json"))!;

    private static readonly string DESIGN = RepoFiles.Read("docs", "v2-ui-simple.md");

    private static readonly string[] FIELDS = ["plainTitle", "plainWhat", "plainFix", "fixer", "plainPassed", "plainOneFix", "plainLooksFor"];

    [Fact]
    public void TheChecksWordsAreTheDataFiles()
    {
        var checks = DATA["checks"]!.AsObject();

        Assert.Equal(IssuePlainTexts.All.Keys.Order(StringComparer.Ordinal), checks.Select(check => check.Key).Order(StringComparer.Ordinal));
        Assert.All(checks, check =>
        {
            var (written, plain) = (check.Value!.AsObject(), IssuePlainTexts.Find(check.Key)!);

            Assert.Equal((string?)written["plainTitle"], plain.Title);
            Assert.Equal(written["plainWhat"]!.AsArray().Select(sentence => (string)sentence!), plain.What);
            Assert.Equal(written["plainFix"]!.AsArray().Select(step => (string)step!), plain.Fix);
            Assert.Equal((string?)written["fixer"], Fixer(plain.Fixer));
            Assert.Equal((string?)written["plainPassed"], plain.Passed);
            Assert.Equal((string?)written["plainOneFix"], plain.OneFix);
            Assert.Equal((string?)written["plainLooksFor"], plain.LooksFor);

            // Nothing in the file is left out of the engine: a field it doesn't know would be words nobody is served.
            Assert.All(written, field => Assert.Contains(field.Key, FIELDS));
        });
    }

    [Fact]
    public void TheCategoriesAndTheSkipReasonsWordsAreTheDataFiles()
    {
        // A category by its id in the API: the enum's name with a small first letter.
        var categories = DATA["categories"]!.AsObject();
        Assert.Equal(
            Enum.GetValues<IssueCategory>().Select(category => (Id: ApiId(category), Title: PlainWords.CategoryTitle(category))).OrderBy(category => category.Id, StringComparer.Ordinal),
            categories.Select(category => (Id: category.Key, Title: (string?)category.Value!["plainTitle"])).OrderBy(category => category.Id, StringComparer.Ordinal));

        var reasons = DATA["skipReasons"]!.AsObject();
        Assert.Equal(
            PlainWords.SkipReasons.Select(reason => (Code: reason.Key, reason.Value.Text, reason.Value.Action)).OrderBy(reason => reason.Code, StringComparer.Ordinal),
            reasons.Select(reason => (Code: reason.Key, Text: (string)reason.Value!["plainText"]!, Action: (string)reason.Value!["plainAction"]!)).OrderBy(reason => reason.Code, StringComparer.Ordinal));

        Assert.Equal(["categories", "checks", "skipReasons"], DATA.AsObject().Select(part => part.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The design document writes ten checks out in full. Each is served as written there: its title, what the check
    /// looks for, what's wrong, the steps, the one-fix sentence, the good news, who fixes it, and the words for the
    /// reason one of them is skipped. A limit is a token in both.
    /// </summary>
    [Fact]
    public void TheTenChecksTheDesignWritesOutAreServedWordForWord()
    {
        var written = WrittenOut();

        Assert.Equal(
        [
            "robots-txt-unreachable", "internal-client-error", "broken-internal-links", "empty-anchor-text", "canonicalised-page",
            "duplicate-site-version", "image-too-large", "title-duplicate", "slow-response", "description-missing"
        ], written.Select(check => check.Id));

        Assert.All(written, check =>
        {
            var (definition, plain) = (IssueCatalog.Find(check.Id)!, IssuePlainTexts.Find(check.Id)!);
            var parts = check.Parts;

            Assert.Equal(parts["Title"], plain.Title);
            Assert.Equal(parts.GetValueOrDefault("Looks for"), plain.LooksFor);
            Assert.Equal(parts["What"], string.Join(' ', plain.What));
            Assert.Equal(parts["Fix"], string.Join(' ', plain.Fix.Select((step, index) => $"({index + 1}) {step}")));
            Assert.Equal(parts.GetValueOrDefault("One fix"), plain.OneFix);
            Assert.Equal(parts["Passed"], plain.Passed);
            Assert.Equal(check.Fixer, Fixer(plain.Fixer));

            // Its heading names the check's category and severity as the catalog has them: "(Response codes, Error; …)".
            Assert.Equal(check.Category.Replace(" ", string.Empty), definition.Category.ToString(), ignoreCase: true);
            Assert.Equal(check.Severity, definition.Severity.ToString());

            // Nothing is written there that the engine has no place for.
            Assert.All(parts.Keys, label => Assert.True(label is "Title" or "Looks for" or "What" or "Fix" or "One fix" or "Passed" || label.StartsWith("When skipped", StringComparison.Ordinal), label));
        });

        // "When skipped (`site-versions-unchecked`): why. What would let it run."
        var skipped = written.SelectMany(check => check.Parts).Where(part => part.Key.StartsWith("When skipped", StringComparison.Ordinal)).ToList();
        var whenSkipped = Assert.Single(skipped);
        var reason = PlainWords.SkipReason(Code().Match(whenSkipped.Key).Groups[1].Value)!;
        Assert.Equal(whenSkipped.Value, $"{reason.Text} {reason.Action}");
    }

    /// <summary>
    /// The two limits the design's ten name are tokens, and at the default thresholds they read as the document's
    /// sentences always did.
    /// </summary>
    [Fact]
    public void TheDesignsLimitsReadAsItsNumbersAtTheDefaults()
    {
        var defaults = new AnalysisSettings();

        Assert.Equal("Some pages show images whose files are over {limit:largeImageBytes}.", IssuePlainTexts.Find("image-too-large")!.What[0]);
        Assert.Equal("Some pages show images whose files are over 200 KB.", PlainLimits.Fill(IssuePlainTexts.Find("image-too-large"), defaults)!.What[0]);
        Assert.Equal("These pages took more than {limit:slowResponseMs} to arrive when the check asked for them.", IssuePlainTexts.Find("slow-response")!.What[0]);
        Assert.Equal("These pages took more than 1 second to arrive when the check asked for them.", PlainLimits.Fill(IssuePlainTexts.Find("slow-response"), defaults)!.What[0]);

        // Fourteen checks name a limit, sixteen times, and no other text does: a number a setting holds is never typed.
        var naming = IssuePlainTexts.All
            .Select(check => (check.Key, Limits: Limit().Matches(Everything(check.Value)).Select(match => match.Groups[1].Value).ToList()))
            .Where(check => check.Limits.Count > 0)
            .ToDictionary(check => check.Key, check => string.Join(' ', check.Limits), StringComparer.Ordinal);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["title-too-long"] = "titleMaxPixels",
                ["title-too-short"] = "titleMinPixels",
                ["description-too-long"] = "descriptionMaxPixels descriptionMaxPixels",
                ["description-too-short"] = "descriptionMinPixels",
                ["low-word-count"] = "minWordCount",
                ["image-alt-too-long"] = "imageAltMaxLength",
                ["image-too-large"] = "largeImageBytes",
                ["url-too-long"] = "maxUrlLength",
                ["slow-response"] = "slowResponseMs",
                ["large-page"] = "largePageBytes",
                ["few-internal-outlinks"] = "minInternalOutlinks",
                ["deep-page"] = "maxClickDepth",
                ["too-many-links"] = "maxInternalLinks maxExternalLinks",
                ["slow-external-links"] = "slowExternalLinkMs"
            }, naming);
    }

    /// <summary>
    /// The design's rule: a Notice says when it is fine, and six that are never simply fine say when the fix can wait. The
    /// six the document names are Notices, and each says so.
    /// </summary>
    [Fact]
    public void TheNoticesTheDesignNamesSayWhenTheyCanWait()
    {
        var rule = DESIGN[DESIGN.IndexOf("- **A Notice always says when it is fine**", StringComparison.Ordinal)..];
        rule = rule[..rule.IndexOf("\n- **", StringComparison.Ordinal)];
        var named = Code().Matches(rule).Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(["empty-anchor-text", "h1-missing", "h1-multiple", "security-headers-missing", "lang-missing", "charset-missing"], named);
        Assert.All(named, id =>
        {
            Assert.Equal(IssueSeverity.Notice, IssueCatalog.Find(id)!.Severity);
            Assert.Contains(IssuePlainTexts.Find(id)!.What, sentence => sentence.Contains("can wait", StringComparison.Ordinal));
        });
    }

    /// <summary>The examples the design gives of a category's title and of a skip reason's words are the ones served.</summary>
    [Fact]
    public void TheDesignsExamplesOfCategoriesAndSkipReasonsAreTheWordsServed()
    {
        var design = Whitespace().Replace(DESIGN, " ");

        Assert.Contains(
            $"Response codes: \"{PlainWords.CategoryTitle(IssueCategory.ResponseCodes)}\"; Canonicals: \"{PlainWords.CategoryTitle(IssueCategory.Canonicals)}\"; " +
            $"Hreflang: \"{PlainWords.CategoryTitle(IssueCategory.Hreflang)}\"", design, StringComparison.Ordinal);

        var noSitemap = PlainWords.SkipReason(SkippedCheckReasons.NO_SITEMAP)!;
        Assert.Contains($"(\"{noSitemap.Text} {noSitemap.Action}\")", design, StringComparison.Ordinal);
    }

    /// <summary>
    /// The checks under "Ten checks, written out": each a heading line ("**1. `id`** (Category, Severity; …). Fixer: who.")
    /// and a list of labelled parts ("- Title: …"), a part's wrapped lines joined by a space.
    /// </summary>
    private static List<(string Id, string Category, string Severity, string Fixer, Dictionary<string, string> Parts)> WrittenOut()
    {
        var section = DESIGN[DESIGN.IndexOf("### Ten checks, written out", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", StringComparison.Ordinal)];
        var headings = Heading().Matches(section);

        return
        [
            .. headings.Select((heading, index) =>
            {
                var body = section[(heading.Index + heading.Length)..(index + 1 < headings.Count ? headings[index + 1].Index : section.Length)];
                var parts = Part().Matches(body).ToDictionary(
                    part => part.Groups["label"].Value,
                    part => Whitespace().Replace(part.Groups["text"].Value, " ").Trim(),
                    StringComparer.Ordinal);
                return (heading.Groups["id"].Value, heading.Groups["category"].Value, heading.Groups["severity"].Value, heading.Groups["fixer"].Value, parts);
            })
        ];
    }

    /// <summary>Every text of a check's plain words, in one string.</summary>
    private static string Everything(IssuePlainText plain)
    {
        string?[] texts = [plain.Title, .. plain.What, .. plain.Fix, plain.Passed, plain.OneFix, plain.LooksFor];
        return string.Join(' ', texts.OfType<string>());
    }

    private static string Fixer(IssueFixer fixer) => fixer switch
    {
        IssueFixer.You => "you",
        IssueFixer.Developer => "developer",
        IssueFixer.Either => "either",
        _ => throw new ArgumentOutOfRangeException(nameof(fixer), fixer, null)
    };

    private static string ApiId(IssueCategory category) => char.ToLowerInvariant(category.ToString()[0]) + category.ToString()[1..];

    [GeneratedRegex(@"^\*\*\d+\. `(?<id>[a-z0-9-]+)`\*\* \((?<category>[A-Za-z ]+), (?<severity>Error|Warning|Notice)[;)].*Fixer: (?<fixer>\w+)\.$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    /// <summary>"- Label: text", the text running on over indented lines until the next part or a blank line.</summary>
    [GeneratedRegex(@"^- (?<label>[A-Z][^:\n]*): (?<text>.*(?:\n  \S.*)*)$", RegexOptions.Multiline)]
    private static partial Regex Part();

    [GeneratedRegex("`([a-z0-9-]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\{limit:(\w+)\}")]
    private static partial Regex Limit();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
