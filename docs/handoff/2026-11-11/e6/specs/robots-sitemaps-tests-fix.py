P = 'tests/ShoutingIguana.Engine.Tests/Storage/RobotsAndSitemapsReaderTests.cs'

EDITS = [
(P,
'''                Assert.Equal((expected.Count, expected.Select(sitemap => new SitemapRef(sitemap, sitemaps[sitemap]))), (read.Total, read.Sitemaps));
''',
'''                Assert.Equal(expected.Count, read.Total);
                Assert.Equal(expected.Select(sitemap => new SitemapRef(sitemap, sitemaps[sitemap])), read.Sitemaps);
'''),
(P,
'''                Assert.Equal((blocked.Count, 1L, [row]), (one.Total, one.Matching, one.Rows));
''',
'''                Assert.Equal((blocked.Count, 1L), (one.Total, one.Matching));
                Assert.Equal([row], one.Rows);
'''),
(P,
'''        Assert.Equal((["https://b.example"], 3L, 1), (paged.Origins.Select(origin => origin.Origin), paged.Matching, paged.Offset));
''',
'''        Assert.Equal(["https://b.example"], paged.Origins.Select(origin => origin.Origin));
        Assert.Equal((3L, 1), (paged.Matching, paged.Offset));
'''),
]
