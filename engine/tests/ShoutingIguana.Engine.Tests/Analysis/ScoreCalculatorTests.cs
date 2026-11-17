using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The score, version 1, held to the design document (docs/v2-ui-simple.md, "The score"): every row of its tables is a
/// case here, and every case its reference code scores (<c>ScoreReference/cases.json</c>, written from
/// <c>app/design/simple/build/lib/score.mjs</c>) scores the same in the engine.
/// </summary>
public sealed class ScoreCalculatorTests
{
    private const int PAGES = 1_000;
    private const int URLS = 5_000;

    /// <summary>
    /// "The garden crawl: 85, Good", line for line, and what fixing the first five leaves. The design worked it out when
    /// duplicate descriptions were worth fixing; the check audit made them a notice, so another warning of the same count
    /// stands in for them here (<see cref="TheGardenCrawlScores87NowThatDuplicateDescriptionsAreANotice"/> scores the crawl as it is now).
    /// </summary>
    [Fact]
    public void TheGardenCrawlScores85AndFixingItsFirstFiveLeaves98()
    {
        var score = Score(265, 4_521,
            ("internal-client-error", 9), ("broken-internal-links", 5), ("image-too-large", 61), ("description-multiple", 8), ("title-duplicate", 6),
            ("slow-response", 3), ("empty-anchor-text", 265), ("image-missing-dimensions", 230), ("description-too-long", 210), ("title-too-long", 86),
            ("open-graph-missing", 11), ("description-missing", 11), ("title-too-short", 9), ("canonical-missing", 8), ("low-word-count", 7),
            ("canonicalised-page", 1), ("security-headers-missing", 1), ("sitemap-missing", 1), ("h1-missing", 1));

        Assert.Equal((1, 85, ScoreBand.Good, 15, 85, false), (score.Version, score.Value, score.Band, score.Lost, score.BeforeCeiling, score.HeldBySerious));
        Assert.Equal(
        [
            new ScoreLine("internal-client-error", IssueSeverity.Error, 9, 4_521, ScoreUnit.Urls, ScoreExtent.Few, 2),
            new ScoreLine("broken-internal-links", IssueSeverity.Error, 5, 265, ScoreUnit.Pages, ScoreExtent.Some, 4),
            new ScoreLine("image-too-large", IssueSeverity.Warning, 61, 265, ScoreUnit.Pages, ScoreExtent.Many, 3),
            new ScoreLine("description-multiple", IssueSeverity.Warning, 8, 265, ScoreUnit.Pages, ScoreExtent.Some, 2),
            new ScoreLine("title-duplicate", IssueSeverity.Warning, 6, 265, ScoreUnit.Pages, ScoreExtent.Some, 2),
            new ScoreLine("slow-response", IssueSeverity.Warning, 3, 265, ScoreUnit.Pages, ScoreExtent.Some, 2)
        ], score.Lines.Take(6));

        // The 13 notices are lines too, at nothing each.
        Assert.Equal(19, score.Lines.Count);
        Assert.All(score.Lines.Skip(6), line => Assert.Equal((IssueSeverity.Notice, 0), (line.Severity, line.Points)));

        // "Fixing the first five in "Fix first" order leaves slow-response: 98."
        Assert.Equal(["internal-client-error", "broken-internal-links", "image-too-large", "description-multiple", "title-duplicate"], score.IfFixed.CheckIds);
        Assert.Equal(98, score.IfFixed.Value);
    }

