using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The links grid's columns. A link's own say what it is, each with its SQL over <c>links l</c> and, where the link
/// summary keeps it, over <c>link_summary c</c>, so a filter on it can be counted there. The rest are the URL grid's
/// columns for either end, named <c>from.</c> and <c>to.</c>: they are read, filtered and sorted by the URL grid's own
/// code, never restated here.
/// </summary>
internal static class LinkColumns
{
    public const string FROM = "from.";

    public const string TO = "to.";

    /// <summary>Stands, in a column's SQL over <c>links</c>, for whether the link's page says nofollow: a set the read holds.</summary>
    public const string SOURCE_NOFOLLOW = "@sourceNofollow";

    /// <summary>Each flag column's bit of a link's flags. First, as the columns below fill it in as they're made.</summary>
    private static readonly Dictionary<string, long> FLAG_BITS = new(StringComparer.Ordinal);

    private static readonly IReadOnlyList<string> KINDS = [.. Enum.GetNames<LinkKind>().Select(Camel)];

    private static readonly IReadOnlyList<string> POSITIONS = [.. Enum.GetNames<LinkPosition>().Select(Camel)];

    public static readonly UrlColumnDefinition ID = new("id", UrlColumnType.Number) { CrawlSql = "l.rowid", Nullable = false, Indexed = true };

    public static readonly UrlColumnDefinition KIND = new("kind", UrlColumnType.Enum)
    {
        CrawlSql = Known("l.kind", KINDS.Count), SummarySql = Known("c.kind", KINDS.Count), Sortable = false, Values = KINDS
    };

    public static readonly UrlColumnDefinition TEXT = new("text", UrlColumnType.Text) { CrawlSql = "l.text", Sortable = false };

    public static readonly UrlColumnDefinition POSITION = new("position", UrlColumnType.Enum)
    {
        CrawlSql = Known("l.position", POSITIONS.Count), SummarySql = Known("c.position", POSITIONS.Count), Sortable = false, Values = POSITIONS
    };

    public static readonly UrlColumnDefinition SELF_LINK = new("selfLink", UrlColumnType.Boolean)
    {
        CrawlSql = "(l.from_id = l.to_id)", SummarySql = "(c.self <> 0)", Nullable = false, Sortable = false
    };

    /// <summary>Whether search engines follow a hyperlink: no nofollow on it, nor on its page. Unknown for anything else.</summary>
    public static readonly UrlColumnDefinition FOLLOWED = new("followed", UrlColumnType.Boolean)
    {
        CrawlSql = $"(CASE WHEN l.kind = {(int)LinkKind.Hyperlink} THEN ((l.flags & {(int)LinkFlags.NoFollow}) = 0 AND NOT {SOURCE_NOFOLLOW}) END)",
        SummarySql = $"(CASE WHEN c.kind = {(int)LinkKind.Hyperlink} THEN ((c.flags & {(int)LinkFlags.NoFollow}) = 0 AND c.source_nofollow = 0) END)",
        Sortable = false
    };

    public static readonly UrlColumnDefinition FRAGMENT = new("fragment", UrlColumnType.Text) { CrawlSql = "l.fragment", Sortable = false };

    /// <summary>Whether a link has text, as the link summary keeps it: the text column's, for "is empty" alone.</summary>
    public const string HAS_TEXT = "(CASE WHEN c.has_text <> 0 THEN 'x' END)";

    /// <summary>A link's own columns, in the order the column chooser lists them.</summary>
    public static readonly IReadOnlyList<UrlColumnDefinition> OWN =
    [
        ID, KIND, TEXT, POSITION, Flag("noFollow", LinkFlags.NoFollow), Flag("sponsored", LinkFlags.Sponsored), Flag("ugc", LinkFlags.Ugc),
        Flag("imageLink", LinkFlags.ImageLink), SELF_LINK, FOLLOWED, FRAGMENT
    ];

    private static readonly Dictionary<string, UrlColumnDefinition> BY_ID = OWN.ToDictionary(column => column.Id, StringComparer.Ordinal);

    /// <summary>The link's own column with this id, or null.</summary>
    public static UrlColumnDefinition? Own(string? id) => id is null ? null : BY_ID.GetValueOrDefault(id);

    /// <summary>
    /// The URL grid's column a <c>from.</c> or <c>to.</c> column stands for, with which end it's of; null for anything else.
    /// The check filter, which is no column, is one too (<paramref name="filter"/>).
    /// </summary>
    public static (string Prefix, UrlColumnDefinition? Column)? End(string? id, bool filter = false)
    {
        var prefix = id is null ? null : id.StartsWith(TO, StringComparison.Ordinal) ? TO : id.StartsWith(FROM, StringComparison.Ordinal) ? FROM : null;
        if (prefix is null)
        {
            return null;
        }

        var name = id![prefix.Length..];
        return UrlColumns.Find(name, issueColumns: false) is { } column ? (prefix, column) : filter && name == UrlColumns.CHECK ? (prefix, null) : null;
    }

    /// <summary>The columns for the column chooser and the filter bar: a link's own, then each end's. Only the target's sort.</summary>
    public static IReadOnlyList<UrlColumnInfo> Describe() =>
    [
        .. OWN.Select(column => new UrlColumnInfo(column.Id, column.Type, false, column.Sortable, column.Filterable, column.Values)),
        .. new[] { FROM, TO }.SelectMany(prefix => UrlColumns.Describe(issueColumns: false)
            .Select(column => column with { Id = prefix + column.Id, Sortable = prefix == TO && column.Sortable }))
    ];

    /// <summary>A link's own column's value, from the link as stored.</summary>
    public static object? Value(UrlColumnDefinition column, long id, long fromId, long toId, long kind, string? text, long position, long flags, string? fragment,
        bool sourceNofollow)
    {
        if (column == ID)
        {
            return id;
        }

        if (column == KIND)
        {
            return kind >= 0 && kind < KINDS.Count ? KINDS[(int)kind] : null;
        }

        if (column == POSITION)
        {
            return position >= 0 && position < POSITIONS.Count ? POSITIONS[(int)position] : null;
        }

        if (column == TEXT)
        {
            return text;
        }

        if (column == FRAGMENT)
        {
            return fragment;
        }

        if (column == SELF_LINK)
        {
            return fromId == toId;
        }

        if (column == FOLLOWED)
        {
            return kind == (int)LinkKind.Hyperlink ? (flags & (long)LinkFlags.NoFollow) == 0 && !sourceNofollow : null;
        }

        return (flags & FLAG_BITS[column.Id]) != 0;
    }

    private static UrlColumnDefinition Flag(string id, LinkFlags flag)
    {
        FLAG_BITS[id] = (long)flag;
        return new UrlColumnDefinition(id, UrlColumnType.Boolean)
        {
            CrawlSql = $"((l.flags & {(int)flag}) <> 0)", SummarySql = $"((c.flags & {(int)flag}) <> 0)", Nullable = false, Sortable = false
        };
    }

    /// <summary>A stored number that names a value, or null for one this engine has no name for (a newer engine's, a crafted file's).</summary>
    private static string Known(string sql, int count) => $"(CASE WHEN {sql} >= 0 AND {sql} < {count} THEN {sql} END)";

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
