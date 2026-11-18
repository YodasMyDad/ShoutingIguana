using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// Every link's grid values worked out from a crawl's own rows by plain SQL and C#, without the links grid's reader: its
/// own columns here, and each end's from the URL grid's reference (<see cref="GridReference"/>). The reader's pages,
/// counts and facets are checked against it.
/// </summary>
internal sealed class LinkReference
{
    private readonly GridReference _urls;

    private LinkReference(GridReference urls, List<Link> links)
    {
        _urls = urls;
        Links = links;
        ById = links.ToDictionary(link => link.Id);
    }

    /// <summary>One stored link, as the reference reads it.</summary>
    public sealed record Link(long Id, int From, int To, long Kind, long Flags, long Position, string? Text, string? Fragment, bool SourceNofollow)
    {
        public bool? Followed => Kind == (long)LinkKind.Hyperlink ? (Flags & (long)LinkFlags.NoFollow) == 0 && !SourceNofollow : null;
    }

    /// <summary>Every link, in crawl order.</summary>
    public IReadOnlyList<Link> Links { get; }

    public IReadOnlyDictionary<long, Link> ById { get; }

    public static LinkReference Load(string path, GridReference urls)
    {
        List<Link> links = [];
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.rowid, l.from_id, l.to_id, l.kind, l.flags, l.position, l.text, l.fragment, COALESCE(src.is_nofollow, 0)
            FROM links l LEFT JOIN responses src ON src.url_id = l.from_id ORDER BY l.rowid
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            links.Add(new Link(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8) != 0));
        }

        return new LinkReference(urls, links);
    }

    /// <summary>A column's value for a link, as the grid shows it: its own, or an end's URL grid column.</summary>
    public object? Value(Link link, string column) => column switch
    {
        "id" => link.Id,
        "kind" => Name<LinkKind>(link.Kind),
        "position" => Name<LinkPosition>(link.Position),
        "text" => link.Text,
        "fragment" => link.Fragment,
        "noFollow" => (link.Flags & (long)LinkFlags.NoFollow) != 0,
        "sponsored" => (link.Flags & (long)LinkFlags.Sponsored) != 0,
        "ugc" => (link.Flags & (long)LinkFlags.Ugc) != 0,
        "imageLink" => (link.Flags & (long)LinkFlags.ImageLink) != 0,
        "selfLink" => link.From == link.To,
        "followed" => link.Followed,
        _ when column.StartsWith("from.", StringComparison.Ordinal) => _urls.Rows[link.From][column[5..]],
        _ when column.StartsWith("to.", StringComparison.Ordinal) => _urls.Rows[link.To][column[3..]],
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
    };

    /// <summary>The links every filter matches and the search finds, in crawl order.</summary>
    public List<Link> Matching(IReadOnlyList<UrlFilter> filters, string? search = null)
    {
        // A filter on an end is the URL grid's filter on that URL: the reference's own evaluator says which URLs pass.
        List<Func<Link, bool>> tests = [];
        foreach (var filter in filters)
        {
            if (filter.Column == "check")
            {
                // The links a check counts: from a page it flagged, to a URL that page's issue names.
                tests.Add(link => filter.Values!.Any(check => _urls.Targets.TryGetValue((check.Text!, link.From), out var targets) && targets.Contains(link.To)));
            }
            else if (filter.Column.StartsWith("from.", StringComparison.Ordinal) || filter.Column.StartsWith("to.", StringComparison.Ordinal))
            {
                var from = filter.Column.StartsWith("from.", StringComparison.Ordinal);
                var urls = _urls.Matching([filter with { Column = filter.Column[(from ? 5 : 3)..] }]).ToHashSet();
                tests.Add(link => urls.Contains(from ? link.From : link.To));
            }
            else
            {
                tests.Add(link => GridReference.Holds(Value(link, filter.Column), filter));
            }
        }

        if (search is not null)
        {
            tests.Add(link => Contains(link.Text, search) || Contains((string?)_urls.Rows[link.From]["url"], search) || Contains((string?)_urls.Rows[link.To]["url"], search));
        }

        return [.. Links.Where(link => tests.All(test => test(link)))];
    }

    /// <summary>
    /// Links in a sort's order: by id, or by a column of the target as the URL grid orders its URLs (unknown values last,
    /// the target's id breaking ties in the key's direction), a target's links together in crawl order.
    /// </summary>
    public List<Link> Order(IEnumerable<Link> links, UrlSort? sort)
    {
        var all = links.ToList();
        if (sort is null || sort.Column == "id")
        {
            return [.. sort?.Descending == true ? all.OrderByDescending(link => link.Id) : all.OrderBy(link => link.Id)];
        }

        var place = _urls.Order([new UrlSort(sort.Column[3..], sort.Descending)], all.Select(link => link.To).Distinct())
            .Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        return [.. all.OrderBy(link => place[link.To]).ThenBy(link => link.Id)];
    }

    private static string? Name<TEnum>(long value) where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), (byte)Math.Clamp(value, 0, 255)) && value is >= 0 and <= 255
            ? char.ToLowerInvariant(Enum.GetName(typeof(TEnum), (byte)value)![0]) + Enum.GetName(typeof(TEnum), (byte)value)![1..]
            : null;

    private static bool Contains(string? value, string part) =>
        value is not null && Lower(value).Contains(Lower(part), StringComparison.Ordinal);

    private static string Lower(string text) => string.Concat(text.Select(character => character is >= 'A' and <= 'Z' ? (char)(character + 32) : character));
}
