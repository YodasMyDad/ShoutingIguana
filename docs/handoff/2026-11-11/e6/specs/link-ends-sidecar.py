S = 'src/ShoutingIguana.Sidecar/'
A = S + 'Api/Models/'

WRITES = [
(A + 'LinkEndsDto.cs', '''using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A URL grid narrowed to one end of some links: the pages the links a set of link filters matches are on
/// (<c>from</c>), or the URLs they lead to (<c>to</c>).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinkEndsDto
{
    /// <summary><c>from</c> or <c>to</c>.</summary>
    public string? End { get; init; }

    /// <summary>The links grid's filters; none for every link.</summary>
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    /// <summary>The links grid's search.</summary>
    public string? Search { get; init; }
}
'''),
(A + 'LinkEndsRequest.cs', '''using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of the URLs at one end of the links a links view matches: the view's filters and search, and which end.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LinkEndsRequest
{
    /// <summary><c>from</c> or <c>to</c>.</summary>
    public string? End { get; init; }

    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    public string? Search { get; init; }

    public int? Offset { get; init; }

    public int? Limit { get; init; }
}
'''),
]

EDITS = [
(A + 'GridQueryRequest.cs',
'''    /// <summary>For a sitemap's URLs (E3b): the sitemap, by its id.</summary>
    public long? SitemapId { get; init; }
''',
'''    /// <summary>For a sitemap's URLs (E3b): the sitemap, by its id.</summary>
    public long? SitemapId { get; init; }

    /// <summary>For the URL grids (E6): only the URLs at one end of the links these link filters match.</summary>
    public LinkEndsDto? LinkEnds { get; init; }
'''),
(A + 'FacetsRequest.cs',
'''    /// <summary>The facets to count; all of them when left out.</summary>
    public IReadOnlyList<string>? Facets { get; init; }
''',
'''    /// <summary>The facets to count; all of them when left out.</summary>
    public IReadOnlyList<string>? Facets { get; init; }

    /// <summary>For the URL grid's facets (E6): only the URLs at one end of the links these link filters match.</summary>
    public LinkEndsDto? LinkEnds { get; init; }
'''),
(S + 'Api/GridRequests.cs',
'''        ThrowIfAny(errors);
        return new UrlQuery
        {
            Columns = request.Columns ?? [],
            Filters = filters,
            Search = request.Search,
''',
'''        var ends = LinkEnds(request.LinkEnds, errors);
        ThrowIfAny(errors);
        return new UrlQuery
        {
            Columns = request.Columns ?? [],
            Filters = filters,
            Search = request.Search,
            LinkEnds = ends,
'''),
(S + 'Api/GridRequests.cs',
'''    public static IReadOnlyList<LinkKind> Kinds(IReadOnlyList<string>? kinds)
''',
'''    /// <summary>
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
'''),
(S + 'Crawls/GridData.cs',
'''        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked };
        return Facets(await ReadAsync(token => UrlGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
''',
'''        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        var ends = GridRequests.LinkEnds(request?.LinkEnds, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked, LinkEnds = ends };
        return Facets(await ReadAsync(token => UrlGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
'''),
(S + 'Crawls/GridData.cs',
'''            throw Problems.BadRequest("Only a check's rows take a group: filter by either end's check, with its group, instead.");
        }

        RefuseSitemap(request);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var page = await ReadAsync(token => LinkGridReader.Query(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new LinkGridPageDto(page.Columns, new LinkRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable);
    }
''',
'''            throw Problems.BadRequest("Only a check's rows take a group: filter by either end's check, with its group, instead.");
        }

        RefuseSitemap(request);
        RefuseLinkEnds(request?.LinkEnds);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var page = await ReadAsync(token => LinkGridReader.Query(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new LinkGridPageDto(page.Columns, new LinkRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable);
    }

    /// <summary>
    /// The URLs at one end of the links a links view matches, a page of them: the view's targets, or the pages its links
    /// are on, each once, however many links the view holds.
    /// </summary>
    public async Task<LinkEndPage> LinkEndsAsync(string id, LinkEndsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var end = GridRequests.End(request?.End, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new LinkEndQuery
        {
            End = end!.Value, Filters = filters, Search = request?.Search, Offset = request?.Offset ?? 0, Limit = request?.Limit ?? UrlGridReader.DEFAULT_LIMIT
        };
        return await ReadAsync(token => LinkGridReader.Ends(path, query, cache, token), cancellationToken).ConfigureAwait(false);
    }
'''),
(S + 'Crawls/GridData.cs',
'''        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked };
        return Facets(await ReadAsync(token => LinkGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
''',
'''        RefuseLinkEnds(request?.LinkEnds);
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked };
        return Facets(await ReadAsync(token => LinkGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
'''),
(S + 'Crawls/GridData.cs',
'''    /// <summary>A sitemap's id belongs to the sitemap's own grid.</summary>
''',
'''    /// <summary>The ends of some links narrow the URL grids: the links grid is narrowed by its own filters.</summary>
    private static void RefuseLinkEnds(LinkEndsDto? ends)
    {
        if (ends is not null)
        {
            throw Problems.BadRequest("Only the URL grids take linkEnds: the links grid takes the link filters themselves.");
        }
    }

    /// <summary>A sitemap's id belongs to the sitemap's own grid.</summary>
'''),
(S + 'Api/GridEndpoints.cs',
'''        api.MapGet("/api/crawls/{id}/sitemaps", async''',
'''        api.MapPost("/api/crawls/{id}/links/ends", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinkEndsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.LinkEndsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkEndPage));

        api.MapGet("/api/crawls/{id}/sitemaps", async'''),
(S + 'Serialization/SidecarJsonContext.cs',
'''        typeof(CamelCaseEnumConverter<UrlSearchField>)
''',
'''        typeof(CamelCaseEnumConverter<UrlSearchField>),
        typeof(CamelCaseEnumConverter<LinkEnd>)
'''),
(S + 'Serialization/SidecarJsonContext.cs',
'''[JsonSerializable(typeof(RobotsOriginsRequest))]
''',
'''[JsonSerializable(typeof(RobotsOriginsRequest))]
[JsonSerializable(typeof(LinkEndsRequest))]
[JsonSerializable(typeof(LinkEndPage))]
'''),
]
