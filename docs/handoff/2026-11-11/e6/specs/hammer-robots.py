P = 'tests/ShoutingIguana.Engine.Tests/Storage/RunningCrawlReadsTests.cs'

EDITS = [
(P,
'''            var first = origins.Origins[0].Origin;
            var file = RobotsReader.ReadFile(path, first, cache);
            var urls = RobotsReader.ReadBlocked(path, first, limit: LIMIT, cache: cache);
            return file is null || urls is null ? $"{first} is listed and has no robots.txt"
                : file.Origin.Blocked != file.Lines.Sum(line => line.Blocked ?? 0) + file.Unmatched.Unreachable + file.Unmatched.Unknown
                    ? $"{first} blocks {file.Origin.Blocked} URLs, and its lines account for {file.Lines.Sum(line => line.Blocked ?? 0)}"
                : urls.Matching != urls.Total || urls.Rows.Count != Math.Min(LIMIT, urls.Total) ? $"{first}: {urls.Rows.Count} rows of {urls.Matching} blocked URLs, of {urls.Total}"
                : null;
        });
''',
'''            // Searched and filtered: one answer's counts are of one list, and are the rows it has.
            foreach (var query in new[] { new RobotsOriginQuery { Limit = 1_000 }, new RobotsOriginQuery { Blocked = true, Limit = 1_000 }, new RobotsOriginQuery { Search = "127", Blocked = false, Limit = 1_000 } })
            {
                var found = RobotsReader.ReadOrigins(path, query, cache);
                var expected = query.Blocked switch { true => found.Counts.Blocked, false => found.Counts.NotBlocked, _ => found.Counts.All };
                if (found.Matching != found.Origins.Count || found.Matching != expected || found.Counts.All != found.Counts.Blocked + found.Counts.NotBlocked ||
                    found.Counts.All > found.Total || found.Origins.Any(origin => query.Blocked is { } blocked && origin.Blocked > 0 != blocked))
                {
                    return $"{found.Origins.Count} origins of {found.Matching} that match, where the counts are {found.Counts} of {found.Total}";
                }
            }

            var first = origins.Origins[0].Origin;
            var file = RobotsReader.ReadFile(path, first, cache);
            var urls = RobotsReader.ReadBlocked(path, first, limit: LIMIT, cache: cache);
            if (file is null || urls is null)
            {
                return $"{first} is listed and has no robots.txt";
            }

            // A URL the origin blocked, as the inspector has it: the same origin, reason and line, and the sitemaps that
            // list it, each one the crawl has.
            if (urls.Rows.Count > 0)
            {
                var row = urls.Rows[^1];
                var inspected = UrlInspectorReader.Read(path, row.Id, cache);
                var test = inspected?.Indexability.Tests.Single(test => test.Test == IndexabilityTestKind.RobotsTxt);
                if (test is null || (test.Origin, test.Reason, test.Line, test.Rule) != (first, row.Reason, row.Line, row.Rule))
                {
                    return $"{row.Url} was refused by {row.Reason} at line {row.Line}, and the inspector says {test?.Reason} at line {test?.Line} of {test?.Origin}";
                }

                if (inspected!.ListedIn.Total < inspected.ListedIn.Sitemaps.Count || inspected.ListedIn.Sitemaps.Any(sitemap => sitemap.Id < 1 || sitemap.Url.Length == 0))
                {
                    return $"{row.Url} is listed in {inspected.ListedIn.Total} sitemaps, {inspected.ListedIn.Sitemaps.Count} of them named";
                }

                var one = RobotsReader.ReadBlocked(path, first, cache: cache, urlId: row.Id);
                if (one is null || one.Matching != 1 || one.Rows.Count != 1 || one.Rows[0] != row)
                {
                    return $"{row.Url} asked for alone gives {one?.Matching} rows";
                }
            }

            return file.Origin.Blocked != file.Lines.Sum(line => line.Blocked ?? 0) + file.Unmatched.Unreachable + file.Unmatched.Unknown
                    ? $"{first} blocks {file.Origin.Blocked} URLs, and its lines account for {file.Lines.Sum(line => line.Blocked ?? 0)}"
                : urls.Matching != urls.Total || urls.Rows.Count != Math.Min(LIMIT, urls.Total) ? $"{first}: {urls.Rows.Count} rows of {urls.Matching} blocked URLs, of {urls.Total}"
                : null;
        });
'''),
(P,
'''                if (sitemap.Urls.Fetched > sitemap.Urls.Listed || sitemap.Urls.Indexable + sitemap.Urls.NonIndexable > sitemap.Urls.Listed)
                {
                    return $"{sitemap.Url} lists {sitemap.Urls.Listed}: {sitemap.Urls.Fetched} fetched, {sitemap.Urls.Indexable} indexable, {sitemap.Urls.NonIndexable} not";
                }
''',
'''                if (sitemap.Urls.Fetched > sitemap.Urls.Listed || sitemap.Urls.Indexable + sitemap.Urls.NonIndexable > sitemap.Urls.Listed)
                {
                    return $"{sitemap.Url} lists {sitemap.Urls.Listed}: {sitemap.Urls.Fetched} fetched, {sitemap.Urls.Indexable} indexable, {sitemap.Urls.NonIndexable} not";
                }

                // An index's file lists at least the sitemaps the crawl has of it, once it's read.
                if (sitemap.ListedSitemaps < sitemap.Sitemaps || (sitemap.State == SitemapState.Pending && sitemap.ListedSitemaps is not null))
                {
                    return $"{sitemap.Url} ({sitemap.State}) lists {sitemap.ListedSitemaps} sitemaps, and the crawl has {sitemap.Sitemaps} of them";
                }
'''),
]
