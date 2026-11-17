using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// A check that needs a page fact only a newer collection has (<see cref="CollectedFacts"/>) is skipped on a crawl with any
/// page parsed before it, saying why, and nothing it found on the other pages is kept: a fact that wasn't collected isn't
/// the page's lacking it.
/// </summary>
public sealed class CollectedFactsTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ChecksNeedingNewerFactsAreSkippedWhenAPageWasParsedBeforeThem()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 }, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);
        Assert.Empty(analysis.Skipped);
        Assert.Single(IssueReader.ReadAll(_path, IssueCatalog.CANONICAL_INVALID_ATTRIBUTE.Id));

        // One page as an earlier version parsed it: the same facts, with no collection.
        ParsedBefore(server.BaseUri + "plants/thin");
        analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        const string WHY = "1 page in this crawl was fetched by an earlier version";
        Assert.True(analysis.IsPartial);
        const string TEXT = $"{WHY}, which didn't record what this check needs. Crawl again to check it.";
        Assert.Equal(
            [
                new SkippedCheck(IssueCatalog.UNCRAWLABLE_LINKS.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.PAGINATION_NOT_LINKED.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.PAGINATION_BROKEN.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.PAGINATION_TO_NON_INDEXABLE.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.PAGINATION_SEQUENCE_ERROR.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.HEAD_TAGS_IN_BODY.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.HTML_STRUCTURE_ERRORS.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT),
                new SkippedCheck(IssueCatalog.MOBILE_ALTERNATE.Id, SkippedCheckReasons.EARLIER_VERSION, TEXT)
            ], analysis.Skipped);
        Assert.Contains($"9 checks weren't run: {WHY}, which didn't record what they need. Crawl again to run them.", analysis.Notes);
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.CANONICAL_INVALID_ATTRIBUTE.Id));
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.UNCRAWLABLE_LINKS.Id));
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.PAGINATION_SEQUENCE_ERROR.Id));
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.HEAD_TAGS_IN_BODY.Id));
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.CANONICAL_INVALID_ATTRIBUTE);

        // The rest of the canonical checks still ran.
        Assert.Single(IssueReader.ReadAll(_path, IssueCatalog.CANONICAL_FRAGMENT.Id));
    }

    /// <summary>
    /// A page's facts as an earlier version stored them, without the lists the new checks added: they read as empty, never
    /// null, so every rule can read them (the garden crawl of 31 October failed two rules on every page before).
    /// </summary>
    [Fact]
    public void FactsAnEarlierVersionStoredReadTheNewListsAsEmpty()
    {
        var stored = System.Text.Encoding.UTF8.GetBytes("""{"headers":[],"page":{"titles":["A page"],"wordCount":3}}""");

        var facts = BlobCodec.DecodeDetails(BlobCodec.Compress(stored)).Page!;

        Assert.Equal((0, "A page"), (facts.CollectedVersion, facts.Titles.Single()));
        Assert.Empty(facts.Pagination);
        Assert.Empty(facts.HeadTagsInBody);
        Assert.Empty(facts.StructureErrors);
        Assert.Empty(facts.CharsetDeclarations);
        Assert.Null(facts.UncrawlableLinks);
        Assert.Null(facts.MobileAlternate);
    }

    /// <summary>Stores a page's facts as they'd read had an earlier version parsed it: with no collection.</summary>
    private void ParsedBefore(string url)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT d.url_id, d.data FROM response_details d JOIN urls u ON u.id = d.url_id WHERE u.url = $url";
        read.Parameters.AddWithValue("$url", url);
        long id;
        byte[] data;
        using (var reader = read.ExecuteReader())
        {
            Assert.True(reader.Read());
            (id, data) = (reader.GetInt64(0), (byte[])reader.GetValue(1));
        }

        var details = BlobCodec.DecodeDetails(data);
        using var write = connection.CreateCommand();
        write.CommandText = "UPDATE response_details SET data = $data WHERE url_id = $id";
        write.Parameters.AddWithValue("$data", BlobCodec.EncodeDetails(details with { Page = details.Page! with { CollectedVersion = 0 } }));
        write.Parameters.AddWithValue("$id", id);
        Assert.Equal(1, write.ExecuteNonQuery());
    }
}
