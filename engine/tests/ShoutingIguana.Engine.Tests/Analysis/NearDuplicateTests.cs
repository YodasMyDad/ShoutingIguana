using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The near-duplicate check: pages are near copies when all 256 bits of their fingerprints are within the threshold, every
/// such pair is found however big the crawl and in whatever order, and nothing else is, chance collisions of one lane and
/// pages that only share a template included.
/// </summary>
public sealed class NearDuplicateTests
{
    private const string SITE = "http://127.0.0.1:50556";
    private static readonly Uri ANY_URI = new("http://site.test/");

    /// <summary>
    /// Two unrelated product pages of the 1M-page benchmark site whose first 64-bit SimHashes are 6 bits apart, as one pair
    /// in 2×10^11 is: the old check flagged them. The rest of the 256 bits tell them apart, and the planted one-word edit
    /// is still found.
    /// </summary>
    [Fact]
    public void ChanceCollisionsOfTheFirstLaneArentNearDuplicates()
    {
        var harness = new RuleHarness(SITE);
        var site = new SyntheticSite(new SyntheticSiteOptions { PageCount = 1_000_000, DeadTwin = true }, new Uri(SITE), new Uri("http://127.0.0.1:50557"));
        AnalyzedPage Render(string path) => harness.Page(SITE + path, site.Handle(path, external: false).Text!);
        AnalyzedPage[] pages = [Render("/p/516369"), Render("/p/623474"), Render("/plants/content-near-a"), Render("/plants/content-near-b")];

        Assert.InRange(ContentFingerprint.Distance(pages[0].Facts.SimHash, pages[1].Facts.SimHash), 0, 6);
        Assert.InRange(Distance(pages[0], pages[1]), 3 * new AnalysisSettings().NearDuplicateMaxBits, 256);
        Assert.Equal([pages[2].UrlId, pages[3].UrlId], NearDuplicatePages(new AnalysisSettings(), pages));
    }

    /// <summary>
    /// The synthetic site plants two collisions of the first lane, so every size of it, CI's 100k benchmark included, carries
    /// one: unrelated text, and text sharing half its words. A change to how the site writes its text would quietly disarm
    /// them, so this checks they still collide, and still aren't near copies.
    /// </summary>
    [Theory]
    [InlineData("/plants/collision-a", "/plants/collision-b")]
    [InlineData("/plants/template-a", "/plants/template-b")]
    public void ThePlantedCollisionsStillCollide(string left, string right)
    {
        var harness = new RuleHarness(SITE);
        var site = new SyntheticSite(new SyntheticSiteOptions(), new Uri(SITE), new Uri("http://127.0.0.1:50557"));
        AnalyzedPage[] pages = [harness.Page(SITE + left, site.Handle(left, external: false).Text!), harness.Page(SITE + right, site.Handle(right, external: false).Text!)];

        Assert.InRange(ContentFingerprint.Distance(pages[0].Facts.SimHash, pages[1].Facts.SimHash), 0, 6);
        Assert.InRange(Distance(pages[0], pages[1]), 2 * new AnalysisSettings().NearDuplicateMaxBits, 256);
        Assert.Empty(NearDuplicatePages(new AnalysisSettings(), pages));
    }

    /// <summary>
    /// A near copy 6 bits away in the first lane, agreeing exactly on only one of the 7 blocks the old check bucketed it by,
    /// with more than 1,000 pages sharing that block crawled in between: the old check compared each page with the next
    /// 1,000 of its bucket only, so it never met. Two-block keys put them in a bucket of their own.
    /// </summary>
    [Fact]
    public void NearCopiesFarApartInCrawlOrderAreFound()
    {
        var random = new Random(11);
        var original = Fingerprint(random);

        // One bit in each of the old blocks 1 to 6 (bits 9 to 17, 18 to 26, ...), so only bits 0 to 8 still agree.
        var copy = original with
        {
            ContentHash = Next(random),
            Lane0 = original.Lane0 ^ (1UL << 13 | 1UL << 22 | 1UL << 31 | 1UL << 40 | 1UL << 49 | 1UL << 58),
            Lanes = new SimHashLanes(original.Lanes.Lane1 ^ 0b11, original.Lanes.Lane2 ^ 0b1100, original.Lanes.Lane3 ^ 0b110000)
        };
        var fillers = Enumerable.Range(0, 1_100).Select(_ =>
        {
            var filler = Fingerprint(random);
            return filler with { Lane0 = (filler.Lane0 & ~0x1FFUL) | (original.Lane0 & 0x1FFUL) };
        });

        List<PageFingerprint> crawl = [original, .. fillers, copy];
        Assert.Equal(12, crawl[0].Distance(crawl[^1]));
        Assert.Equal([1, crawl.Count], NearDuplicatePages(new AnalysisSettings(), crawl.Select((fingerprint, index) => Page(fingerprint with { UrlId = index + 1 }))));
    }

