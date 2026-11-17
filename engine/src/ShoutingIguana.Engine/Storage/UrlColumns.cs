using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The URL grid's columns, and how each reads in either mode. With a URL summary, sorts and the common filters read its
/// narrow row (<c>s</c>), which the indexes serve; the crawl's text (titles, the canonical) is read through subqueries
/// so they never join. Without one, everything reads the crawl's own tables (<c>u</c>, <c>r</c>, <c>e</c>), with the same
/// meaning as the summary's columns: the summary is built from those tables, as <see cref="UrlSummaryBuilder"/> does.
/// </summary>
internal static class UrlColumns
{
    /// <summary>The filter that's a check, not a column: the URLs it flagged.</summary>
    public const string CHECK = "check";

    /// <summary>Stands for the crawl's indexability reason, which reads the analysis's where an analysis made one.</summary>
    public const string REASON = "@reason";

    /// <summary>The response row is a fetch: there is one, and it isn't one stored without asking, its host having stopped answering.</summary>
    public static readonly string FETCHED = $"(r.url_id IS NOT NULL AND NOT {AnalysisReader.NOT_CHECKED})";

    /// <summary>
    /// A page answered but only status-checked, so its HTML (meta robots, canonical) was never read: its indexability is
    /// unknown unless its response rules it out, as the summary says (<see cref="UrlSummary.IsIndexable"/>).
    /// </summary>
    private static readonly string UNREAD_PAGE = $"(r.outcome = {(int)FetchOutcome.Success} AND r.is_html = 1 AND {REASON} = 0 AND r.word_count IS NULL)";

    private static readonly string JUDGED = $"({FETCHED} AND NOT {UNREAD_PAGE})";

    /// <summary>A status the crawl got, as the summary keeps it: none when not fetched, or no HTTP answer.</summary>
    private static string Status(string alias) =>
        $"CASE WHEN {alias}.url_id IS NOT NULL AND NOT {AnalysisReader.NOT_CHECKED.Replace("r.", alias + ".", StringComparison.Ordinal)} AND {alias}.status_code > 0 THEN {alias}.status_code END";

    /// <summary>The status classes, numbered in this order by the SQL that gives a URL's.</summary>
    public static readonly IReadOnlyList<string> STATUS_CLASSES = ["2xx", "3xx", "4xx", "5xx", "other", "noResponse"];

    public static readonly UrlColumnDefinition ID = new("id", UrlColumnType.Number)
    {
        SummarySql = "s.url_id", CrawlSql = "u.id", Indexed = true, Nullable = false
    };

    public static readonly UrlColumnDefinition URL = new("url", UrlColumnType.Text)
    {
        Source = ColumnSource.Urls, SummarySql = "u.url", CrawlSql = "u.url", Indexed = true, Nullable = false
    };

    public static readonly UrlColumnDefinition STATUS_CODE = new("statusCode", UrlColumnType.Number)
    {
        SummarySql = "s.status_code", CrawlSql = Status("r"), Indexed = true
    };

    /// <summary>A URL's status class, numbered as <see cref="STATUS_CLASSES"/>; it sorts as the status code.</summary>
    public static readonly UrlColumnDefinition STATUS_CLASS = new("statusClass", UrlColumnType.Enum)
    {
        SummarySql = StatusClass("s.status_code", "(s.status_code IS NULL AND s.indexability_reason IS NOT NULL)"),
        CrawlSql = StatusClass(Status("r"), $"({FETCHED} AND COALESCE(r.status_code, 0) <= 0)"),
        Indexed = true, Values = STATUS_CLASSES
    };

    public static readonly UrlColumnDefinition INDEXABLE = new("indexable", UrlColumnType.Boolean)
    {
        SummarySql = "s.indexable", CrawlSql = $"CASE WHEN {JUDGED} THEN {REASON} = 0 END"
    };

    public static readonly UrlColumnDefinition INDEXABILITY = new("indexability", UrlColumnType.Enum)
    {
        SummarySql = "s.indexability_reason", CrawlSql = $"CASE WHEN {JUDGED} THEN {REASON} END", Indexed = true,
        Values = ["none", "noindex", "canonicalised", "redirected", "clientError", "serverError", "blockedByRobots", "blocked", "unreachable"]
    };

    public static readonly UrlColumnDefinition TITLE = new("title", UrlColumnType.Text)
    {
        Source = ColumnSource.Responses, CrawlSql = "r.title", Sortable = false
    };

    public static readonly UrlColumnDefinition DETAIL = new("detail", UrlColumnType.Text)
    {
        Source = ColumnSource.Issues, CrawlSql = "i.detail"
    };

    public static readonly UrlColumnDefinition RELATED_URL = new("relatedUrl", UrlColumnType.Text)
    {
        Source = ColumnSource.Issues, CrawlSql = "(SELECT ru.url FROM urls ru WHERE ru.id = i.related_url_id)", UrlIdSql = "i.related_url_id",
        Sortable = false
    };

