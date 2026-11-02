using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Lanes 1 to 3 of each page's content fingerprint live in columns the crawl writer adds whenever they're missing, so the
/// crawl's schema version stays as it was: a file from before them still opens and gets them when continued, and an
/// earlier engine can still read and continue a file that has them.
/// </summary>
public sealed class FingerprintLaneTests : IAsyncLifetime
{
    /// <summary>The columns an earlier engine's writer stores a response into, as it names them.</summary>
    private const string EARLIER_COLUMNS = """
        url_id, fetched_at, outcome, status_code, content_type, size_bytes, body_bytes, ttfb_ms, response_ms,
        http_version, redirect_url_id, block_reason, error, is_html, title, title_count, meta_description,
        meta_description_count, h1, h1_count, canonical_url_id, is_noindex, is_nofollow, indexability,
        indexability_reason, word_count, content_hash, simhash, language, outlink_count
        """;

    private const string NEAR_COPY = "/plants/content-near-a";

    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _site = null!;

    public async Task InitializeAsync() => _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _site.DisposeAsync();
        _files.Dispose();
    }

    /// <summary>
    /// The lanes are stored as the parser computed them, in their own columns and not again with the rest of the facts,
    /// and the analysis reads them back from there.
    /// </summary>
    [Fact]
    public async Task TheLanesAreStoredAsParsedAndReadBack()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path)).Status);
        var parsed = HtmlPageParser.Parse(_site.Site.Handle(NEAR_COPY, external: false).Text!, new Uri(_site.BaseUri, NEAR_COPY), [], "ShoutingIguana").Facts.SimHashLanes;

        using var reader = AnalysisReader.Open(path);
        var page = reader.ReadPages(reader.ReadUrls()).Single(page => page.Url.EndsWith(NEAR_COPY, StringComparison.Ordinal));
        Assert.NotNull(parsed);
        Assert.Equal(parsed, page.Facts.SimHashLanes);
        Assert.Equal(Signed(parsed.Value), TestCrawls.Rows(path, $"SELECT simhash_1, simhash_2, simhash_3 FROM responses WHERE url_id = {page.UrlId}").Single());
        Assert.Null(reader.ReadFacts(page.UrlId)!.SimHashLanes);
        Assert.DoesNotContain("simHashLanes", BlobCodec.DecompressText(Details(path, page.UrlId)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE {CrawlSchema.PAGE} AND r.simhash_1 IS NULL"));
    }

    /// <summary>
    /// What an engine from before the lanes leaves: a responses table without their columns. It opens and reads as before;
    /// continuing it adds them, and the pages fetched from then on have them.
    /// </summary>
    [Fact]
    public async Task AFileFromBeforeTheLanesGetsTheirColumnsWhenItIsContinued()
    {
        var path = _files.NewPath();
        await using (CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        Execute(path, string.Concat(CrawlSchema.RESPONSE_COLUMNS.Select(column => $"ALTER TABLE responses DROP COLUMN {column.Name};")));
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM pragma_table_info('responses') WHERE name LIKE 'simhash_%'"));

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path, resume: true)).Status);
        Assert.Equal(3, TestCrawls.Count(path, "SELECT COUNT(*) FROM pragma_table_info('responses') WHERE name LIKE 'simhash_%'"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE {CrawlSchema.PAGE} AND r.simhash_1 IS NULL"));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(2, IssueReader.ReadAll(path, IssueCatalog.NEAR_DUPLICATE_CONTENT.Id).Count());
    }

    /// <summary>
    /// An earlier engine checks the crawl's schema version, which the lanes didn't change, and stores a response naming the
    /// columns it knows, so a page it fetches again into a file that has the lanes is stored without them. Readers that
    /// name their columns read the file as before.
    /// </summary>
    [Fact]
    public async Task AnEarlierEngineCanStillContinueAndReadAFileWithTheLanes()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path)).Status);
        var page = TestCrawls.Count(path, $"SELECT id FROM urls WHERE url LIKE '%{NEAR_COPY}'");

        Execute(path, $"INSERT OR REPLACE INTO responses ({EARLIER_COLUMNS}) SELECT {EARLIER_COLUMNS} FROM responses WHERE url_id = {page}");

        Assert.Equal(CrawlSchema.VERSION, TestCrawls.Count(path, "PRAGMA user_version"));
        Assert.Equal(["null | null | null"], TestCrawls.Rows(path, $"SELECT simhash_1, simhash_2, simhash_3 FROM responses WHERE url_id = {page}"));
        Assert.Equal(_site.Site.GetExpectations().HtmlPages, CrawlStatsReader.Read(path).HtmlPageCount);
        using var reader = AnalysisReader.Open(path);
        Assert.Null(reader.ReadPages(reader.ReadUrls()).Single(read => read.UrlId == page).Facts.SimHashLanes);
    }

    private CrawlOptions Options() => new() { StartUrl = _site.BaseUri.AbsoluteUri, Concurrency = 8 };

    /// <summary>The lanes as the columns hold them: their bits as signed 64-bit integers.</summary>
    private static string Signed(SimHashLanes lanes) => string.Join(" | ",
        new[] { lanes.Lane1, lanes.Lane2, lanes.Lane3 }.Select(lane => unchecked((long)lane).ToString(CultureInfo.InvariantCulture)));

    private static byte[] Details(string path, int urlId)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return (byte[])CrawlDatabase.Scalar(connection, $"SELECT data FROM response_details WHERE url_id = {urlId}")!;
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