    /// <summary>
    /// The pairs hardest to find: exactly at the threshold, spread evenly over the four lanes, each lane's bits one to a
    /// block, so each lane agrees on just two blocks and shares a single key. Found at every threshold the settings allow,
    /// wide keys hashed down included, and a pair one bit further is not.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(27)]
    [InlineData(32)]
    public void PairsAtTheThresholdAreFoundHoweverTheirBitsFall(int maxBits)
    {
        var settings = new AnalysisSettings { NearDuplicateMaxBits = maxBits };
        var random = new Random(maxBits);
        var original = Fingerprint(random);
        var blocks = maxBits / ContentFingerprint.LANES + 2;
        PageFingerprint Apart(int bits, int id)
        {
            var lanes = new ulong[ContentFingerprint.LANES];
            for (var lane = 0; lane < lanes.Length; lane++)
            {
                lanes[lane] = original.Lane(lane);
                var differing = bits / ContentFingerprint.LANES + (lane < bits % ContentFingerprint.LANES ? 1 : 0);
                for (var block = 0; block < differing; block++)
                {
                    lanes[lane] ^= 1UL << (block * 64 / blocks);
                }
            }

            return new PageFingerprint(id, Next(random), lanes[0], new SimHashLanes(lanes[1], lanes[2], lanes[3]));
        }

        List<PageFingerprint> unrelated = [.. Enumerable.Range(3, 2_000).Select(id => Fingerprint(random) with { UrlId = id })];
        PageFingerprint[] atThreshold = [original with { UrlId = 1 }, Apart(maxBits, 2), .. unrelated];
        PageFingerprint[] pastIt = [original with { UrlId = 1 }, Apart(maxBits + 1, 2), .. unrelated];

        Assert.Equal([1, 2], NearDuplicatePages(settings, atThreshold.Select(Page)));
        Assert.Empty(NearDuplicatePages(settings, pastIt.Select(Page)));
    }

    /// <summary>
    /// Pages sharing 70% of their words (a template), the rest their own: about 70% alike, which the old check's 64 bits
    /// took for near copies by the thousand. None is one, while one-word edits among them still are.
    /// </summary>
    [Fact]
    public void PagesSharingATemplateArentNearDuplicatesButTheirNearCopiesAre()
    {
        const int PAGES = 2_000;
        const int EDITED = 20;
        var random = new Random(70);
        var template = Enumerable.Range(0, 280).Select(_ => $"t{random.Next(50_000)}").ToArray();
        var texts = Enumerable.Range(0, PAGES).Select(_ => template.Concat(Enumerable.Range(0, 120).Select(_ => $"w{random.Next(50_000)}")).ToArray()).ToList();

        // The last pages are one-word edits of the first ones.
        for (var edit = 0; edit < EDITED; edit++)
        {
            var copy = (string[])texts[edit].Clone();
            copy[340] = "edited";
            texts[PAGES - EDITED + edit] = copy;
        }

        var pages = texts.Select((words, index) =>
        {
            var fingerprint = ContentFingerprint.Compute(string.Join(' ', words));
            return Page(new PageFingerprint(index + 1, fingerprint.ContentHash, fingerprint.SimHash, fingerprint.Lanes));
        }).ToList();
        var oldCheckWouldFlag = pages.Take(PAGES - EDITED).Count(page => pages.Take(PAGES - EDITED).Any(other =>
            other.UrlId != page.UrlId && ContentFingerprint.Distance(page.Facts.SimHash, other.Facts.SimHash) <= 6));

        Assert.True(oldCheckWouldFlag > 100, $"Only {oldCheckWouldFlag} pages collide in the first lane");
        Assert.Equal([.. Enumerable.Range(1, EDITED), .. Enumerable.Range(PAGES - EDITED + 1, EDITED)], NearDuplicatePages(new AnalysisSettings(), pages));
    }

