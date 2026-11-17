using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// Grid queries from JSON to the engine's, and filters back to JSON for the facets. What the JSON can't say (an operator
/// that doesn't exist, a value that's an object) is an <c>invalid-query</c> field error here; whether the column takes
/// it, the engine says.
/// </summary>
internal static class GridRequests
{
    private static readonly Dictionary<string, FilterOperator> OPERATORS =
        Enum.GetValues<FilterOperator>().ToDictionary(op => Camel(op.ToString()), StringComparer.Ordinal);

    private static readonly Dictionary<string, LinkKind> KINDS = Enum.GetValues<LinkKind>().ToDictionary(kind => Camel(kind.ToString()), StringComparer.Ordinal);

    private static readonly JsonElement NULL = JsonDocument.Parse("null").RootElement.Clone();

    public static UrlQuery Query(GridQueryRequest? request)
    {
        request ??= new GridQueryRequest();
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        foreach (var index in Nulls(request.Columns))
        {
            Add(errors, $"columns[{index}]", "Give a column's id.");
        }

        var filters = Filters(request.Filters, errors);
        var sort = Sorts(request.Sort, errors);
        var ends = LinkEnds(request.LinkEnds, errors);
        ThrowIfAny(errors);
        return new UrlQuery
        {
            Columns = request.Columns ?? [],
            Filters = filters,
            Search = request.Search,
            LinkEnds = ends,
            Sort = sort,
            Offset = request.Offset ?? 0,
            Limit = request.Limit ?? UrlGridReader.DEFAULT_LIMIT,
            After = request.After,
            HiddenChecks = request.HiddenChecks ?? []
        };
    }

    /// <summary>A grid's sort keys, in order; a key without a column is a field error under <c>sort[i]</c>.</summary>
    public static List<UrlSort> Sorts(IReadOnlyList<UrlSortDto?>? keys, Dictionary<string, List<string>> errors)
    {
        List<UrlSort> sort = [];
        foreach (var (key, index) in (keys ?? []).Select((key, index) => (key, index)))
        {
            if (key is null)
            {
                Add(errors, $"sort[{index}]", "Give a column to sort by, as { \"column\": \"…\", \"desc\": true }.");
            }
            else if (string.IsNullOrEmpty(key.Column))
            {
                Add(errors, $"sort[{index}].column", "Give the column to sort by.");
            }
            else
            {
                sort.Add(new UrlSort(key.Column, key.Desc));
            }
        }

        return sort;
    }

    public static List<UrlFilter> Filters(IReadOnlyList<UrlFilterDto>? filters, Dictionary<string, List<string>> errors)
    {
        List<UrlFilter> compiled = [];
        foreach (var (filter, index) in (filters ?? []).Select((filter, index) => (filter, index)))
        {
            var field = $"filters[{index}]";
            if (filter is null)
            {
                Add(errors, field, "Give a filter: a column, an operator and what it takes.");
                continue;
            }

            if (string.IsNullOrEmpty(filter.Column))
            {
                Add(errors, $"{field}.column", "Give the column to filter by.");
                continue;
            }

            if (filter.Op is not { } name || !OPERATORS.TryGetValue(name, out var op))
            {
                Add(errors, $"{field}.op", $"Give an operator: {string.Join(", ", OPERATORS.Keys)}.");
                continue;
            }

            var values = filter.Values?.Select(Value).ToList();
            if (values is not null && values.Any(value => value is null))
            {
                Add(errors, $"{field}.values", "Each value must be text, a number, or true or false.");
                continue;
            }

            compiled.Add(new UrlFilter(filter.Column, op)
            {
                Value = filter.Value is { } single ? Value(single) : null,
                Values = values?.Select(value => value!.Value).ToList(),
                Min = filter.Min,
                Max = filter.Max,
                Group = filter.Group is { } group ? new IssueGroupKey(group.Detail, group.RelatedUrlId) : null
            });
        }

        return compiled;
    }

    /// <summary>
    /// The ends of some links a URL grid is narrowed to, its errors keyed under <c>linkEnds</c>; null when the request
    /// has none.
    /// </summary>
    public static UrlLinkEnds? LinkEnds(LinkEndsDto? ends, Dictionary<string, List<string>> errors)
    {
        if (ends is null)
        {
            return null;
        }

        Dictionary<string, List<string>> found = new(StringComparer.Ordinal);
        var filters = Filters(ends.Filters, found);
        var end = End(ends.End, found);
        foreach (var (field, messages) in found)
        {
            messages.ForEach(message => Add(errors, "linkEnds." + field, message));
        }

        return end is { } known ? new UrlLinkEnds(known, filters, ends.Search) : null;
    }