    /// <summary>Every column, in the order the column chooser lists them; a check's own columns last.</summary>
    public static readonly IReadOnlyList<UrlColumnDefinition> ALL =
    [
        ID,
        URL,
        new("fetched", UrlColumnType.Boolean)
        {
            SummarySql = "(s.status_code IS NOT NULL OR s.indexability_reason IS NOT NULL)", CrawlSql = FETCHED, Nullable = false
        },
        new("isInternal", UrlColumnType.Boolean) { SummarySql = "s.is_internal", CrawlSql = "u.is_internal", Nullable = false },
        new("source", UrlColumnType.Enum)
        {
            SummarySql = "s.source", CrawlSql = "u.source", Nullable = false, Values = ["seed", "link", "sitemap", "siteVersion"]
        },
        new("depth", UrlColumnType.Number)
        {
            SummarySql = "s.depth", CrawlSql = $"CASE WHEN u.depth >= {CrawlDepth.UNLINKED} THEN NULL ELSE u.depth END", Indexed = true
        },
        new("type", UrlColumnType.Enum)
        {
            SummarySql = "s.resource_type",
            CrawlSql = $"{ReaderFunctions.RESOURCE_TYPE}(u.url, CASE WHEN {FETCHED} AND r.outcome = {(int)FetchOutcome.Success} THEN r.content_type END)",
            Indexed = true, Nullable = false, Values = ["page", "image", "script", "stylesheet", "font", "media", "pdf", "other"]
        },
        STATUS_CODE,
        STATUS_CLASS,
        new("outcome", UrlColumnType.Enum)
        {
            Source = ColumnSource.Responses, Sortable = false,
            CrawlSql = $"CASE WHEN r.url_id IS NULL THEN NULL WHEN {AnalysisReader.NOT_CHECKED} THEN {(int)UrlOutcome.NotChecked} ELSE r.outcome END",
            Values = Enum.GetNames<UrlOutcome>().Select(Camel).ToList()
        },
        new("redirectTarget", UrlColumnType.Text)
        {
            Source = ColumnSource.Responses, CrawlSql = "(SELECT rt.url FROM urls rt WHERE rt.id = r.redirect_url_id)", UrlIdSql = "r.redirect_url_id",
            Sortable = false
        },
        new("redirectTargetStatusCode", UrlColumnType.Number)
        {
            Source = ColumnSource.Responses, CrawlSql = $"(SELECT {Status("rs")} FROM responses rs WHERE rs.url_id = r.redirect_url_id)", Sortable = false
        },
        new("redirectChain", UrlColumnType.Object) { Source = ColumnSource.Computed, Sortable = false, Filterable = false },
        new("fetchError", UrlColumnType.Text)
        {
            Source = ColumnSource.Responses, CrawlSql = $"CASE WHEN {FETCHED} AND r.outcome <> {(int)FetchOutcome.Success} THEN r.error END", Sortable = false
        },
        new("robotsRule", UrlColumnType.Object) { Source = ColumnSource.Computed, Sortable = false, Filterable = false },
        INDEXABLE,
        INDEXABILITY,
        new("noindex", UrlColumnType.Boolean)
        {
            Source = ColumnSource.Responses, CrawlSql = "CASE WHEN r.is_noindex = 1 THEN 1 WHEN r.word_count IS NOT NULL THEN 0 END", Sortable = false
        },
        new("nofollow", UrlColumnType.Boolean)
        {
            Source = ColumnSource.Responses, CrawlSql = "CASE WHEN r.is_nofollow = 1 THEN 1 WHEN r.word_count IS NOT NULL THEN 0 END", Sortable = false
        },
        new("metaRobots", UrlColumnType.Text) { Source = ColumnSource.Details, Sortable = false, Filterable = false },
        new("xRobotsTag", UrlColumnType.Text) { Source = ColumnSource.Details, Sortable = false, Filterable = false },
        new("canonicalUrl", UrlColumnType.Text)
        {
            Source = ColumnSource.Responses, CrawlSql = "(SELECT c.url FROM urls c WHERE c.id = r.canonical_url_id)", UrlIdSql = "r.canonical_url_id",
            Sortable = false
        },
        new("contentType", UrlColumnType.Text) { Source = ColumnSource.Responses, CrawlSql = $"CASE WHEN {FETCHED} THEN r.content_type END", Sortable = false },
        TITLE,
        new("description", UrlColumnType.Text) { Source = ColumnSource.Responses, CrawlSql = "r.meta_description", Sortable = false },
        new("h1", UrlColumnType.Text) { Source = ColumnSource.Responses, CrawlSql = "r.h1", Sortable = false },
        new("h1Count", UrlColumnType.Number)
        {
            Source = ColumnSource.Responses, CrawlSql = "CASE WHEN r.word_count IS NOT NULL THEN r.h1_count END", Sortable = false
        },
        new("wordCount", UrlColumnType.Number) { SummarySql = "s.word_count", CrawlSql = $"CASE WHEN {FETCHED} THEN r.word_count END", Indexed = true },
        new("outlinks", UrlColumnType.Number)
        {
            SummarySql = "s.outlinks", CrawlSql = $"CASE WHEN {FETCHED} AND r.word_count IS NOT NULL THEN r.outlink_count END"
        },
        new("sizeBytes", UrlColumnType.Number) { SummarySql = "s.size_bytes", CrawlSql = $"CASE WHEN {FETCHED} THEN r.size_bytes END", Indexed = true },
        new("pixelWidth", UrlColumnType.Number) { Source = ColumnSource.Computed, Sortable = false, Filterable = false },
        new("pixelHeight", UrlColumnType.Number) { Source = ColumnSource.Computed, Sortable = false, Filterable = false },
        new("responseMs", UrlColumnType.Number)
        {
            SummarySql = "s.response_ms", Indexed = true,
            CrawlSql = $"CASE WHEN {FETCHED} AND r.outcome <> {(int)FetchOutcome.RobotsDisallowed} THEN r.response_ms END"
        },
        new("ttfbMs", UrlColumnType.Number)
        {
            Source = ColumnSource.Responses, Sortable = false,
            CrawlSql = $"CASE WHEN {FETCHED} AND r.outcome <> {(int)FetchOutcome.RobotsDisallowed} THEN r.ttfb_ms END"
        },
        new("titleLength", UrlColumnType.Number) { SummarySql = "s.title_length" },
        new("descriptionLength", UrlColumnType.Number) { SummarySql = "s.description_length" },
        new("finalUrl", UrlColumnType.Text)
        {
            SummarySql = "(SELECT f.url FROM urls f WHERE f.id = s.final_url_id)", UrlIdSql = "s.final_url_id", Nullable = false, Sortable = false
        },
        new("finalStatusCode", UrlColumnType.Number) { SummarySql = "s.final_status_code" },
        new("inSitemap", UrlColumnType.Boolean) { SummarySql = "s.in_sitemap" },
        new("inlinks", UrlColumnType.Number) { SummarySql = "s.inlinks", Nullable = false },
        new("followedInlinks", UrlColumnType.Number) { SummarySql = "s.followed_inlinks", Nullable = false },
        new("nofollowInlinks", UrlColumnType.Number) { SummarySql = "s.nofollow_inlinks", Nullable = false },
        new("linkingPages", UrlColumnType.Number) { SummarySql = "s.linking_pages", Indexed = true, Nullable = false },
        new("usedOn", UrlColumnType.Number) { SummarySql = "s.used_on", Nullable = false },
        new("issues", UrlColumnType.Number) { SummarySql = "s.issues", Indexed = true, Nullable = false },
        new("errors", UrlColumnType.Number) { SummarySql = "s.errors", Nullable = false },
        new("warnings", UrlColumnType.Number) { SummarySql = "s.warnings", Nullable = false },
        new("notices", UrlColumnType.Number) { SummarySql = "s.notices", Nullable = false },
        DETAIL,
        new("relatedUrlId", UrlColumnType.Number) { Source = ColumnSource.Issues, CrawlSql = "i.related_url_id" },
        RELATED_URL,
        new("relatedStatusCode", UrlColumnType.Number)
        {
            Source = ColumnSource.Issues, Sortable = false,
            SummarySql = "(SELECT rs.status_code FROM url_summary rs WHERE rs.url_id = i.related_url_id)",
            CrawlSql = $"(SELECT {Status("rs")} FROM responses rs WHERE rs.url_id = i.related_url_id)"
        }
    ];