    /// <summary>
    /// 2,000 near copies of one page, alike in three lanes and a bit apart in the fourth, share every key of those three,
    /// in a bucket far fuller than unrelated pages ever make one. They're not all compared with one another: sorted by the
    /// lane, each meets the 64 after it, and a bucket that is one group already is passed over. They still form one group,
    /// joined through their neighbours, within the documented bound (112 keys × pages × 64 comparisons, at this size) and
    /// in fewer comparisons than comparing every pair once.
    /// </summary>
    [Fact]
    public void ADenseClusterIsOneGroupWithinTheBound()
    {
        const int COPIES = 2_000;
        var random = new Random(3);
        var original = Fingerprint(random);
        var rules = new DuplicateRules(new AnalysisSettings());
        for (var copy = 0; copy < COPIES; copy++)
        {
            var lanes = original.Lanes with { Lane3 = original.Lanes.Lane3 ^ (1UL << (copy % 64)) };
            rules.Observe(Page(original with { UrlId = copy + 1, ContentHash = Next(random), Lanes = lanes }));
        }

        var hits = rules.Finish().Where(hit => hit.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT).ToList();

        Assert.Equal(COPIES, hits.Count);
        Assert.All(hits, hit => Assert.Equal($"{COPIES - 1:N0} similar pages", hit.Detail));
        Assert.InRange(rules.Comparisons, 1, 112L * COPIES * 64);
        Assert.True(rules.Comparisons < (long)COPIES * (COPIES - 1) / 2, $"{rules.Comparisons:N0} comparisons");
    }

    /// <summary>
    /// A page an earlier version crawled has only the first lane. The rule counts such pages, so the analysis can skip the
    /// check, and doesn't search: any page could be the near copy of one of them.
    /// </summary>
    [Fact]
    public void PagesWithoutTheWholeFingerprintAreCountedNotCompared()
    {
        var random = new Random(5);
        var original = Fingerprint(random);
        var rules = new DuplicateRules(new AnalysisSettings());
        rules.Observe(Page(original with { UrlId = 1 }));
        rules.Observe(Page(original with { UrlId = 2, ContentHash = 2 }));
        rules.Observe(new AnalyzedPage
        {
            UrlId = 3, Url = ANY_URI.AbsoluteUri, Uri = ANY_URI,
            Facts = new PageFacts { WordCount = 500, ContentHash = 3, SimHash = original.Lane0 }
        });

        Assert.Equal((2, 1), (rules.PagesWithFingerprint, rules.PagesWithoutFingerprint));
        Assert.DoesNotContain(rules.Finish(), hit => hit.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT);
    }

    private static List<int> NearDuplicatePages(AnalysisSettings settings, IEnumerable<AnalyzedPage> pages)
    {
        var rules = new DuplicateRules(settings);
        foreach (var page in pages)
        {
            rules.Observe(page);
        }

        return [.. rules.Finish().Where(hit => hit.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT).Select(hit => hit.UrlId).Order()];
    }

    private static int Distance(AnalyzedPage left, AnalyzedPage right) =>
        ContentFingerprint.Distance(left.Facts.SimHash, left.Facts.SimHashLanes!.Value, right.Facts.SimHash, right.Facts.SimHashLanes!.Value);

    private static PageFingerprint Fingerprint(Random random) =>
        new(0, Next(random), Next(random), new SimHashLanes(Next(random), Next(random), Next(random)));

    private static ulong Next(Random random)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>An indexable page with enough words, and with this fingerprint.</summary>
    private static AnalyzedPage Page(PageFingerprint fingerprint) => new()
    {
        UrlId = fingerprint.UrlId, Url = ANY_URI.AbsoluteUri, Uri = ANY_URI,
        Facts = new PageFacts { WordCount = 500, ContentHash = fingerprint.ContentHash, SimHash = fingerprint.Lane0, SimHashLanes = fingerprint.Lanes }
    };
}