    /// <summary>
    /// The same crawl now that duplicate descriptions are a notice (the check audit, docs/v2-check-audit.md): their 2 points
    /// are no longer lost, so it scores 87, and the five to fix first are then all that cost points, so fixing them leaves 100.
    /// </summary>
    [Fact]
    public void TheGardenCrawlScores87NowThatDuplicateDescriptionsAreANotice()
    {
        var score = Score(265, 4_521,
            ("internal-client-error", 9), ("broken-internal-links", 5), ("image-too-large", 61), ("description-duplicate", 8), ("title-duplicate", 6),
            ("slow-response", 3), ("empty-anchor-text", 265), ("image-missing-dimensions", 230), ("description-too-long", 210), ("title-too-long", 86),
            ("open-graph-missing", 11), ("description-missing", 11), ("title-too-short", 9), ("canonical-missing", 8), ("low-word-count", 7),
            ("canonicalised-page", 1), ("security-headers-missing", 1), ("sitemap-missing", 1), ("h1-missing", 1));

        Assert.Equal((87, ScoreBand.Good, 13, 87, false), (score.Value, score.Band, score.Lost, score.BeforeCeiling, score.HeldBySerious));
        Assert.Equal(["internal-client-error", "broken-internal-links", "image-too-large", "title-duplicate", "slow-response"], score.Lines.Take(5).Select(line => line.CheckId));
        Assert.Contains(new ScoreLine("description-duplicate", IssueSeverity.Notice, 8, 265, ScoreUnit.Pages, ScoreExtent.Some, 0), score.Lines);
        Assert.Equal(["internal-client-error", "broken-internal-links", "image-too-large", "title-duplicate", "slow-response"], score.IfFixed.CheckIds);
        Assert.Equal(100, score.IfFixed.Value);
    }

    /// <summary>"A nearly clean site (invented): 98, Very good."</summary>
    [Fact]
    public void ANearlyCleanSiteScores98()
    {
        var score = Score(48, 612, ("image-missing-alt", 3), ("title-too-long", 4), ("description-too-long", 9), ("canonical-missing", 2),
            ("low-word-count", 1), ("open-graph-missing", 48), ("security-headers-missing", 1));

        Assert.Equal((98, ScoreBand.VeryGood, 2), (score.Value, score.Band, score.Lost));
        Assert.Equal((ScoreExtent.Some, 2), (score.Lines[0].Extent, score.Lines[0].Points));
    }

    /// <summary>"A badly broken site (invented): 36, Poor", line for line.</summary>
    [Fact]
    public void ABadlyBrokenSiteScores36()
    {
        var score = Score(1_240, 3_870,
            ("broken-internal-links", 1_240), ("mixed-content", 620), ("internal-server-error", 420), ("title-missing", 187), ("internal-client-error", 212),
            ("image-broken", 96), ("redirect-loop", 14), ("image-missing-alt", 1_022), ("viewport-missing", 1_240), ("duplicate-site-version", 1),
            ("title-duplicate", 540), ("slow-response", 310), ("broken-external-links", 44));

        Assert.Equal((36, ScoreBand.Poor, 64, 36, false), (score.Value, score.Band, score.Lost, score.BeforeCeiling, score.HeldBySerious));
        Dictionary<string, (ScoreExtent Extent, int Points)> expected = new()
        {
            ["broken-internal-links"] = (ScoreExtent.Most, 10),
            ["mixed-content"] = (ScoreExtent.Most, 10),
            ["internal-server-error"] = (ScoreExtent.Many, 7),
            ["title-missing"] = (ScoreExtent.Many, 7),
            ["internal-client-error"] = (ScoreExtent.Some, 4),
            ["image-broken"] = (ScoreExtent.Some, 4),
            ["redirect-loop"] = (ScoreExtent.Few, 2),
            ["image-missing-alt"] = (ScoreExtent.Most, 4),
            ["viewport-missing"] = (ScoreExtent.Most, 4),
            ["duplicate-site-version"] = (ScoreExtent.Site, 4),
            ["title-duplicate"] = (ScoreExtent.Many, 3),
            ["slow-response"] = (ScoreExtent.Many, 3),
            ["broken-external-links"] = (ScoreExtent.Some, 2)
        };
        Assert.Equal(expected, score.Lines.ToDictionary(line => line.CheckId, line => (line.Extent, line.Points)));
    }

