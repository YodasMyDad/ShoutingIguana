P = 'tests/ShoutingIguana.Engine.Tests/Storage/UrlInspectorReaderTests.cs'

EDITS = [
(P,
'''        var inlinks = Counted("to_id");
        var outlinks = Counted("from_id");
        var named = 0;
''',
'''        var inlinks = Counted("to_id");
        var outlinks = Counted("from_id");
        var hyperlinks = Counted("to_id", "kind = 0");
        var named = 0;
'''),
(P,
'''            // Every link of every kind, the page's links to itself left out: what its lists of links hold in all.
            Assert.Equal(new UrlLinkTotals(inlinks.GetValueOrDefault(id), outlinks.GetValueOrDefault(id)), url.LinkTotals);
''',
'''            // Every link of every kind, the page's links to itself left out: what its lists of links hold in all. And the
            // hyperlinks among those to it, every one stored.
            Assert.Equal(new UrlLinkTotals(inlinks.GetValueOrDefault(id), outlinks.GetValueOrDefault(id)) { HyperlinkInlinks = hyperlinks.GetValueOrDefault(id) }, url.LinkTotals);
'''),
(P,
'''        Assert.Equal(Read(crawl.Path, home).LinkTotals, new UrlLinkTotals(
            UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Limit = 1 })!.Total,
            UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Outlinks, new LinkQuery { Limit = 1 })!.Total));
''',
'''        Assert.Equal(Read(crawl.Path, home).LinkTotals, new UrlLinkTotals(
            UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Limit = 1 })!.Total,
            UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Outlinks, new LinkQuery { Limit = 1 })!.Total)
        {
            HyperlinkInlinks = UrlInspectorReader.ReadLinks(crawl.Path, home, LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Limit = 1 })!.Total
        });
'''),
(P,
'''        Assert.All(new[] { home, Id("/p/7"), Id("/plants/"), Id("/img/1.jpg") }, id =>
            Assert.Equal(new UrlLinkTotals(inlinks.GetValueOrDefault(id), outlinks.GetValueOrDefault(id)), Read(crawl.Changed, id).LinkTotals));
    }
''',
'''        Assert.All(new[] { home, Id("/p/7"), Id("/plants/"), Id("/img/1.jpg") }, id =>
            Assert.Equal(new UrlLinkTotals(inlinks.GetValueOrDefault(id), outlinks.GetValueOrDefault(id)) { HyperlinkInlinks = hyperlinks.GetValueOrDefault(id) },
                Read(crawl.Changed, id).LinkTotals));
    }

    /// <summary>
    /// The hyperlinks to a URL, as the inspector counts them from the stored links, are what the links grid lists for it and
    /// what the inspector's own list of that kind holds: a figure that lands. The analysis's inlinks are the hyperlinks on
    /// the pages it audited, and it doesn't audit Cloudflare's own pages (under /cdn-cgi/), which the crawl fetches and
    /// parses like any other: where one of those links to a URL, the two counts differ by exactly its links, and nowhere
    /// else.
    /// </summary>
    [Fact]
    public void TheHyperlinksToAUrlLandInTheLinksGridAndPassTheAnalysisInlinksByTheUnauditedPagesLinks()
    {
        var stored = Counted("to_id", "kind = 0");
        var unaudited = Counted("to_id", "kind = 0 AND from_id IN (SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE instr(u.url, '/cdn-cgi/') > 0 AND r.word_count IS NOT NULL)");
        Assert.NotEmpty(unaudited);
        ReadCache cache = new();
        var differing = 0;
        foreach (var id in crawl.Reference.Rows.Keys)
        {
            var url = Read(crawl.Path, id);
            var expected = stored.GetValueOrDefault(id);
            Assert.Equal(expected, url.LinkTotals.HyperlinkInlinks);
            Assert.Equal(expected, Read(crawl.Changed, id).LinkTotals.HyperlinkInlinks);
            Assert.Equal(expected, url.Links!.Inlinks + unaudited.GetValueOrDefault(id));
            differing += unaudited.ContainsKey(id) ? 1 : 0;

            // The links grid's count for it, and the inspector's own list of its hyperlinks, with the link summary and without.
            if (id % 7 == 0 || unaudited.ContainsKey(id))
            {
                UrlQuery listed = new() { Filters = [Number("to.id", FilterOperator.Eq, id), Names("kind", FilterOperator.In, "hyperlink"), Boolean("selfLink", false)], Limit = 1 };
                Assert.Equal(expected, LinkGridReader.Query(crawl.Path, listed, cache).Matching);
                Assert.Equal(expected, LinkGridReader.Query(crawl.Changed, listed, cache).Matching);
                Assert.Equal(expected, UrlInspectorReader.ReadLinks(crawl.Path, id, LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Limit = 1 })!.Total);
                Assert.Equal(expected, UrlInspectorReader.ReadLinks(crawl.Changed, id, LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Limit = 1 })!.Total);
            }
        }

        // The site's menu is on its Cloudflare page too: each of the menu's targets has a hyperlink more than the analysis counted.
        Assert.InRange(differing, 5, 100);
        Assert.Equal(unaudited.Count, differing);
    }
'''),
(P,
'''    private Dictionary<int, long> Counted(string end) =>
        TestCrawls.Rows(crawl.Path, $"SELECT {end}, COUNT(*) FROM links WHERE from_id <> to_id GROUP BY 1").Select(row => row.Split(" | "))
''',
'''    private Dictionary<int, long> Counted(string end, string where = "1") =>
        TestCrawls.Rows(crawl.Path, $"SELECT {end}, COUNT(*) FROM links WHERE from_id <> to_id AND {where} GROUP BY 1").Select(row => row.Split(" | "))
'''),
]
