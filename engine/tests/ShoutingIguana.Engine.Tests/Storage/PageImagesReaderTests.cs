using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The images a page shows, for the inspector: each image file once, the largest first after any redirects, with the
/// alt text the page gives it and where it sits. Held to the page's image links as stored.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class PageImagesReaderTests(GridCrawl crawl)
{
    [Fact]
    public void APagesImagesAreEachFileOnceTheLargestFirst()
    {
        var pages = TestCrawls.Rows(crawl.Path, $"SELECT printf('%09d', from_id) FROM links WHERE kind = {(int)LinkKind.Image} AND from_id <> to_id GROUP BY from_id")
            .Select(int.Parse).ToList();
        Assert.True(pages.Count >= 10, $"Only {pages.Count} pages show an image.");
        var sized = 0;
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            ReadCache cache = new();
            foreach (var page in pages.Take(40))
            {
                var links = Links(path, page);
                var read = PageImagesReader.Read(path, page, PageImagesReader.MAX_LIMIT, cache)!;
                var ids = links.Select(link => link.To).Distinct().ToList();
                Assert.Equal((page, (long)ids.Count, (long)links.Count), (read.UrlId, read.Total, read.Links));

                // Each file once, the largest first after any redirects (as the large-image check judges it), then as the page shows them.
                var facts = GridReadContext.Read(path, null, CancellationToken.None, context => UrlFactsReader.Read(context, ids));
                var expected = ids.Select((id, index) => (id, index)).OrderBy(entry => facts[entry.id].SizeBytes is null ? 1 : 0)
                    .ThenByDescending(entry => facts[entry.id].SizeBytes ?? 0).ThenBy(entry => entry.index).Select(entry => entry.id).ToList();
                Assert.Equal(expected, read.Rows.Select(row => row.Id));
                sized += read.Rows.Count(row => row.Target.SizeBytes is not null);
                foreach (var row in read.Rows)
                {
                    var own = links.Where(link => link.To == row.Id).ToList();
                    Assert.Equal((long)own.Count, row.Links);
                    Assert.Equal(own.Select(link => link.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text)), row.Text);
                    Assert.Equal(Enum.GetValues<LinkPosition>().Where(position => own.Any(link => link.Position == (long)position)), row.Positions);
                    Assert.Equal(facts[row.Id], row.Target);
                }

                // A page of them: the first, in the same order.
                var first = PageImagesReader.Read(path, page, 1, cache)!;
                Assert.Equal((read.Total, read.Links), (first.Total, first.Links));
                Assert.Equal(read.Rows.Take(1).Select(row => row.Id), first.Rows.Select(row => row.Id));
            }
        }

        Assert.True(sized > 10, $"Only {sized} images have a size: the order by size isn't held to much.");
    }

    [Fact]
    public void APageWithoutImagesHasNoneAndAUrlTheCrawlLacksIsNull()
    {
        var bare = TestCrawls.Rows(crawl.Path, $"SELECT printf('%09d', u.id) FROM urls u WHERE NOT EXISTS (SELECT 1 FROM links l WHERE l.from_id = u.id AND l.kind = {(int)LinkKind.Image}) LIMIT 1")
            .Select(int.Parse).Single();
        var none = PageImagesReader.Read(crawl.Path, bare)!;
        Assert.Equal((bare, 0L, 0L), (none.UrlId, none.Total, none.Links));
        Assert.Empty(none.Rows);
        Assert.Null(PageImagesReader.Read(crawl.Path, int.MaxValue));
        Assert.Contains("limit", Assert.Throws<UrlQueryException>(() => PageImagesReader.Read(crawl.Path, bare, 0)).Errors.Keys);
        Assert.Contains("limit", Assert.Throws<UrlQueryException>(() => PageImagesReader.Read(crawl.Path, bare, PageImagesReader.MAX_LIMIT + 1)).Errors.Keys);
    }

    /// <summary>The page's image links to other URLs, in crawl order, as stored.</summary>
    private static List<(int To, string? Text, long Position)> Links(string path, int page)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT to_id, text, position FROM links WHERE from_id = {page} AND to_id <> {page} AND kind = {(int)LinkKind.Image} ORDER BY rowid";
        using var reader = command.ExecuteReader();
        List<(int, string?, long)> links = [];
        while (reader.Read())
        {
            links.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2)));
        }

        return links;
    }
}