    /// <summary>"What each mix gives", a row a line.</summary>
    [Fact]
    public void EachMixGivesTheScoreTheDesignSays()
    {
        // Nothing found, or only things worth a look.
        Assert.Equal(100, Score(PAGES, URLS).Value);
        Assert.Equal(100, Score(PAGES, URLS, ("description-missing", 1_000), ("title-too-long", 300), ("noindex-page", 4)).Value);

        // One problem worth fixing on a few pages, and on every page.
        Assert.Equal(99, Score(PAGES, URLS, ("title-duplicate", 2)).Value);
        Assert.Equal(96, Score(PAGES, URLS, ("viewport-missing", 1_000)).Value);

        // Five problems worth fixing, each on some pages.
        // (The design's mixes named description-duplicate among the warnings until the check audit made it a notice: duplicate-content stands in.)
        Assert.Equal(90, Score(PAGES, URLS, ("title-duplicate", 50), ("duplicate-content", 40), ("image-too-large", 30), ("slow-response", 20),
            ("image-missing-alt", 10)).Value);

        // One serious problem on a few pages (98 before the ceiling), and on every page.
        var few = Score(PAGES, URLS, ("broken-internal-links", 3));
        Assert.Equal((89, 98, true, ScoreBand.Good), (few.Value, few.BeforeCeiling, few.HeldBySerious, few.Band));
        var every = Score(PAGES, URLS, ("broken-internal-links", 1_000));
        Assert.Equal((89, 90, true), (every.Value, every.BeforeCeiling, every.HeldBySerious));

        // Three serious on many pages, and five worth fixing on many.
        Assert.Equal(64, Score(PAGES, URLS, ("broken-internal-links", 200), ("title-missing", 150), ("image-broken", 100), ("title-duplicate", 400),
            ("duplicate-content", 300), ("image-too-large", 250), ("slow-response", 200), ("image-missing-alt", 100)).Value);

        // One site-wide serious fault and nothing else, and two of them.
        Assert.Equal((40, ScoreBand.Poor), (Score(PAGES, URLS, ("no-https", 1)).Value, Score(PAGES, URLS, ("no-https", 1)).Band));
        var two = Score(PAGES, URLS, ("no-https", 1), ("robots-txt-unreachable", 1));
        Assert.Equal((0, 120, 0, false), (two.Value, two.Lost, two.BeforeCeiling, two.HeldBySerious));

        // A 5-page site with no title on one page.
        var tiny = Score(5, 61, ("title-missing", 1));
        Assert.Equal((89, 93, ScoreExtent.Many, 7), (tiny.Value, tiny.BeforeCeiling, tiny.Lines[0].Extent, tiny.Lines[0].Points));
    }

    /// <summary>"It can't say these lies", a row a line.</summary>
    [Fact]
    public void TheScoreCantTellTheLiesTheDesignRulesOut()
    {
        // A site robots.txt blocks scores 95: a blocked start URL reads no pages, so there is no score at all; a site
        // blocked in part carries what wasn't read beside the number.
        Assert.Null(ScoreCalculator.Calculate([Check("blocked-by-robots", 1)], 0, pages: 0, urls: 1, incomplete: null, blockedByRobots: 1));
        Assert.Equal(1_080, ScoreCalculator.Calculate([], 0, PAGES, URLS, null, blockedByRobots: 1_080)!.Covers.BlockedByRobots);

        // A site whose every page is noindex scores 100: noindex is a notice, but the site being hidden is serious and site-wide.
        var hidden = Score(265, 4_521, ("noindex-page", 265), ("site-hidden-from-search", 1));
        Assert.Equal((40, 60), (hidden.Value, hidden.Lost));
        Assert.Equal((ScoreUnit.Site, ScoreExtent.Site, 60), (hidden.Lines[0].Unit, hidden.Lines[0].Extent, hidden.Lines[0].Points));

        // robots.txt unreadable, or no HTTPS, scores 90: 60 points, so 40 at best.
        Assert.Equal(40, Score(PAGES, URLS, ("robots-txt-unreachable", 1)).Value);
        Assert.Equal(40, Score(PAGES, URLS, ("no-https", 1)).Value);

        // A 5-page site scores 20 for one missing description: a notice costs nothing; one of five without a title is "many".
        Assert.Equal(100, Score(5, 61, ("description-missing", 1)).Value);
        Assert.Equal(89, Score(5, 61, ("title-missing", 1)).Value);

        // A site with 2 serious problems reads "Very good": the ceiling.
        var serious = Score(10_000, 50_000, ("broken-internal-links", 1), ("internal-client-error", 1));
        Assert.Equal((89, 96, ScoreBand.Good), (serious.Value, serious.BeforeCeiling, serious.Band));

        // An unchecked site reads perfect: skipped checks are counted beside the score, never as clean.
        var skipped = ScoreCalculator.Calculate([Check("title-missing", 0)], checksSkipped: 4, PAGES, URLS, null, 0)!;
        Assert.Equal((1, 4, 100), (skipped.Covers.ChecksRun, skipped.Covers.ChecksSkipped, skipped.Value));
    }

