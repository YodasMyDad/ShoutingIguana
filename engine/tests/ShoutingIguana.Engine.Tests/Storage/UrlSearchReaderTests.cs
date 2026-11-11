using System.Globalization;
using System.Text;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Search as it's typed, held to a reference worked out here from the crawl's rows with strings alone: which URLs match,
/// how each ranks and in what order they come, for texts taken from every part of the crawl's own URLs and titles and for
/// ones planted to be awkward (capitals, text that isn't ASCII, LIKE's wildcards, other sites' URLs). And to the URL
/// grid: the count given is the grid's under the same search, with a URL summary and without.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class UrlSearchReaderTests(GridCrawl crawl) : IDisposable
{
    private static readonly int[] LIMITS = [1, 3, UrlSearchReader.DEFAULT_LIMIT, UrlSearchReader.MAX_LIMIT];

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    private sealed record Row(int Id, string Url, string? Title, bool IsInternal);

    /// <summary>The crawl with awkward URLs and titles planted in it: analysed, and a copy continued since.</summary>
    private (string Analysed, string Changed) Planted()
    {
        var site = crawl.Server.BaseUri.AbsoluteUri;
        var copy = crawl.Copy();
        var next = (int)TestCrawls.Count(copy, "SELECT MAX(id) FROM urls") + 1;
        foreach (var (url, title, isInternal) in new (string, string?, bool)[]
                 {
                     (site + "Wild/UPPER-Case/", "An UPPER case Title", true),
                     (site + "wild/caf%C3%A9", "Café Über – crème brûlée", true),
                     (site + "wild/каталог/", "Каталог товаров", true),
                     (site + "wild/find?q=sofa/", "Sofa-sets: (garden) sofa, 100% rattan_weave", true),
                     (site + "wild/under_score", @"A back\slash and an under_score", true),
                     (site + "wild/per%cent", "per%cent", true),
                     (site + "wild/sofa", "sofa", true),
                     (site + "wild/sofa/", "The sofa", true),
                     (site + "wild/sofa/sofa-beds", "Beds", true),
                     (site + "wild/a-sofa", "Unsofa", true),
                     (site + "wild/emoji", "Tables 🪑 chairs", true),
                     ("https://www.facebook.com/shoutingiguana", null, false),
                     ("https://facebook.example.org/", null, false),
                     ("https://example.org/", null, false),
                     ("https://example.org/wild/sofa", null, false),
                     ("http://www.example.org", null, false)
                 })
        {
            Plant(copy, next++, url, title, isInternal);
        }

        var changed = _files.NewPath();
        File.Copy(copy, changed);
        Stamp(changed);
        return (copy, changed);
    }

    [Fact]
    public void TheBestFewAreTheReferencesForEveryText()
    {
        var (analysed, changed) = Planted();
        var rows = Rows(analysed);
        ReadCache cache = new();
        var texts = Texts(rows);
        Assert.InRange(texts.Count, 300, 5_000);
        var ranks = new int[3];
        foreach (var text in texts)
        {
            var expected = Reference(rows, text);
            foreach (var limit in LIMITS)
            {
                var found = UrlSearchReader.Search(analysed, text, limit, cache);
                Assert.True(expected.Take(limit).Select(match => (match.Row.Id, match.Rank, match.Field)).SequenceEqual(found.Results.Select(result => (result.Id, result.Rank, result.MatchedIn))),
                    $"'{text}', the best {limit}: expected {Describe(expected.Take(limit).Select(match => (match.Row.Id, match.Rank, match.Field)))}, found {Describe(found.Results.Select(result => (result.Id, result.Rank, result.MatchedIn)))}");
                Assert.Equal((expected.Count, rows.Count), (found.Matching, found.Total));
            }

            // What a result shows is the crawl's own: the URL and title as stored, capitals and all.
            foreach (var result in UrlSearchReader.Search(analysed, text, UrlSearchReader.MAX_LIMIT, cache).Results)
            {
                var row = rows.Single(row => row.Id == result.Id);
                Assert.Equal((row.Url, row.Title, row.IsInternal), (result.Url, result.Title, result.IsInternal));
                ranks[(int)result.Rank]++;
            }

            // And the count is the URL grid's under the same search, with a URL summary and without.
            Assert.Equal(expected.Count, UrlGridReader.Query(analysed, new UrlQuery { Search = text, Limit = 1 }).Matching);
            Assert.Equal(expected.Count, UrlGridReader.Query(changed, new UrlQuery { Search = text, Limit = 1 }).Matching);
            Assert.Equal(expected.Count, UrlSearchReader.Search(changed, text, 1).Matching);
        }

        // The texts did find URLs of every rank.
        Assert.All(ranks, count => Assert.InRange(count, 50, int.MaxValue));
    }

    [Theory]
    [InlineData("wild/sofa", "wild/sofa", UrlSearchRank.Exact, UrlSearchField.Url)]
    [InlineData("/wild/sofa/", "wild/sofa/", UrlSearchRank.Exact, UrlSearchField.Url)]
    [InlineData("WILD/SOFA", "wild/sofa", UrlSearchRank.Exact, UrlSearchField.Url)]
    [InlineData("example.org", "https://example.org/", UrlSearchRank.Exact, UrlSearchField.Url)]
    [InlineData("https://example.org/wild/sofa", "https://example.org/wild/sofa", UrlSearchRank.Exact, UrlSearchField.Url)]
    [InlineData("upper-c", "Wild/UPPER-Case/", UrlSearchRank.Start, UrlSearchField.Url)]
    [InlineData("facebook.com", "https://www.facebook.com/shoutingiguana", UrlSearchRank.Start, UrlSearchField.Url)]
    [InlineData("crème", "wild/caf%C3%A9", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData("(garden", "wild/find?q=sofa/", UrlSearchRank.Anywhere, UrlSearchField.Title)]
    [InlineData("garden) sofa", "wild/find?q=sofa/", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData("товаров", "wild/каталог/", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData("оваров", "wild/каталог/", UrlSearchRank.Anywhere, UrlSearchField.Title)]
    [InlineData("chairs", "wild/emoji", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData("nsofa", "wild/a-sofa", UrlSearchRank.Anywhere, UrlSearchField.Title)]
    [InlineData("100%", "wild/find?q=sofa/", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData("rattan_", "wild/find?q=sofa/", UrlSearchRank.Start, UrlSearchField.Title)]
    [InlineData(@"k\s", "wild/under_score", UrlSearchRank.Anywhere, UrlSearchField.Title)]
    public void TheFirstResultIsThePageMeant(string text, string url, UrlSearchRank rank, UrlSearchField field)
    {
        var (analysed, _) = Planted();
        var first = UrlSearchReader.Search(analysed, text).Results[0];
        Assert.Equal((url.Contains("://", StringComparison.Ordinal) ? url : crawl.Server.BaseUri.AbsoluteUri + url, rank, field), (first.Url, first.Rank, first.MatchedIn));
    }

    /// <summary>
    /// The order among the URLs a text matches: the page it names, then those it starts a segment of, the site's own
    /// before another site's, by URL before by title, the shorter first; then the rest.
    /// </summary>
    [Fact]
    public void TheOrderIsByRankThenTheSitesOwnThenTheUrlThenTheShorter()
    {
        var (analysed, _) = Planted();
        var site = crawl.Server.BaseUri.AbsoluteUri;
        var found = UrlSearchReader.Search(analysed, "sofa", 50).Results.Select(result => (result.Url, result.Rank, result.MatchedIn)).ToList();
        (string, UrlSearchRank, UrlSearchField)[] expected =
        [
            (site + "wild/sofa", UrlSearchRank.Start, UrlSearchField.Url),
            (site + "wild/sofa/", UrlSearchRank.Start, UrlSearchField.Url),
            (site + "wild/sofa/sofa-beds", UrlSearchRank.Start, UrlSearchField.Url),
            (site + "wild/find?q=sofa/", UrlSearchRank.Start, UrlSearchField.Title),
            ("https://example.org/wild/sofa", UrlSearchRank.Start, UrlSearchField.Url),
            (site + "wild/a-sofa", UrlSearchRank.Anywhere, UrlSearchField.Url)
        ];
        Assert.Equal(expected, found);
    }

    [Fact]
    public void AResultCarriesItsStatusAndOutcomeAsTheGridHasThem()
    {
        ReadCache cache = new();
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            HashSet<UrlOutcome?> outcomes = [];
            foreach (var text in new[] { "/", "p/1", "/broken/", "/r/", "/private/", "/err/", "missing", "ext" })
            {
                var results = UrlSearchReader.Search(path, text, UrlSearchReader.MAX_LIMIT, cache).Results;
                Assert.NotEmpty(results);
                var grid = UrlGridReader.Query(path, new UrlQuery
                {
                    Columns = ["statusCode", "outcome"],
                    Filters = [Numbers("id", FilterOperator.In, [.. results.Select(result => (double)result.Id)])],
                    Limit = 1_000
                }).Rows.ToDictionary(row => row.Id, row => ((long?)row.Values[1], (string?)row.Values[2]));
                foreach (var result in results)
                {
                    Assert.Equal(grid[result.Id], (result.StatusCode, result.Outcome is { } outcome ? char.ToLowerInvariant(outcome.ToString()[0]) + outcome.ToString()[1..] : null));
                    outcomes.Add(result.Outcome);
                }
            }

            Assert.Contains(UrlOutcome.Success, outcomes);
            Assert.True(outcomes.Count >= 3, $"The results had only {outcomes.Count} outcomes between them.");
        }
    }

    /// <summary>Nothing typed yet: every URL matches, as with no search in the grid, none is ranked, and the text has been read.</summary>
    [Fact]
    public void AnEmptyTextMatchesEveryUrlRanksNoneAndReadsTheText()
    {
        ReadCache cache = new();
        var urls = TestCrawls.Count(crawl.Path, "SELECT COUNT(*) FROM urls");
        var nothing = UrlSearchReader.Search(crawl.Path, string.Empty, cache: cache);
        Assert.Equal((0, urls, urls), (nothing.Results.Count, nothing.Matching, nothing.Total));
        Assert.Equal(1, cache.TextCount);
    }

    [Fact]
    public void ItRefusesWhatItCantSearchFor()
    {
        Assert.Equal("text", Assert.Single(Assert.Throws<UrlQueryException>(() => UrlSearchReader.Search(crawl.Path, null!)).Errors).Key);
        Assert.Equal("text", Assert.Single(Assert.Throws<UrlQueryException>(() => UrlSearchReader.Search(crawl.Path, new string('a', UrlSearchReader.MAX_TEXT + 1))).Errors).Key);
        Assert.Equal("text", Assert.Single(Assert.Throws<UrlQueryException>(() => UrlSearchReader.Search(crawl.Path, "a\0b")).Errors).Key);
        Assert.Equal("limit", Assert.Single(Assert.Throws<UrlQueryException>(() => UrlSearchReader.Search(crawl.Path, "a", 0)).Errors).Key);
        Assert.Equal("limit", Assert.Single(Assert.Throws<UrlQueryException>(() => UrlSearchReader.Search(crawl.Path, "a", UrlSearchReader.MAX_LIMIT + 1)).Errors).Key);
        Assert.Equal(UrlSearchReader.MAX_LIMIT, UrlSearchReader.Search(crawl.Path, "/", UrlSearchReader.MAX_LIMIT).Results.Count);
        Assert.NotEmpty(UrlSearchReader.Search(crawl.Path, new string('/', 1)).Results);
        Assert.Empty(UrlSearchReader.Search(crawl.Path, new string('a', UrlSearchReader.MAX_TEXT)).Results);
    }

    /// <summary>
    /// The crawl's text is read once and kept while the crawl stands still: a URL put into the file behind its back isn't
    /// found until the crawl's stamp moves, as a run of the crawl moves it; without a cache every search reads the file.
    /// </summary>
    [Fact]
    public void TheTextIsKeptWhileTheCrawlStandsStillAndReadAgainOnceItHasChanged()
    {
        var copy = crawl.Copy();
        ReadCache cache = new();
        var before = UrlSearchReader.Search(copy, "kept-while-still", cache: cache);
        Assert.Equal(0, before.Matching);

        Plant(copy, (int)before.Total + 1, crawl.Server.BaseUri.AbsoluteUri + "kept-while-still", "Planted", isInternal: true);
        Assert.Equal(0, UrlSearchReader.Search(copy, "kept-while-still", cache: cache).Matching);
        Assert.Equal(1, UrlSearchReader.Search(copy, "kept-while-still").Matching);

        Stamp(copy);
        var after = UrlSearchReader.Search(copy, "kept-while-still", cache: cache);
        Assert.Equal((1, before.Total + 1), (after.Matching, after.Total));
        Assert.Equal("Planted", Assert.Single(after.Results).Title);
    }

    /// <summary>A crawl's text is megabytes, so only two crawls' are kept: the one being searched and the one before.</summary>
    [Fact]
    public void OnlyTwoCrawlsTextIsKept()
    {
        ReadCache cache = new();
        foreach (var path in new[] { crawl.Path, crawl.Changed, crawl.Copy(), crawl.Copy() })
        {
            Assert.NotEmpty(UrlSearchReader.Search(path, "/", cache: cache).Results);
            Assert.InRange(cache.TextCount, 1, ReadCache.TEXT_CAPACITY);
        }

        Assert.Equal(ReadCache.TEXT_CAPACITY, cache.TextCount);
    }

    /// <summary>
    /// A running crawl is answered for one moment of it: within the two seconds its text is kept, a URL the crawl has
    /// found since isn't in the results or the counts, though each result's status is read as it now stands; after
    /// them, it is.
    /// </summary>
    [Fact]
    public void ARunningCrawlIsAnsweredForOneMomentOfIt()
    {
        var running = crawl.Copy();
        Change(running, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        using (CrawlFileLock.Acquire(running))
        {
            ReadCache cache = new();
            var moment = UrlSearchReader.Search(running, "found-while-running", cache: cache);
            Assert.Equal(0, moment.Matching);
            var home = Assert.Single(UrlSearchReader.Search(running, "/", 1, cache).Results);
            Assert.Equal((200, UrlOutcome.Success), (home.StatusCode, home.Outcome));

            // The crawl goes on: a URL more, and the home page fetched again, to an error.
            Plant(running, (int)moment.Total + 1, crawl.Server.BaseUri.AbsoluteUri + "found-while-running", "Found", isInternal: true, summary: false);
            Change(running, "UPDATE responses SET status_code = 503, outcome = 3 WHERE url_id = $id", ("$id", home.Id));
            var same = UrlSearchReader.Search(running, "found-while-running", cache: cache);
            Assert.Equal((0, moment.Total), (same.Matching, same.Total));
            var again = Assert.Single(UrlSearchReader.Search(running, "/", 1, cache).Results);
            Assert.Equal((home.Id, 503, UrlOutcome.ServerError), (again.Id, again.StatusCode, again.Outcome));

            Thread.Sleep(ReadCache.LIVE_FOR + TimeSpan.FromMilliseconds(200));
            var later = UrlSearchReader.Search(running, "found-while-running", cache: cache);
            Assert.Equal((1, moment.Total + 1), (later.Matching, later.Total));
        }
    }

    /// <summary>The text itself: rows in any order, a title for no URL, a second title, ids no URL has, and a zero byte, as a file may hold.</summary>
    [Fact]
    public void TheTextTakesWhatAFileMayHold()
    {
        SearchText text = new();
        text.AddUrl(7, "https://x.test/seven"u8, true);
        text.AddUrl(3, "https://x.test/Three"u8, false);
        text.AddUrl(0, "https://x.test/zero"u8, true);
        text.AddUrl(long.MaxValue, "https://x.test/huge"u8, true);
        text.AddUrl(9, "https://x.test/nine\0tail"u8, true);
        text.AddTitle(3, "Three's Title"u8);
        text.AddTitle(3, "A second title"u8);
        text.AddTitle(8, "Nobody's"u8);
        text.AddTitle(7, "Seven\0tail"u8);
        text.Seal();

        Assert.Equal(3, text.Count);
        Assert.Equal([7, 3, 9], Enumerable.Range(0, 3).Select(text.Id));
        Assert.Equal(["https://x.test/seven", "https://x.test/Three", "https://x.test/nine"], Enumerable.Range(0, 3).Select(text.Url));
        Assert.Equal(["Seven", "Three's Title", null], Enumerable.Range(0, 3).Select(text.Title));
        Assert.Equal(0, text.Search(SearchText.Needle("tail"), 10).Matching);
        Assert.Equal(0, text.Search(SearchText.Needle("second"), 10).Matching);
        Assert.Equal(0, text.Search(SearchText.Needle("nobody"), 10).Matching);

        // Of three that match alike, the site's own before another's, the shorter first, whatever their ids.
        var three = text.Search(SearchText.Needle("X.T"), 10);
        Assert.Equal(3, three.Matching);
        Assert.Equal([2, 0, 1], three.Best.Select(hit => hit.Ordinal));
        Assert.All(three.Best, hit => Assert.Equal((UrlSearchRank.Start, UrlSearchField.Url), (hit.Rank, hit.Field)));
        Assert.InRange(text.Bytes, 100, 4_096);
    }

    /// <summary>Texts from every part of the crawl's URLs and titles, in either case, with some that are awkward.</summary>
    private static List<string> Texts(IReadOnlyList<Row> rows)
    {
        HashSet<string> texts = new(StringComparer.Ordinal)
        {
            "p", "P", "/", "//", "://", "http", "HTTP://", "127", "www", "www.", "faceb", "facebook", "example.org", "example.org/", "org", ".", "-", " ", "é", "É", "über", "Über", "–",
            "sofa", "SOFA", "(garden", "q=sofa", "%", "_", @"\", "%C3", "100%", "rattan_", "🪑", "каталог", "Каталог", "товаров", "zzz-nothing-matches-this", "a", "e", "1", "?", "wild/",
            "/wild", "under_score", "underXscore", "per%cent", "perXcent"
        };
        foreach (var (row, index) in rows.Select((row, index) => (row, index)).Where(entry => entry.index % 5 == 0 || entry.row.Url.Contains("wild", StringComparison.OrdinalIgnoreCase)))
        {
            var uri = new Uri(row.Url);
            var path = uri.PathAndQuery;
            texts.Add(row.Url);
            texts.Add(row.Url.ToUpperInvariant());
            texts.Add(row.Url[(row.Url.IndexOf("://", StringComparison.Ordinal) + 3)..]);
            texts.Add(path);
            texts.Add(path.Trim('/'));
            texts.Add(path.TrimEnd('/'));
            texts.Add(path.Trim('/') + "/");
            if (path.Trim('/').Split('/')[^1] is { Length: >= 3 } last)
            {
                texts.Add(last[..3]);
                texts.Add(last[1..]);
                texts.Add("/" + last[..2]);
            }

            if (path.Length > 6)
            {
                texts.Add(path.Substring(2, 4));
            }

            if (row.Title is { Length: > 0 } title)
            {
                texts.Add(title);
                texts.Add(title.ToUpperInvariant());
                var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                texts.Add(words[0]);
                texts.Add(words[^1]);
                texts.Add(words[^1].ToLowerInvariant());
                if (title.Length > 5)
                {
                    texts.Add(title.Substring(1, 4));
                    texts.Add(title[^4..]);
                }
            }
        }

        // Whole strings only: a text cut inside a character that takes two UTF-16 units isn't one a person can type.
        return [.. texts.Where(text => text.Length is > 0 and <= UrlSearchReader.MAX_TEXT && !char.IsSurrogate(text[0]) && !char.IsHighSurrogate(text[^1])).Order(StringComparer.Ordinal)];
    }

    private sealed record Match(Row Row, UrlSearchRank Rank, UrlSearchField Field, int Length);

    /// <summary>Every URL the text matches, best first, by the rules as the contract states them, worked out on strings.</summary>
    private static List<Match> Reference(IReadOnlyList<Row> rows, string text)
    {
        var needle = Fold(text);
        List<Match> matches = [];
        foreach (var row in rows)
        {
            List<Match> either = [];
            var url = Fold(row.Url);
            if (url.Contains(needle, StringComparison.Ordinal))
            {
                either.Add(new Match(row, UrlRank(url, needle), UrlSearchField.Url, Encoding.UTF8.GetByteCount(row.Url)));
            }

            if (row.Title is { } stored && Fold(stored) is var title && title.Contains(needle, StringComparison.Ordinal))
            {
                either.Add(new Match(row, TitleRank(title, needle), UrlSearchField.Title, Encoding.UTF8.GetByteCount(stored)));
            }

            if (either.Count > 0)
            {
                matches.Add(either.OrderBy(match => match.Rank).ThenBy(match => match.Field).ThenBy(match => match.Length).First());
            }
        }

        return [.. matches.OrderBy(match => match.Rank).ThenBy(match => !match.Row.IsInternal).ThenBy(match => match.Field).ThenBy(match => match.Length).ThenBy(match => match.Row.Id)];
    }

    private static UrlSearchRank UrlRank(string url, string needle)
    {
        var host = url.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0 ? scheme + 3 : 0;
        var path = url.IndexOf('/', host) is var slash and >= 0 ? slash : url.Length;
        var named = needle.TrimEnd('/');
        string[] names = [url, url[host..], url[path..], path < url.Length ? url[(path + 1)..] : string.Empty];
        if (names.Any(name => name.TrimEnd('/') == named))
        {
            return UrlSearchRank.Exact;
        }

        for (var at = url.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = url.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            if (needle[0] == '/' || at == 0 || url[at - 1] == '/' || (url[host..].StartsWith("www.", StringComparison.Ordinal) && at == host + 4))
            {
                return UrlSearchRank.Start;
            }
        }

        return UrlSearchRank.Anywhere;
    }

    private static UrlSearchRank TitleRank(string title, string needle)
    {
        var word = Rune.GetRuneAt(needle, 0) is var first && Rune.IsLetterOrDigit(first);
        for (var at = title.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = title.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || (word && !Rune.IsLetterOrDigit(RuneBefore(title, at))))
            {
                return UrlSearchRank.Start;
            }
        }

        return UrlSearchRank.Anywhere;
    }

    private static Rune RuneBefore(string text, int at) =>
        char.IsLowSurrogate(text[at - 1]) && at >= 2 ? Rune.GetRuneAt(text, at - 2) : Rune.GetRuneAt(text, at - 1);

    /// <summary>ASCII capitals lowercased, and nothing else: what the search ignores.</summary>
    private static string Fold(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var index = 0; index < span.Length; index++)
        {
            span[index] = source[index] is >= 'A' and <= 'Z' ? (char)(source[index] + 32) : source[index];
        }
    });

    private static string Describe(IEnumerable<(int Id, UrlSearchRank Rank, UrlSearchField Field)> results) =>
        string.Join(", ", results.Select(result => string.Create(CultureInfo.InvariantCulture, $"{result.Id} {result.Rank} {result.Field}")));

    private static List<Row> Rows(string path)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT u.id, u.url, r.title, u.is_internal FROM urls u LEFT JOIN responses r ON r.url_id = u.id ORDER BY u.id";
        using var reader = command.ExecuteReader();
        List<Row> rows = [];
        while (reader.Read())
        {
            rows.Add(new Row(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3)));
        }

        return rows;
    }

    /// <summary>A URL the crawl found, with its response and title when it has one, and its row in the URL summary.</summary>
    private static void Plant(string path, int id, string url, string? title, bool isInternal, bool summary = true)
    {
        Change(path, "INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ($id, $url, $internal, 0, 2, 2, 0, 1)",
            ("$id", id), ("$url", url), ("$internal", isInternal ? 1 : 0));
        if (title is not null)
        {
            Change(path, """
                INSERT INTO responses (url_id, fetched_at, outcome, status_code, content_type, ttfb_ms, response_ms, is_html, title, title_count,
                                       meta_description_count, h1_count, is_noindex, is_nofollow, indexability, indexability_reason, word_count, outlink_count)
                VALUES ($id, 0, 0, 200, 'text/html', 1, 1, 1, $title, 1, 0, 0, 0, 0, 0, 0, 100, 0)
                """, ("$id", id), ("$title", title));
        }

        if (summary)
        {
            Change(path, """
                INSERT INTO url_summary (url_id, is_internal, source, depth, resource_type, status_code, indexable, indexability_reason, final_url_id,
                                         inlinks, followed_inlinks, nofollow_inlinks, linking_pages, issues, errors, warnings, notices)
                VALUES ($id, $internal, 1, 2, 0, $status, 1, 0, $id, 0, 0, 0, 0, 0, 0, 0, 0)
                """, ("$id", id), ("$internal", isInternal ? 1 : 0), ("$status", title is null ? DBNull.Value : 200));
        }
    }
}
