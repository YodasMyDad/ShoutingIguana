using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Builds <c>url_summary</c> inside the analysis's transaction, once the issues are stored. One pass over the URLs in id
/// order, merged with each URL's issue counts (both streamed from SQLite) and with what the analysis judged, which its
/// <see cref="UrlTable"/> already holds: so nothing more is kept per URL, and the grid and the checks can't disagree.
/// The indexes come after the rows.
/// </summary>
internal static class UrlSummaryBuilder
{
    /// <summary>The table's columns, in order, as the builder writes them and <see cref="UrlSummaryReader"/> reads them.</summary>
    public static readonly string[] COLUMNS =
    [
        "url_id", "is_internal", "source", "depth", "resource_type", "status_code", "indexable", "indexability_reason", "final_url_id",
        "final_status_code", "in_sitemap", "inlinks", "followed_inlinks", "nofollow_inlinks", "linking_pages", "outlinks", "issues",
        "errors", "warnings", "notices", "size_bytes", "response_ms", "word_count", "title_length", "description_length"
    ];

    /// <summary>The URL's own facts that the table in memory doesn't hold, and its address for its type.</summary>
    private const string URLS = """
        SELECT u.id, r.outcome, r.content_type, r.response_ms, r.word_count, r.outlink_count, u.url, r.title, r.meta_description
        FROM urls u LEFT JOIN responses r ON r.url_id = u.id
        ORDER BY u.id
        """;

    /// <summary>Checks that flagged each URL, by severity, in URL id order (the url_id index covers it).</summary>
    private static readonly string ISSUE_COUNTS = $"""
        SELECT i.url_id, COUNT(*), SUM(t.severity = '{nameof(IssueSeverity.Error)}'), SUM(t.severity = '{nameof(IssueSeverity.Warning)}'),
               SUM(t.severity = '{nameof(IssueSeverity.Notice)}')
        FROM issues i JOIN issue_types t ON t.id = i.issue
        GROUP BY i.url_id
        ORDER BY i.url_id
        """;

    /// <param name="sitemapsCovered">
    /// Every sitemap was read, so a URL none lists isn't in them: the analysis ran the not-in-sitemap check. Otherwise only
    /// a listing is known.
    /// </param>
    /// <param name="progress">Told of each row as it is written.</param>
    /// <returns>The rows written: one per URL.</returns>
    public static long Build(SqliteConnection connection, SqliteTransaction transaction, UrlTable urls, bool sitemapsCovered,
        AnalysisProgressReporter? progress = null)
    {
        CrawlDatabase.Execute(connection, CrawlSchema.URL_SUMMARY, transaction);

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO url_summary ({string.Join(", ", COLUMNS)}) VALUES ({string.Join(", ", COLUMNS.Select(column => "$" + column))})";
        var parameters = COLUMNS.Select(column => insert.Parameters.Add("$" + column, SqliteType.Integer)).ToArray();
        insert.Prepare();

        long written = 0;
        using (var urlQuery = Command(connection, transaction, URLS))
        using (var issueQuery = Command(connection, transaction, ISSUE_COUNTS))
        using (var urlRows = urlQuery.ExecuteReader())
        using (var issueRows = issueQuery.ExecuteReader())
        {
            var issuesLeft = issueRows.Read();
            while (urlRows.Read())
            {
                var id = urlRows.GetInt32(0);
                while (issuesLeft && issueRows.GetInt32(0) < id)
                {
                    issuesLeft = issueRows.Read();
                }

                var flagged = issuesLeft && issueRows.GetInt32(0) == id;
                Bind(parameters, Summarise(urls, id, urlRows, flagged ? issueRows : null, sitemapsCovered));
                insert.ExecuteNonQuery();
                written++;
                progress?.Advance(written);
            }
        }

        CrawlDatabase.Execute(connection, CrawlSchema.URL_SUMMARY_INDEXES, transaction);
        return written;
    }