    /// <summary>
    /// The reference code's own cases: the design's named sites, the mockups', the band edges and 120 random sites, each
    /// scored by score.mjs and then by the engine, line for line, with the reference's fix-first order and what fixing
    /// the first five leaves.
    /// </summary>
    [Fact]
    public void EveryCaseTheReferenceCodeScoresComesOutTheSame()
    {
        var file = JsonNode.Parse(File.ReadAllText(ReferenceCases()))!;
        Assert.Equal(ScoreCalculator.VERSION, (int)file["scoreVersion"]!);
        Assert.Equal(ScoreCalculator.SERIOUS_CEILING, (int)file["seriousCeiling"]!);
        var cases = file["cases"]!.AsArray();
        Assert.True(cases.Count > 250, $"Only {cases.Count} reference cases");

        foreach (var site in cases)
        {
            var name = (string)site!["name"]!;
            List<IssueCount> checks =
            [
                .. site["checks"]!.AsArray().Select(check =>
                    new IssueCount(IssueCatalog.Find((string)check!["id"]!)! with { Severity = Enum.Parse<IssueSeverity>((string)check["severity"]!, ignoreCase: true) },
                        (long)check["count"]!))
            ];
            var actual = ScoreCalculator.Calculate(checks, 0, (long)site["pages"]!, (long)site["urls"]!, null, 0);
            if (site["expected"] is not { } expected)
            {
                Assert.True(actual is null, name);
                continue;
            }

            Assert.NotNull(actual);
            Assert.Equal((name, (int)expected["value"]!, Band((string)expected["band"]!), (int)expected["lost"]!, (int)expected["beforeCeiling"]!, (bool)expected["heldBySerious"]!),
                (name, actual.Value, actual.Band, actual.Lost, actual.BeforeCeiling, actual.HeldBySerious));

            // The engine's lines are in fix-first order; the reference's, in the order it was given the checks.
            Assert.Equal(expected["fixFirst"]!.AsArray().Select(id => (string)id!), actual.Lines.Select(line => line.CheckId));
            Assert.Equal(
                expected["lines"]!.AsArray()
                    .Select(line => new ScoreLine((string)line!["checkId"]!, checks.Single(check => check.Issue.Id == (string)line["checkId"]!).Issue.Severity,
                        (long)line["count"]!, (long)line["of"]!, Unit((string)line["unit"]!), Enum.Parse<ScoreExtent>((string)line["extent"]!, ignoreCase: true), (int)line["points"]!))
                    .OrderBy(line => line.CheckId, StringComparer.Ordinal),
                actual.Lines.OrderBy(line => line.CheckId, StringComparer.Ordinal));
            Assert.Equal(expected["ifFixed"]!["checkIds"]!.AsArray().Select(id => (string)id!), actual.IfFixed.CheckIds);
            Assert.Equal((name, (int)expected["ifFixed"]!["value"]!), (name, actual.IfFixed.Value));
        }
    }

