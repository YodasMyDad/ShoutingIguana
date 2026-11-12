using System.Reflection;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using SQLitePCL;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>What the analysis reads from a crawl outside its pass over the pages.</summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class AnalysisReaderTests(GridCrawl crawl)
{
    /// <summary>
    /// A rule may ask for a URL for every page it flags (where a canonical's redirects end), so the lookup is one
    /// statement, compiled once and kept, not one compiled and thrown away by each call. SQLite itself says so: the
    /// reader's connection holds one more compiled statement after the first lookup, and still that one after hundreds
    /// more. A lookup that compiled its own each time would leave none behind. The answers are what they were.
    /// </summary>
    [Fact]
    public void AUrlIsLookedUpByItsIdThroughOneStatementCompiledOnce()
    {
        using var reader = AnalysisReader.Open(crawl.Path);

        // SQLite lists a connection's statements only once it is asked to keep track of them, from then on.
        Connection(reader).Handle!.enable_sqlite3_next_stmt(true);
        var home = reader.ReadUrlId(crawl.Server.BaseUri.AbsoluteUri);
        Assert.True(home > 0);
        var before = CompiledStatements(reader);

        Assert.Equal(crawl.Server.BaseUri.AbsoluteUri, reader.ReadUrl(home));
        Assert.Equal(before + 1, CompiledStatements(reader));

        // A URL by its id, the same one each time it is asked for, and nothing for no URL.
        var ids = Enumerable.Range(1, 300).ToArray();
        var urls = ids.Select(id => reader.ReadUrl(id)!).ToArray();
        Assert.Equal(urls.Length, urls.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(urls, ids.Select(id => reader.ReadUrl(id)));
        Assert.All(ids.Take(50), id => Assert.Equal(id, reader.ReadUrlId(urls[id - 1])));
        Assert.Null(reader.ReadUrl(0));
        Assert.Null(reader.ReadUrl(int.MaxValue));
        Assert.Equal(before + 1, CompiledStatements(reader));

        // And while the pages are being read, which is when the rules ask.
        using var pages = reader.ReadPages(reader.ReadUrls()).GetEnumerator();
        Assert.True(pages.MoveNext());
        Assert.Equal(urls[..100], ids.Take(100).Select(id => reader.ReadUrl(id)));
    }

    /// <summary>
    /// The statements the reader's connection holds compiled, as SQLite lists them. The connection is the reader's own,
    /// which nothing else may use, so the test reaches for it by name: a renamed field fails here, loudly.
    /// </summary>
    private static int CompiledStatements(AnalysisReader reader)
    {
        var connection = Connection(reader);
        var count = 0;
        for (var statement = raw.sqlite3_next_stmt(connection.Handle, null); statement is not null; statement = raw.sqlite3_next_stmt(connection.Handle, statement))
        {
            count++;
        }

        return count;
    }

    private static SqliteConnection Connection(AnalysisReader reader)
    {
        var field = typeof(AnalysisReader).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return Assert.IsType<SqliteConnection>(field.GetValue(reader));
    }
}
