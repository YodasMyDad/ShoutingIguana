E = 'src/ShoutingIguana.Engine/'
M = E + 'Storage/Models/'

WRITES = [
(M + 'SitemapRef.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A sitemap of the crawl: its id, which <see cref="SitemapListReader.Read"/> and the sitemap's URL grid know it by, and the URL it was found at.</summary>
public sealed record SitemapRef(int Id, string Url);
'''),
(M + 'UrlSitemaps.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The sitemaps that list a URL, in the order the crawl found them.</summary>
/// <param name="Total">How many list it.</param>
/// <param name="Sitemaps">The first of them, at most <see cref="UrlInspectorReader.MAX_SITEMAPS_NAMED"/>.</param>
public sealed record UrlSitemaps(long Total, IReadOnlyList<SitemapRef> Sitemaps);
'''),
]

EDITS = [
# --- The crawl file: an index's own count of the sitemaps its file lists.
(E + 'Storage/CrawlSchema.cs',
'''    /// <summary>
    /// Analysis results, created on first analysis and replaced as a whole by every later one. Severity and
''',
'''    /// <summary>
    /// The <c>sitemaps</c> columns added since the table was first created, which the writer adds whenever it opens a file,
    /// as it does <see cref="RESPONSE_COLUMNS"/>:
    /// <list type="bullet">
    /// <item><c>listed_sitemaps</c> is how many sitemaps the file lists (its <c>&lt;sitemap&gt;</c> entries, as read: repeats
    /// and entries that aren't URLs included), so an index says how many it names beside how many of them the crawl has;
    /// 0 for a sitemap that lists only URLs. Null when the file's body was never read (it failed first, or was left
    /// unread), and for every sitemap an engine from before the column read.</item>
    /// </list>
    /// </summary>
    public static readonly (string Name, string Type)[] SITEMAP_COLUMNS = [("listed_sitemaps", "INTEGER")];

    /// <summary>
    /// Analysis results, created on first analysis and replaced as a whole by every later one. Severity and
'''),
(E + 'Storage/CrawlWriter.cs',
'''            AddMissingResponseColumns(connection);
            var writer = new CrawlWriter(connection, fileLock, options, counters, logger);
''',
'''            AddMissingColumns(connection, "responses", CrawlSchema.RESPONSE_COLUMNS);

            // And the count of the sitemaps an index's file lists.
            AddMissingColumns(connection, "sitemaps", CrawlSchema.SITEMAP_COLUMNS);
            var writer = new CrawlWriter(connection, fileLock, options, counters, logger);
'''),
(E + 'Storage/CrawlWriter.cs',
'''    /// <summary>Adds the <c>responses</c> columns a file made by an earlier engine lacks (<see cref="CrawlSchema.RESPONSE_COLUMNS"/>).</summary>
    private static void AddMissingResponseColumns(SqliteConnection connection)
    {
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM pragma_table_info('responses')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var (name, type) in CrawlSchema.RESPONSE_COLUMNS.Where(column => !existing.Contains(column.Name)))
        {
            CrawlDatabase.Execute(connection, $"ALTER TABLE responses ADD COLUMN {name} {type}");
        }
    }
''',
'''    /// <summary>
    /// Adds the columns of a crawl-time table that a file made by an earlier engine lacks (<see cref="CrawlSchema.RESPONSE_COLUMNS"/>,
    /// <see cref="CrawlSchema.SITEMAP_COLUMNS"/>): each may be null, so the rows the file has keep none.
    /// </summary>
    private static void AddMissingColumns(SqliteConnection connection, string table, (string Name, string Type)[] columns)
    {
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var (name, type) in columns.Where(column => !existing.Contains(column.Name)))
        {
            CrawlDatabase.Execute(connection, $"ALTER TABLE {table} ADD COLUMN {name} {type}");
        }
    }
'''),
(E + 'Storage/WriterStatements.cs',
'''            UPDATE sitemaps SET status_code = $status, is_index = $isIndex, url_count = $count, error = $error, is_probe = $isProbe
            WHERE id = $id
            """, "$status", "$isIndex", "$count", "$error", "$isProbe", "$id");
''',
'''            UPDATE sitemaps SET status_code = $status, is_index = $isIndex, url_count = $count, error = $error, is_probe = $isProbe,
                                listed_sitemaps = $listed
            WHERE id = $id
            """, "$status", "$isIndex", "$count", "$error", "$isProbe", "$listed", "$id");
'''),
(E + 'Storage/WriterStatements.cs',
'''        Run(_updateSitemap, record.StatusCode, record.IsIndex, record.UrlCount, record.Error, record.IsProbe, record.Id);
''',
'''        Run(_updateSitemap, record.StatusCode, record.IsIndex, record.UrlCount, record.Error, record.IsProbe, record.ListedSitemaps, record.Id);
'''),
(M + 'SitemapRecord.cs',
'''/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false);
''',
'''/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
/// <param name="ListedSitemaps">How many sitemaps the file lists, as read; null when its body was never read.</param>
internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false, int? ListedSitemaps = null);
'''),
(E + 'Sitemaps/SitemapDiscoverer.cs',
'''        List<string> children = [];
        var batch = new List<OutLink>(BATCH_SIZE);
        var urlCount = 0;
        var isIndex = false;
        int? statusCode = null;
        string? error = null;
''',
'''        List<string> children = [];
        var batch = new List<OutLink>(BATCH_SIZE);
        var urlCount = 0;
        var isIndex = false;
        var bodyRead = false;
        int? statusCode = null;
        string? error = null;
'''),
(E + 'Sitemaps/SitemapDiscoverer.cs',
'''                var isText = MediaTypeOf(response.Content.Headers) == "text/plain" ||
                             uri.AbsolutePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

''',
'''                var isText = MediaTypeOf(response.Content.Headers) == "text/plain" ||
                             uri.AbsolutePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                // From here on the file is being read: what it lists is counted, to its end or to where it fails.
                bodyRead = true;
'''),
(E + 'Sitemaps/SitemapDiscoverer.cs',
'''        await writer.CompleteSitemapAsync(new SitemapRecord(sitemapId, statusCode, isIndex, urlCount, error, isProbe), cancellationToken).ConfigureAwait(false);
''',
'''        // Every sitemap the file lists is counted, whether or not the crawl goes on to read it: past its limit of sitemaps,
        // or of nesting, an index still says how many it names.
        await writer.CompleteSitemapAsync(new SitemapRecord(sitemapId, statusCode, isIndex, urlCount, error, isProbe, bodyRead ? children.Count : null), cancellationToken)
            .ConfigureAwait(false);
'''),
# --- The list of sitemaps.
(M + 'SitemapInfo.cs',
'''    string? Error,
    SitemapUrlCounts Urls);
''',
'''    string? Error,
    SitemapUrlCounts Urls)
{
    /// <summary>
    /// How many sitemaps the file lists, as the crawl read it: its <c>&lt;sitemap&gt;</c> entries, repeats and entries that
    /// aren't URLs included, whether or not the crawl went on to read them (<see cref="Sitemaps"/> counts those it has, which
    /// its limits of 1,000 sitemaps and 3 levels can leave far fewer). 0 for a sitemap that lists only URLs. Null when the
    /// file's body was never read (it failed first, was left unread or is still to be read), and for a crawl made by an
    /// engine that didn't keep the count.
    /// </summary>
    public int? ListedSitemaps { get; init; }
}
'''),
(E + 'Storage/SitemapListReader.cs',
'''        List<(int Id, string Url, int? ParentId, int? Status, bool IsIndex, int Entries, string? Error, bool IsProbe)> stored = [];
        using (var command = context.Command("SELECT id, url, parent_id, status_code, is_index, url_count, error, is_probe FROM sitemaps ORDER BY id", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                stored.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    !reader.IsDBNull(4) && reader.GetInt64(4) != 0, reader.IsDBNull(5) ? 0 : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    !reader.IsDBNull(7) && reader.GetInt64(7) != 0));
            }
        }
''',
'''        // A file from before an index's own count was kept has no column for it, until it's continued.
        var listed = context.Scalar("SELECT COUNT(*) FROM pragma_table_info('sitemaps') WHERE name = 'listed_sitemaps'", []) > 0 ? "listed_sitemaps" : "NULL";
        List<(int Id, string Url, int? ParentId, int? Status, bool IsIndex, int Entries, string? Error, bool IsProbe, int? Listed)> stored = [];
        using (var command = context.Command($"SELECT id, url, parent_id, status_code, is_index, url_count, error, is_probe, {listed} FROM sitemaps ORDER BY id", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                stored.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    !reader.IsDBNull(4) && reader.GetInt64(4) != 0, reader.IsDBNull(5) ? 0 : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    !reader.IsDBNull(7) && reader.GetInt64(7) != 0, reader.IsDBNull(8) || reader.GetInt64(8) is < 0 or > int.MaxValue ? null : reader.GetInt32(8)));
            }
        }
'''),
(E + 'Storage/SitemapListReader.cs',
'''            sitemaps.Add(new SitemapInfo(sitemap.Id, sitemap.Url, sitemap.ParentId, foundVia, state, sitemap.Status, sitemap.IsIndex, sitemap.Entries,
                children[sitemap.Id].Count(), sitemap.Error, counts));
''',
'''            sitemaps.Add(new SitemapInfo(sitemap.Id, sitemap.Url, sitemap.ParentId, foundVia, state, sitemap.Status, sitemap.IsIndex, sitemap.Entries,
                children[sitemap.Id].Count(), sitemap.Error, counts)
            {
                ListedSitemaps = sitemap.Listed
            });
'''),
# --- The inspector: the sitemaps that list the URL, and what refused it.
(M + 'IndexabilityTest.cs',
'''/// <param name="Unreachable">For <see cref="IndexabilityTestKind.RobotsTxt"/>: robots.txt couldn't be read, which refuses everything.</param>
''',
'''/// <param name="Unreachable">For <see cref="IndexabilityTestKind.RobotsTxt"/>: robots.txt couldn't be read, which refuses everything.</param>
/// <param name="Origin">
/// For <see cref="IndexabilityTestKind.RobotsTxt"/>: the origin whose robots.txt decided it, as the list of origins names
/// it, when the crawl has one for it (or refused the URL): <see cref="RobotsReader.ReadFile"/> then gives the file.
/// </param>
/// <param name="Reason">For a URL robots.txt refused: why, as <see cref="RobotsReader.ReadBlocked"/> says of it.</param>
/// <param name="Line">For a URL a rule refused: the line of the origin's stored file that refused it.</param>
/// <param name="Rule">That line's text.</param>
'''),
(M + 'IndexabilityTest.cs',
'''    public bool? Unreachable { get; init; }

''',
'''    public bool? Unreachable { get; init; }

    public string? Origin { get; init; }

    public RobotsBlockReason? Reason { get; init; }

    public int? Line { get; init; }

    public string? Rule { get; init; }

'''),
(M + 'UrlDetail.cs',
'''    /// <summary>The issues on other URLs that name this one as the other URL involved; null before the first analysis.</summary>
    public NamedByIssues? NamedBy { get; init; }
''',
'''    /// <summary>The issues on other URLs that name this one as the other URL involved; null before the first analysis.</summary>
    public NamedByIssues? NamedBy { get; init; }

    /// <summary>
    /// The sitemaps that list the URL, read from the crawl's own tables: there with or without a URL summary, where
    /// <see cref="UrlLinkCounts.InSitemap"/> is the analysis's.
    /// </summary>
    public UrlSitemaps ListedIn { get; init; } = new(0, []);
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''    /// <summary>Tests only: each page of links as its query runs, with its parameters, so its plan can be asked for.</summary>
''',
'''    /// <summary>The most sitemaps the inspector names of those that list a URL; their number is given whole.</summary>
    public const int MAX_SITEMAPS_NAMED = 20;

    /// <summary>Tests only: each page of links as its query runs, with its parameters, so its plan can be asked for.</summary>
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''            NamedBy = context.Analysis is null ? null : NamedBy(context, urlId),
            Summary = context.Summary,
''',
'''            NamedBy = context.Analysis is null ? null : NamedBy(context, urlId),
            ListedIn = ListedIn(context, urlId),
            Summary = context.Summary,
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''    /// <summary>
    /// One page of the links to or from a URL, in the order they were found, a page's links to itself left out; null when
''',
'''    /// <summary>
    /// The sitemaps that list a URL, in the order the crawl found them. What each sitemap lists is keyed by sitemap, then
    /// URL, so each sitemap is asked whether it lists this one: a lookup a sitemap (a crawl reads at most a thousand), where
    /// finding the URL's rows without their sitemap would read every row of every sitemap.
    /// </summary>
    private static UrlSitemaps ListedIn(GridReadContext context, int urlId)
    {
        List<SitemapRef> sitemaps = [];
        long total = 0;
        using var command = context.Command(
            "SELECT s.id, s.url FROM sitemaps s WHERE EXISTS (SELECT 1 FROM sitemap_urls m WHERE m.sitemap_id = s.id AND m.url_id = $id) ORDER BY s.id", [new("$id", urlId)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (total++ < MAX_SITEMAPS_NAMED)
            {
                sitemaps.Add(new SitemapRef(reader.GetInt32(0), reader.GetString(1)));
            }
        }

        return new UrlSitemaps(total, sitemaps);
    }

    /// <summary>
    /// One page of the links to or from a URL, in the order they were found, a page's links to itself left out; null when
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''        var robotsTxt = !self.Fetched
            ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.NotChecked)
            : refused
                ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.Fail)
                {
                    Unreachable = error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true
                }
                : new IndexabilityTest(IndexabilityTestKind.RobotsTxt, context.Options.RespectRobotsTxt ? TestOutcome.Pass : TestOutcome.NotChecked);
''',
'''        // The origin whose robots.txt decides the URL, named where the crawl has a file for it to show; for a URL it
        // refused, with why, and the line when a rule did: what the origin's own list of blocked URLs says of it.
        var url = row.GetString(0);
        var origin = RobotsReader.OriginOf(url);
        var why = refused ? RobotsReader.WhyRefused(context, self.Id, url, error) : null;
        var robotsTxt = (!self.Fetched
            ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.NotChecked)
            : refused
                ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.Fail)
                {
                    Unreachable = error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true,
                    Reason = why!.Reason,
                    Line = why.Line,
                    Rule = why.Rule
                }
                : new IndexabilityTest(IndexabilityTestKind.RobotsTxt, context.Options.RespectRobotsTxt ? TestOutcome.Pass : TestOutcome.NotChecked)) with
        {
            Origin = origin is not null && (refused || RobotsReader.HasAnswer(context, origin)) ? origin : null
        };
'''),
]