    /// <summary>
    /// The reference names the site-wide checks and the checks that can flag any URL by id; the engine reads both off the
    /// catalog. They agree, or the engine would score other lines than the reference does. Changing either changes what a
    /// score is worked out from: it needs a new <see cref="ScoreCalculator.VERSION"/>, and the reference's lists with it.
    /// </summary>
    [Fact]
    public void TheCatalogSaysWhatTheReferenceSaysOfEveryCheck()
    {
        var reference = File.ReadAllText(Path.Combine(RepoFolder(), "app", "design", "simple", "build", "lib", "score.mjs"));
        Assert.Equal(Ids(reference, "SITE_WIDE"), IssueCatalog.All.Where(check => check.SiteWide).Select(check => check.Id).Order(StringComparer.Ordinal));

        // A site hidden from search is reported on its home page, which is a page: no URL check, here or in the reference.
        Assert.Equal(Ids(reference, "URL_SUBJECT"),
            IssueCatalog.All.Where(check => check.Subject == IssueSubject.Url).Select(check => check.Id).Order(StringComparer.Ordinal));

        static IEnumerable<string> Ids(string source, string name)
        {
            var list = source.Split($"export const {name} = new Set([")[1].Split("]);")[0];
            return list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim('\'')).Order(StringComparer.Ordinal);
        }
    }

    /// <summary>The points table, as the design prints it, and the notice row at nothing.</summary>
    [Fact]
    public void ThePointsAreTheDesignsTable()
    {
        ScoreExtent[] extents = [ScoreExtent.Few, ScoreExtent.Some, ScoreExtent.Many, ScoreExtent.Most, ScoreExtent.Site];
        Assert.Equal([2, 4, 7, 10, 60], extents.Select(extent => ScoreCalculator.Points(IssueSeverity.Error, extent)));
        Assert.Equal([1, 2, 3, 4, 4], extents.Select(extent => ScoreCalculator.Points(IssueSeverity.Warning, extent)));
        Assert.Equal([0, 0, 0, 0, 0], extents.Select(extent => ScoreCalculator.Points(IssueSeverity.Notice, extent)));
    }

    /// <summary>A few is under 1 in 100; some, 1 in 100 or more; many, 1 in 10 or more; most, half or more.</summary>
    [Theory]
    [InlineData(1, 101, ScoreExtent.Few)]
    [InlineData(1, 100, ScoreExtent.Some)]
    [InlineData(9, 100, ScoreExtent.Some)]
    [InlineData(10, 100, ScoreExtent.Many)]
    [InlineData(49, 100, ScoreExtent.Many)]
    [InlineData(50, 100, ScoreExtent.Most)]
    [InlineData(100, 100, ScoreExtent.Most)]
    [InlineData(150, 100, ScoreExtent.Most)]
    [InlineData(1, 5, ScoreExtent.Many)]
    [InlineData(1, 1, ScoreExtent.Most)]
    [InlineData(2_999, 300_000, ScoreExtent.Few)]
    [InlineData(3_000, 300_000, ScoreExtent.Some)]
    public void HowMuchOfTheSiteGoesByTheShare(long count, long of, ScoreExtent extent) => Assert.Equal(extent, ScoreCalculator.ExtentOf(count, of));

    /// <summary>90 and over is very good, 70 and over good, 50 and over needs work, under 50 poor.</summary>
    [Theory]
    [InlineData(100, ScoreBand.VeryGood)]
    [InlineData(90, ScoreBand.VeryGood)]
    [InlineData(89, ScoreBand.Good)]
    [InlineData(70, ScoreBand.Good)]
    [InlineData(69, ScoreBand.NeedsWork)]
    [InlineData(50, ScoreBand.NeedsWork)]
    [InlineData(49, ScoreBand.Poor)]
    [InlineData(0, ScoreBand.Poor)]
    public void TheWordGoesByTheNumber(int value, ScoreBand band) => Assert.Equal(band, ScoreCalculator.BandOf(value));

    /// <summary>
    /// The ceiling: while an error check has issues the score can't pass 89, however little it costs. Take the error away
    /// and the same points give more; a warning that costs the same never holds it.
    /// </summary>
    [Fact]
    public void WhileASeriousProblemStandsTheScoreCantPass89()
    {
        var held = Score(PAGES, URLS, ("broken-internal-links", 1));
        Assert.Equal((89, 98, true, ScoreBand.Good), (held.Value, held.BeforeCeiling, held.HeldBySerious, held.Band));

        var free = Score(PAGES, URLS, ("title-duplicate", 20));
        Assert.Equal((98, 98, false, ScoreBand.VeryGood), (free.Value, free.BeforeCeiling, free.HeldBySerious, free.Band));

        // Under the ceiling already, nothing is held: the number is the sum.
        var under = Score(PAGES, URLS, ("broken-internal-links", 1_000), ("title-duplicate", 20));
        Assert.Equal((88, 88, false), (under.Value, under.BeforeCeiling, under.HeldBySerious));

        // An error check that ran and found nothing is no problem standing.
        Assert.Equal(100, ScoreCalculator.Calculate([Check("broken-internal-links", 0)], 0, PAGES, URLS, null, 0)!.Value);

        // Fixing the serious problems lifts the ceiling from what is left.
        Assert.Equal(100, held.IfFixed.Value);
    }

    /// <summary>
    /// A site-wide fault is the whole site whatever its count: 60 points for an error and 4 for a warning, where the same
    /// count of a page's fault would be a few. Its count isn't a share of anything.
    /// </summary>
    [Fact]
    public void ASiteWideFaultCountsAsTheWholeSiteWhateverItsCount()
    {
        var score = Score(10_000, 50_000, ("no-https", 1), ("duplicate-site-version", 3), ("robots-txt-unreachable-other-origin", 1));

        // In the order to fix them: site-wide faults of one severity in catalog order, whatever their counts.
        Assert.Equal(
        [
            new ScoreLine("no-https", IssueSeverity.Error, 1, 50_000, ScoreUnit.Site, ScoreExtent.Site, 60),
            new ScoreLine("robots-txt-unreachable-other-origin", IssueSeverity.Warning, 1, 50_000, ScoreUnit.Site, ScoreExtent.Site, 4),
            new ScoreLine("duplicate-site-version", IssueSeverity.Warning, 3, 50_000, ScoreUnit.Site, ScoreExtent.Site, 4)
        ], score.Lines);
        Assert.Equal((32, 68), (score.Value, score.Lost));

        // The same check, were it not site-wide, would be one page of ten thousand: a few.
        var asAPage = ScoreCalculator.Calculate([new IssueCount(IssueCatalog.NO_HTTPS with { SiteWide = false }, 1)], 0, 10_000, 50_000, null, 0)!;
        Assert.Equal((ScoreUnit.Pages, ScoreExtent.Few, 2, 89), (asAPage.Lines[0].Unit, asAPage.Lines[0].Extent, asAPage.Lines[0].Points, asAPage.Value));
    }

    /// <summary>A page check's count is a share of the pages read; a check on any URL's, of the URLs crawled.</summary>
    [Fact]
    public void APageChecksShareIsOfThePagesAndAUrlChecksOfTheUrls()
    {
        var score = Score(100, 10_000, ("broken-internal-links", 50), ("internal-client-error", 50));

        Assert.Equal(
        [
            new ScoreLine("internal-client-error", IssueSeverity.Error, 50, 10_000, ScoreUnit.Urls, ScoreExtent.Few, 2),
            new ScoreLine("broken-internal-links", IssueSeverity.Error, 50, 100, ScoreUnit.Pages, ScoreExtent.Most, 10)
        ], score.Lines);
    }

    /// <summary>What "fixing these five" takes away: the first five that cost points, in fix-first order, never a notice.</summary>
    [Fact]
    public void FixingTheFirstFiveTakesAwayTheFirstFiveProblemsNotNotices()
    {
        var score = Score(PAGES, URLS, ("broken-internal-links", 10), ("title-duplicate", 10), ("description-missing", 900), ("title-too-long", 500));
        Assert.Equal(["broken-internal-links", "title-duplicate"], score.IfFixed.CheckIds);
        Assert.Equal((89, 100), (score.Value, score.IfFixed.Value));

        // Seven problems: the first five go, and the last two still cost what they cost.
        var seven = Score(PAGES, URLS, ("broken-internal-links", 700), ("title-missing", 600), ("image-broken", 500), ("mixed-content", 400),
            ("redirect-loop", 300), ("internal-client-error", 200), ("title-duplicate", 100));
        Assert.Equal(["broken-internal-links", "title-missing", "image-broken", "mixed-content", "redirect-loop"], seven.IfFixed.CheckIds);
        Assert.Equal(ScoreCalculator.FIX_FIRST, seven.IfFixed.CheckIds.Count);

        // internal-client-error is 200 of 5,000 URLs (some: 4) and still serious, title-duplicate is many (3): 93, held at 89.
        Assert.Equal(89, seven.IfFixed.Value);

        // Nothing to fix: nothing is taken away.
        var clean = Score(PAGES, URLS, ("description-missing", 3));
        Assert.Empty(clean.IfFixed.CheckIds);
        Assert.Equal(100, clean.IfFixed.Value);
    }

    /// <summary>What the score covers travels with it, and a crawl that didn't read all of the site gives a partial score.</summary>
    [Fact]
    public void TheScoreSaysWhatItCoversAndWhenItIsPartial()
    {
        List<IssueCount> checks = [Check("title-duplicate", 20), Check("title-missing", 0), Check("h1-missing", 0)];

        var whole = ScoreCalculator.Calculate(checks, checksSkipped: 2, 312, 2_104, incomplete: null, blockedByRobots: 7)!;
        Assert.False(whole.Partial);
        Assert.Equal(new ScoreCoverage(312, 2_104, 3, 2, Complete: true, Reason: null, 7), whole.Covers);

        foreach (var reason in Enum.GetValues<ScoreIncompleteReason>())
        {
            var part = ScoreCalculator.Calculate(checks, 2, 312, 2_104, reason, 7)!;
            Assert.True(part.Partial);
            Assert.Equal(new ScoreCoverage(312, 2_104, 3, 2, Complete: false, reason, 7), part.Covers);

            // Partial says what was read; it changes no number.
            Assert.Equal((whole.Value, whole.Lost), (part.Value, part.Lost));
        }
    }

    /// <summary>No page read is no score, whatever was flagged: there is nothing honest to say.</summary>
    [Fact]
    public void WithNoPageReadThereIsNoScore()
    {
        Assert.Null(ScoreCalculator.Calculate([], 0, pages: 0, urls: 0, null, 0));
        Assert.Null(ScoreCalculator.Calculate([Check("internal-client-error", 1), Check("robots-txt-unreachable", 1)], 3, pages: 0, urls: 12, ScoreIncompleteReason.Stopped, 0));
        Assert.NotNull(ScoreCalculator.Calculate([], 0, pages: 1, urls: 1, null, 0));
    }

    /// <summary>
    /// On any site: the score is within 0 to 100, never passes 89 with a serious problem, is 100 with none but notices,
    /// and never rises when a problem is added or one there already spreads. 2,000 sites from a fixed seed.
    /// </summary>
    [Fact]
    public void OnAnySiteTheScoreKeepsItsPromises()
    {
        var random = new Random(20261111);
        for (var site = 0; site < 2_000; site++)
        {
            long pages = random.Next(4) switch { 0 => random.Next(1, 10), 1 => random.Next(10, 500), 2 => random.Next(500, 50_000), _ => random.Next(50_000, 2_000_000) };
            var urls = pages + random.NextInt64(0, pages * 5);
            var picked = IssueCatalog.All.OrderBy(_ => random.Next()).Take(random.Next(0, 25)).ToList();
            List<IssueCount> checks = [.. picked.Select(check => new IssueCount(check, random.Next(4) == 0 ? 0 : random.NextInt64(1, urls + 10)))];

            var score = ScoreCalculator.Calculate(checks, 0, pages, urls, null, 0)!;
            var found = checks.Where(check => check.Count > 0).ToList();
            var summary = $"site {site}: {pages} pages, {urls} URLs, {string.Join(", ", found.Select(check => $"{check.Issue.Id} x{check.Count}"))}";

            Assert.True(score.Value is >= 0 and <= 100, summary);
            Assert.Equal((summary, ScoreCalculator.BandOf(score.Value)), (summary, score.Band));
            Assert.Equal((summary, Math.Max(0, 100 - score.Lost)), (summary, score.BeforeCeiling));
            if (found.Any(check => check.Issue.Severity == IssueSeverity.Error))
            {
                Assert.True(score.Value <= ScoreCalculator.SERIOUS_CEILING, summary);
            }

            if (found.All(check => check.Issue.Severity == IssueSeverity.Notice))
            {
                Assert.True(score.Value == 100, summary);
            }

            // Another problem, of any check that hadn't found one.
            var spare = IssueCatalog.All.Except(found.Select(check => check.Issue)).ToList();
            var added = new IssueCount(spare[random.Next(spare.Count)], random.NextInt64(1, urls + 10));
            var withAdded = ScoreCalculator.Calculate([.. checks.Where(check => check.Issue != added.Issue), added], 0, pages, urls, null, 0)!;
            Assert.True(withAdded.Value <= score.Value, $"{summary}; adding {added.Issue.Id} x{added.Count} raised {score.Value} to {withAdded.Value}");

            // A problem there already, on more of the site.
            if (found.Count > 0)
            {
                var grown = found[random.Next(found.Count)];
                var more = grown with { Count = grown.Count + random.NextInt64(1, urls) };
                var withMore = ScoreCalculator.Calculate([.. checks.Select(check => check == grown ? more : check)], 0, pages, urls, null, 0)!;
                Assert.True(withMore.Value <= score.Value, $"{summary}; {grown.Issue.Id} growing to {more.Count} raised {score.Value} to {withMore.Value}");
            }

            // Fixing problems never lowers it.
            Assert.True(score.IfFixed.Value >= score.Value, summary);
        }
    }

    private static SiteScore Score(long pages, long urls, params (string Id, long Count)[] checks) =>
        ScoreCalculator.Calculate([.. checks.Select(check => Check(check.Id, check.Count))], 0, pages, urls, null, 0)!;

    private static IssueCount Check(string id, long count) => new(IssueCatalog.Find(id)!, count);

    private static ScoreBand Band(string id) => id switch
    {
        "very-good" => ScoreBand.VeryGood,
        "good" => ScoreBand.Good,
        "needs-work" => ScoreBand.NeedsWork,
        "poor" => ScoreBand.Poor,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
    };

    /// <summary>The reference counts in pages, addresses (Simple's word for URLs) or the whole site.</summary>
    private static ScoreUnit Unit(string unit) => unit switch
    {
        "pages" => ScoreUnit.Pages,
        "addresses" => ScoreUnit.Urls,
        "site" => ScoreUnit.Site,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
    };

    private static string ReferenceCases() =>
        Path.Combine(RepoFolder(), "engine", "tests", "ShoutingIguana.Engine.Tests", "Analysis", "ScoreReference", "cases.json");

    private static string RepoFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ShoutingIguana.Engine.slnx")))
            {
                return folder.Parent!.FullName;
            }
        }

        throw new InvalidOperationException($"No engine folder above {AppContext.BaseDirectory}.");
    }
}