    private static readonly Dictionary<string, UrlColumnDefinition> BY_ID = ALL.ToDictionary(column => column.Id, StringComparer.Ordinal);

    /// <summary>The column with this id, or null; a check's own columns only when <paramref name="issueColumns"/>.</summary>
    public static UrlColumnDefinition? Find(string? id, bool issueColumns) =>
        id is not null && BY_ID.GetValueOrDefault(id) is { } column && (issueColumns || !column.IsIssueColumn) ? column : null;

    /// <summary>The columns for the column chooser: the URL grid's, or a check's rows' too.</summary>
    public static IReadOnlyList<UrlColumnInfo> Describe(bool issueColumns) =>
    [
        .. ALL.Where(column => issueColumns || !column.IsIssueColumn)
            .Select(column => ColumnWords.Url(new UrlColumnInfo(column.Id, column.Type, column.NeedsAnalysis, column.Sortable, column.Filterable, column.Values)))
    ];

    /// <summary>
    /// 0 to 5 for <see cref="STATUS_CLASSES"/>, from a status (null when there is none) and whether the URL was fetched
    /// without an HTTP answer; null when not fetched.
    /// </summary>
    private static string StatusClass(string status, string noResponse) => $"""
        CASE WHEN {status} >= 200 AND {status} <= 299 THEN 0 WHEN {status} >= 300 AND {status} <= 399 THEN 1
             WHEN {status} >= 400 AND {status} <= 499 THEN 2 WHEN {status} >= 500 AND {status} <= 599 THEN 3
             WHEN {status} IS NOT NULL THEN 4 WHEN {noResponse} THEN 5 END
        """;

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