    /// <summary>One URL's row: what the analysis made of it (<paramref name="urls"/>), with the crawl's own numbers.</summary>
    /// <param name="issues">The URL's issue counts, or null when no check flagged it.</param>
    private static UrlSummary Summarise(UrlTable urls, int id, SqliteDataReader row, SqliteDataReader? issues, bool sitemapsCovered)
    {
        ref readonly var status = ref urls[id];

        // A URL whose host had stopped answering was never asked, so the analysis counts it as unfetched; so does this.
        var fetched = status.Fetched;
        var answered = fetched && !row.IsDBNull(1) && (FetchOutcome)row.GetInt32(1) == FetchOutcome.Success;

        // The crawler read the HTML (a word count says so). A page outside the start folder, or in an iframe, was only
        // status-checked: its meta robots, canonical and links were never seen, so only what its headers prove is known.
        var read = fetched && !row.IsDBNull(4);
        var unknownIndexability = answered && status is { IsHtml: true, IndexabilityReason: IndexabilityReason.None } && !read;
        var judged = fetched && !unknownIndexability;

        // Judged as the analysis judges it: by the content type of a successful response, otherwise by the extension.
        var type = answered && !row.IsDBNull(2) ? ResourceTypes.OfContentType(row.GetString(2)) : ResourceTypes.OfUrl(row.GetString(6));

        // Meta refreshes included: the table treats an instant one as a redirect.
        var final = urls.TraceRedirects(id).FinalId;
        ref readonly var end = ref urls[final];
        var inlinks = urls.Inlinks(id);
        var nofollow = urls.NofollowLinks(id);
        return new UrlSummary
        {
            UrlId = id,
            IsInternal = status.IsInternal,
            Source = status.Source,
            Depth = status.Depth >= CrawlDepth.UNLINKED ? null : status.Depth,
            ResourceType = type,
            StatusCode = fetched && status.StatusCode > 0 ? status.StatusCode : null,
            IsIndexable = judged ? status is { Outcome: FetchOutcome.Success, IndexabilityReason: IndexabilityReason.None } : null,
            IndexabilityReason = judged ? status.IndexabilityReason : null,
            FinalUrlId = final,
            FinalStatusCode = end is { Fetched: true, StatusCode: > 0 } ? end.StatusCode : null,
            InSitemap = status.InSitemap ? true : sitemapsCovered ? false : null,
            Inlinks = inlinks,
            FollowedInlinks = inlinks - nofollow,
            NofollowInlinks = nofollow,
            LinkingPages = urls.LinkingPages(id),
            Outlinks = read ? row.GetInt32(5) : null,
            Issues = issues?.GetInt32(1) ?? 0,
            Errors = issues?.GetInt32(2) ?? 0,
            Warnings = issues?.GetInt32(3) ?? 0,
            Notices = issues?.GetInt32(4) ?? 0,
            SizeBytes = fetched ? status.SizeBytes : null,

            // robots.txt refusing a URL is recorded without a request, so there's no time to show.
            ResponseMs = fetched && status.Outcome != FetchOutcome.RobotsDisallowed && !row.IsDBNull(3) ? row.GetInt32(3) : null,
            WordCount = fetched && !row.IsDBNull(4) ? row.GetInt32(4) : null,

            // Measured as the title and description checks measure them, so a sort by length agrees with what they flag.
            TitleLength = fetched && !row.IsDBNull(7) ? DisplayWidth.Measure(row.GetString(7)).Width : null,
            DescriptionLength = fetched && !row.IsDBNull(8) ? DisplayWidth.Measure(row.GetString(8)).Width : null
        };
    }

    private static void Bind(SqliteParameter[] parameters, UrlSummary summary)
    {
        object?[] values =
        [
            summary.UrlId, summary.IsInternal, (int)summary.Source, summary.Depth, (int)summary.ResourceType, summary.StatusCode, summary.IsIndexable,
            (int?)summary.IndexabilityReason, summary.FinalUrlId, summary.FinalStatusCode, summary.InSitemap, summary.Inlinks,
            summary.FollowedInlinks, summary.NofollowInlinks, summary.LinkingPages, summary.Outlinks, summary.Issues, summary.Errors,
            summary.Warnings, summary.Notices, summary.SizeBytes, summary.ResponseMs, summary.WordCount, summary.TitleLength,
            summary.DescriptionLength
        ];

        for (var index = 0; index < parameters.Length; index++)
        {
            parameters[index].Value = values[index] ?? DBNull.Value;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
