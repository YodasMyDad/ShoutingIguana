using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// Crawls of the synthetic site dressed as a Shopify store, whose product pages refuse an unsigned crawler after two
/// requests and let a signed one through: the store is told as such and kept as a crawl fact, a product it refused is
/// "not checked" for Shopify's reason (never broken), a signed crawl or recheck reads them all, and the signature goes
/// to the store's own host alone and is never kept, logged or shown.
/// </summary>
public sealed class ShopifyCrawlTests : IDisposable
{
    private const string PRODUCTS = "FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.is_internal = 1 AND u.url LIKE '%/p/%'";
    private static readonly string REFUSED_UNSIGNED = $"{RetryingFetcher.RATE_LIMITED_ERROR} (429 Too Many Requests): {ShopifySignals.UNSIGNED_REFUSAL}";
    private static readonly string REFUSED_SIGNED = $"{RetryingFetcher.RATE_LIMITED_ERROR} (429 Too Many Requests): {ShopifySignals.SIGNED_REFUSAL}: " +
                                                    "it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain";

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task AnUnsignedCrawlLeavesTheProductsNotCheckedBecauseShopifyLimitsCrawlersWithoutASignature()
    {
        await using var server = await StartStoreAsync();
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);

        var summary = await CrawlAsync(Options(server), path, time, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        var products = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS}");
        var refused = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error = '{REFUSED_UNSIGNED.Replace("'", "''", StringComparison.Ordinal)}'");
        var read = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.status_code = 200");
        Assert.True(products >= 4, $"{products} product URLs");
        Assert.True(read <= 2, $"{read} products read");
        Assert.Equal(products, refused + read);

        // Kept as a fact of the crawl, from the first response that said so: the home page's headers.
        var platform = SitePlatformReader.Read(path);
        Assert.Equal(ShopifySignals.PLATFORM, platform?.Name);
        Assert.Equal("powered-by: Shopify", platform?.Signal);

        // Said as it happened, in plain words with what to do: one alert, said again only as its count grew, never a page's.
        var notices = observer.Notices.Where(notice => notice.Code == "shopify-unsigned").ToList();
        var notice = notices[0];
        Assert.Equal(CrawlNoticeLevel.Warning, notice.Level);
        // Product pages are asked in parallel, so the first alert may already count more than one.
        Assert.Matches($"^{System.Text.RegularExpressions.Regex.Escape(server.BaseUri.Host)} runs on Shopify, and Shopify is refusing some of its pages because the crawl isn't signed: " +
                       $"{notice.Data!.Count} so far[.] They'll be marked not checked, not broken[.] To fix it, in Shopify admin go to Online Store › Preferences › Crawler access, " +
                       "choose Create signature", notice.Message);
        Assert.DoesNotContain("429", notice.Message, StringComparison.Ordinal);
        Assert.Null(notice.Data!.Reason);
        var counts = notices.Select(said => said.Data!.Count!.Value).ToList();
        Assert.Equal(counts.Order(), counts);
        Assert.Equal(counts.Distinct().Count(), counts.Count);
        Assert.InRange(counts[^1], 1, refused);
        Assert.Equal(0, observer.Count("shopify-signature-refused"));

        // What the results need: how many weren't checked, why, and which to recheck.
        var access = ShopifyAccessReader.Read(path);
        Assert.NotNull(access);
        Assert.Equal(refused, access.NotCheckedUnsigned);
        Assert.Equal(0, access.NotCheckedSigned);
        Assert.Equal(refused, access.NotCheckedUrlIds.Count);

        // A crawl made before the platform was kept (as the owner's Jitsunami crawls were) is told by its first page's
        // stored headers, and its refusals counted the same.
        var earlier = _files.NewPath();
        File.Copy(path, earlier);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={earlier};Pooling=False"))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE site_platform";
            drop.ExecuteNonQuery();
        }

        Assert.Null(SitePlatformReader.Read(earlier));
        var fromHeaders = ShopifyAccessReader.Read(earlier);
        Assert.Equal("powered-by: Shopify", fromHeaders?.Signal);
        Assert.Equal((int)refused, fromHeaders?.NotCheckedUnsigned);

        // Not checked is no fault of the pages: no check calls them broken or unreachable.
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        string[] failures = ["internal-blocked", "internal-client-error", "internal-server-error", "internal-unreachable", "broken-internal-links"];
        Assert.DoesNotContain(IssueReader.ReadAll(path), row => failures.Contains(row.Issue) && (row.Url.Contains("/p/", StringComparison.Ordinal) ||
            row.RelatedUrl?.Contains("/p/", StringComparison.Ordinal) == true));

        // Nor does any other: a page Shopify refused is in no check's count, so it never moves the score.
        var refusedUrls = TestCrawls.Rows(path, $"SELECT u.url {PRODUCTS} AND r.error LIKE '%{ShopifySignals.UNSIGNED_REFUSAL}%'").ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(IssueReader.ReadAll(path), row => refusedUrls.Contains(row.Url) || row.RelatedUrl is { } related && refusedUrls.Contains(related));
        Assert.Equal(0, server.ShopifySigned);
    }

    [Fact]
    public async Task ASignedCrawlReadsEveryProductAndSendsTheSignatureNowhereElse()
    {
        await using var server = await StartStoreAsync();
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        var logs = new CapturingLoggerFactory();

        var summary = await CrawlAsync(Signed(Options(server), server) with { StoreHtml = true }, path, time, new RecordingObserver(time), logs);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        var products = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS}");
        Assert.True(products >= 4, $"{products} product URLs");
        Assert.Equal(products, TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.status_code = 200"));
        Assert.True(server.ShopifySigned >= products, $"{server.ShopifySigned} signed requests");
        Assert.Equal(0, ShopifyAccessReader.Read(path)!.NotCheckedUnsigned);

        // The home page links the store under another host's name over https, and redirects there: both were asked
        // for, and neither, nor the external site, ever got the signature.
        Assert.Equal(2, TestCrawls.Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'https://localhost:%' AND r.status_code IS NOT NULL"));
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE url LIKE '%{SyntheticShopify.AWAY_PATH}'"));
        Assert.Equal(0, server.ShopifySignedElsewhere);

        // Never kept, logged or said.
        AssertNoSignatureIn(path);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(SyntheticShopify.KEY_ID, StringComparison.Ordinal) ||
                                                        message.Contains(SyntheticShopify.SIGNATURE, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASignedRecheckOfThePagesShopifyRefusedReadsThem()
    {
        await using var server = await StartStoreAsync();
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        await CrawlAsync(Options(server), path, time, new RecordingObserver(time));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var refused = ShopifyAccessReader.Read(path)!.NotCheckedUrlIds;
        Assert.NotEmpty(refused);

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk(null, UrlIds: [.. refused]), stored => Signed(stored, server) with
        {
            TrustedCertificate = server.Certificate
        });

        Assert.Equal(CrawlStatus.Completed, run.Status);
        Assert.Equal(refused.Count, recheck.Judged);
        Assert.Equal(refused.Count, TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.status_code = 200 AND r.url_id IN ({string.Join(',', refused)})"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error IS NOT NULL"));
        Assert.Equal(0, ShopifyAccessReader.Read(path)!.NotCheckedUnsigned);
        Assert.Equal(0, server.ShopifySignedElsewhere);
        AssertNoSignatureIn(path);
    }

    [Fact]
    public async Task ASignatureTheStoreRefusesSaysItMayBeInvalidExpiredOrForAnotherDomain()
    {
        await using var server = await StartStoreAsync(signatureFor: "www.shop.example");
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);

        await CrawlAsync(Signed(Options(server), server), path, time, observer);

        // Refused when asked, or never asked before the section's time ran out (the crawl's end, under load): either way
        // a signed crawl was refused, and said so, never "without a signature".
        var asked = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error = '{REFUSED_SIGNED.Replace("'", "''", StringComparison.Ordinal)}'");
        var refused = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error LIKE '%{ShopifySignals.SIGNED_REFUSAL.Replace("'", "''", StringComparison.Ordinal)}: %'");
        Assert.True(asked > 0, $"{asked} refused when asked");
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error LIKE '%{ShopifySignals.UNSIGNED_REFUSAL}%'"));
        var notice = Assert.Single(observer.Notices, notice => notice.Code == "shopify-signature-refused");
        Assert.Equal(
            $"{server.BaseUri.Host} runs on Shopify, and Shopify refused pages even with the signature: it may not be active yet (a new one can take up to an hour), " +
            "may have expired, or may be for another domain. The crawl asks again for them at its end; any still refused are marked not checked, not broken.",
            notice.Message);
        Assert.Equal(0, observer.Count("shopify-unsigned"));
        var access = ShopifyAccessReader.Read(path)!;
        Assert.Equal(refused, access.NotCheckedSigned);
        Assert.Equal(0, access.NotCheckedUnsigned);
        Assert.Equal(["it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain"], access.Reasons);
    }

    [Fact]
    public async Task ASignatureForTheWwwTwinIsNamedAsTheReasonTheStoreStillRefuses()
    {
        await using var server = await StartStoreAsync();
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        var twin = $"www.{server.BaseUri.Authority}";
        var options = Options(server) with
        {
            Signatures = new Dictionary<string, CrawlSignature>
            {
                [twin] = new() { SignatureInput = SyntheticShopify.SIGNATURE_INPUT, Signature = SyntheticShopify.SIGNATURE }
            }
        };

        await CrawlAsync(options, path, time, observer);

        var reason = $"the signature is for {twin}, not {server.BaseUri.Authority}, and each domain needs its own";
        var refused = TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error = '{RetryingFetcher.RATE_LIMITED_ERROR} (429 Too Many Requests): {ShopifySignals.UNSIGNED_REFUSAL}: {reason}'");
        Assert.True(refused > 0, $"{refused} refused");
        var notice = observer.Notices.First(notice => notice.Code == "shopify-unsigned");
        Assert.Contains($"because the crawl isn't signed ({reason})", notice.Message, StringComparison.Ordinal);
        Assert.Equal(reason, notice.Data!.Reason);
        // A product the section held back and gave up on without asking again has Shopify's limit alone as its reason.
        var access = ShopifyAccessReader.Read(path)!;
        Assert.Equal(TestCrawls.Count(path, $"SELECT COUNT(*) {PRODUCTS} AND r.error LIKE '%{ShopifySignals.UNSIGNED_REFUSAL}%'"), access.NotCheckedUnsigned);
        Assert.Equal([reason], access.Reasons);
        Assert.Equal(0, server.ShopifySigned);
    }

    [Fact]
    public async Task AnOrdinarySiteRefusingWithA429KeepsTheGeneralReason()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions
        {
            PageCount = 3,
            RateLimit = new SyntheticRateLimit(MaxRequests: 1, Window: TimeSpan.FromSeconds(1), Penalty: TimeSpan.FromSeconds(1), NeverRelents: true)
        });
        var path = _files.NewPath();
        var time = new FakeTimeProvider();

        await CrawlAsync(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 }, path, time, new RecordingObserver(time));

        Assert.Null(SitePlatformReader.Read(path));
        Assert.Null(ShopifyAccessReader.Read(path));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE error LIKE '%Shopify%'"));
    }

    private static Task<SyntheticSiteServer> StartStoreAsync(string? signatureFor = null) => SyntheticSiteServer.StartAsync(new SyntheticSiteOptions
    {
        PageCount = 6,
        Https = true,
        Shopify = new SyntheticShopify(UnsignedProductRequests: 2, SignatureFor: signatureFor)
    });

    private static CrawlOptions Options(SyntheticSiteServer server) =>
        new() { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8, TrustedCertificate = server.Certificate };

    /// <summary>The store's signature, for its own host and port.</summary>
    private static CrawlOptions Signed(CrawlOptions options, SyntheticSiteServer server) => options with
    {
        Signatures = new Dictionary<string, CrawlSignature>
        {
            [server.BaseUri.Authority] = new() { SignatureInput = SyntheticShopify.SIGNATURE_INPUT, Signature = SyntheticShopify.SIGNATURE }
        }
    };

    private static async Task<CrawlSummary> CrawlAsync(CrawlOptions options, string path, FakeTimeProvider time, ICrawlObserver observer,
        ILoggerFactory? logs = null)
    {
        await using var job = new CrawlEngine(logs ?? NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, path), observer);
        return await FakeClock.PumpAsync(time, job.Completion, TimeSpan.FromSeconds(1), realLimit: TimeSpan.FromMinutes(2));
    }

    /// <summary>Neither value, nor the key id within one, anywhere in the file as SQLite wrote it: options, notices, errors.</summary>
    private static void AssertNoSignatureIn(string path)
    {
        foreach (var file in new[] { path, path + "-wal" }.Where(File.Exists))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var text = Encoding.Latin1.GetString(copy.ToArray());
            Assert.DoesNotContain(SyntheticShopify.KEY_ID, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticShopify.SIGNATURE, text, StringComparison.Ordinal);
        }
    }
}
