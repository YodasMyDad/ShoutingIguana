EDITS = [
('tests/ShoutingIguana.Sidecar.Tests/GridEndpointsTests.cs',
'''             "headers", "structuredData", "hreflang", "issues", "namedBy", "summary", "source"],''',
'''             "headers", "structuredData", "hreflang", "issues", "namedBy", "summary", "source", "listedIn"],'''),
('tests/ShoutingIguana.Sidecar.Tests/LinksSitemapsRobotsEndpointsTests.cs',
'''        Assert.Equal(["id", "url", "parentId", "foundVia", "state", "statusCode", "isIndex", "entries", "sitemaps", "error", "urls"], index.AsObject().Select(property => property.Key));
''',
'''        Assert.Equal(["id", "url", "parentId", "foundVia", "state", "statusCode", "isIndex", "entries", "sitemaps", "error", "urls", "listedSitemaps"],
            index.AsObject().Select(property => property.Key));

        // The index says how many sitemaps its file lists, beside how many of them the crawl has; a sitemap of URLs lists
        // none, and one that couldn't be read doesn't say.
        Assert.Equal([3, 0, 0, null], sitemaps.Select(sitemap => (int?)sitemap!["listedSitemaps"]));
'''),
]