    /// <summary>Which end of a link, by its name; anything else is a field error under <c>end</c>.</summary>
    public static LinkEnd? End(string? end, Dictionary<string, List<string>> errors)
    {
        switch (end)
        {
            case "from":
                return LinkEnd.From;
            case "to":
                return LinkEnd.To;
            default:
                Add(errors, "end", "Give the end of the links: from (the pages they're on) or to (the URLs they lead to).");
                return null;
        }
    }

    public static IReadOnlyList<LinkKind> Kinds(IReadOnlyList<string>? kinds)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        List<LinkKind> parsed = [];
        foreach (var (name, index) in (kinds ?? []).Select((name, index) => (name, index)))
        {
            if (name is null || !KINDS.TryGetValue(name, out var kind))
            {
                Add(errors, $"kinds[{index}]", $"There's no kind of link called '{name}': there are {string.Join(", ", KINDS.Keys)}.");
            }
            else if (parsed.Contains(kind))
            {
                Add(errors, $"kinds[{index}]", $"'{name}' is named twice: name each kind of link once.");
            }
            else
            {
                parsed.Add(kind);
            }
        }

        ThrowIfAny(errors);
        return parsed;
    }

    /// <summary>The facets asked for; a null among them is a field error, as the engine's are for names it doesn't know.</summary>
    public static IReadOnlyList<string> Facets(IReadOnlyList<string>? facets, Dictionary<string, List<string>> errors)
    {
        foreach (var index in Nulls(facets))
        {
            Add(errors, $"facets[{index}]", "Give a facet's name.");
        }

        return facets ?? [];
    }

    /// <summary>
    /// A URL's id from a request: none is a field error; one no URL could have (0, below, or past what an id holds) is
    /// null, which is a URL the crawl doesn't have.
    /// </summary>
    public static int? UrlId(long? urlId) => urlId switch
    {
        null => throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["urlId"] = ["Give the URL's id."] }),
        < 1 or > int.MaxValue => null,
        { } id => (int)id
    };

    /// <summary>Where a list sent has nulls in it.</summary>
    private static IEnumerable<int> Nulls<T>(IReadOnlyList<T?>? items) where T : class =>
        (items ?? []).Select((item, index) => (item, index)).Where(entry => entry.item is null).Select(entry => entry.index);

    /// <summary>A filter as the facets give it back, for the UI to add to its query.</summary>
    public static UrlFilterDto Dto(UrlFilter filter) => new()
    {
        Column = filter.Column,
        Op = Camel(filter.Operator.ToString()),
        Value = filter.Value is { } value ? Element(value) : null,
        Values = filter.Values?.Select(Element).ToList(),
        Min = filter.Min,
        Max = filter.Max,
        Group = filter.Group is { } group ? new IssueGroupKeyDto { Detail = group.Detail, RelatedUrlId = group.RelatedUrlId } : null
    };

    /// <summary>A facet's value as JSON: text, a number, a boolean, or null.</summary>
    public static JsonElement Element(object? value) => value switch
    {
        null => NULL,
        string text => JsonSerializer.SerializeToElement(text, SidecarJsonContext.Default.String),
        bool flag => JsonSerializer.SerializeToElement(flag, SidecarJsonContext.Default.Boolean),
        double number => JsonSerializer.SerializeToElement(number, SidecarJsonContext.Default.Double),
        _ => JsonSerializer.SerializeToElement(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture), SidecarJsonContext.Default.Int64)
    };

    /// <summary>A groups request's order (evidence): <c>pages</c> or <c>size</c>, or null for the check's own.</summary>
    public static IssueGroupSort? GroupSort(string? sort) => sort switch
    {
        null => null,
        "pages" => IssueGroupSort.Pages,
        "size" => IssueGroupSort.Size,
        _ => throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["sort"] = ["Order the groups by \"pages\" or \"size\"."]
        })
    };

    /// <summary>The API's name for how a check's issues group.</summary>
    public static string Grouping(IssueGrouping grouping) => grouping switch
    {
        IssueGrouping.PageDetail or IssueGrouping.ResponseDetail => "detail",
        IssueGrouping.RelatedUrl => "relatedUrl",
        _ => "none"
    };

    public static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw Problems.InvalidQuery(errors.ToDictionary(error => error.Key, error => (IReadOnlyList<string>)error.Value, StringComparer.Ordinal));
        }
    }

    private static FilterValue? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => FilterValue.Of(element.GetString()!),
        JsonValueKind.Number => FilterValue.Of(element.GetDouble()),
        JsonValueKind.True => FilterValue.Of(true),
        JsonValueKind.False => FilterValue.Of(false),
        _ => null
    };

    private static JsonElement Element(FilterValue value) => Element(value.Text ?? (object?)value.Number ?? value.Boolean);

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    internal static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            errors[field] = messages = [];
        }

        messages.Add(message);
    }
}
