using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// The grids' reads of a crawl file, through the engine's readers: the URL grid, its facets and lookup, the inspector
/// with its links and source, and a check's groups and rows. Counts are kept in one cache for every crawl, keyed so a
/// crawl continued or analysed again counts afresh.
/// </summary>
internal sealed class GridData(CrawlManager crawls, ReadCache cache)
{
    /// <summary>The columns for the column chooser and the filter bar, a check's own last.</summary>
    public static IReadOnlyList<UrlColumnDto> Columns { get; } =
    [
        .. UrlGridReader.IssueRowColumns.Select(column => new UrlColumnDto(
            column.Id,
            column.Type.ToString().ToLowerInvariant(),
            column.NeedsAnalysis,
            column.Sortable,
            column.Filterable,
            column.Values,
            UrlGridReader.Columns.All(other => other.Id != column.Id)))
    ];

    public UrlPageDto Query(string id, GridQueryRequest? request)
    {
        if (request?.Group is not null)
        {
            throw Problems.BadRequest("Only a check's rows take a group: filter by the check, with its group, instead.");
        }

        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        return Page(Read(() => UrlGridReader.Query(path, query, cache)));
    }

    public FacetsDto Facets(string id, FacetsRequest? request)
    {
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        GridRequests.ThrowIfAny(errors);
        var facets = Read(() => UrlGridReader.Facets(path, new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = request?.Facets ?? [] }, cache));
        return new FacetsDto(facets.Matching, facets.Total, facets.Summary, facets.Unavailable,
            facets.Facets.ToDictionary(facet => facet.Key, facet => (IReadOnlyList<FacetBucketDto>)
                [.. facet.Value.Select(bucket => new FacetBucketDto(GridRequests.Element(bucket.Value), bucket.Count, GridRequests.Dto(bucket.Filter)) { Min = bucket.Min, Max = bucket.Max })],
                StringComparer.Ordinal));
    }

    public UrlRef Lookup(string id, UrlLookupRequest request)
    {
        var path = crawls.PathOf(id);
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["url"] = ["Give a URL, or a path that starts with /."] });
        }

        return Read(() => UrlGridReader.Lookup(path, request.Url)) ?? throw Problems.UrlNotFound();
    }

    public UrlDetailDto Url(string id, int urlId)
    {
        var path = crawls.PathOf(id);
        var url = Read(() => UrlInspectorReader.Read(path, urlId, cache)) ?? throw Problems.UrlNotFound();
        var indexability = url.Indexability;
        return new UrlDetailDto(
            url.Id, url.Url, url.IsInternal, url.Type, url.Discovery,
            url.Response is { } response ? UrlResponseDto.From(response) : null,
            url.Redirects,
            new IndexabilityDto(indexability.Indexable, indexability.Reason, indexability.JudgedBy, [.. indexability.Tests.Select(IndexabilityTestDto.From)]),
            url.Content, url.Limits, url.Links, url.Headers, url.StructuredData, url.Hreflang, url.Issues, url.NamedBy, url.Summary,
            new SourceStateDto(url.SourceStored));
    }

    public LinkPageDto Links(string id, int urlId, LinkDirection direction, LinksRequest? request)
    {
        var path = crawls.PathOf(id);
        var query = new LinkQuery
        {
            Kinds = GridRequests.Kinds(request?.Kinds),
            Offset = request?.Offset ?? 0,
            Limit = request?.Limit ?? UrlGridReader.DEFAULT_LIMIT,
            After = request?.After
        };
        var page = Read(() => UrlInspectorReader.ReadLinks(path, urlId, direction, query, cache)) ?? throw Problems.UrlNotFound();
        return new LinkPageDto([.. page.Rows.Select(LinkRowDto.From)], page.Offset, page.Total, page.Next);
    }

    public HtmlSourceDto Source(string id, int urlId)
    {
        var path = crawls.PathOf(id);
        var source = Read(() => UrlInspectorReader.ReadSource(path, urlId)) ?? throw Problems.UrlNotFound();
        return source.Stored
            ? new HtmlSourceDto(true) { Html = source.Html, Bytes = source.Bytes, Truncated = source.Truncated }
            : new HtmlSourceDto(false) { Reason = source.Missing == HtmlSourceMissing.NotStored ? "notStored" : "notPage" };
    }

    public IssueGroupPageDto Groups(string id, string checkId, GroupsRequest? request)
    {
        var path = crawls.PathOf(id);
        var page = Read(() => IssueGroupReader.ReadGroups(path, checkId, request?.Offset ?? 0, request?.Limit ?? UrlGridReader.DEFAULT_LIMIT, cache))
                   ?? throw Problems.NotAnalyzed();
        if (page.Total is null && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        return new IssueGroupPageDto(page.CheckId, GridRequests.Grouping(page.Grouping), page.Total, page.GroupCount, page.Offset, page.Groups);
    }

    public UrlPageDto Rows(string id, string checkId, GridQueryRequest? request)
    {
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var group = request?.Group is { } key ? new IssueGroupKey(key.Detail, key.RelatedUrlId) : null;
        var rows = Read(() => IssueGroupReader.ReadRows(path, checkId, query, group, cache)) ?? throw Problems.NotAnalyzed();
        if (rows.Page is { } page)
        {
            return Page(page);
        }

        // Skipped: no rows and no counts, never 0.
        return IssueCatalog.Find(checkId) is null
            ? throw Problems.CheckNotFound(checkId)
            : new UrlPageDto(["id"], new UrlRowsDto(["id"], []), query.Offset, null, null, null, rows.Summary, []);
    }

    private static UrlPageDto Page(UrlPage page) =>
        new(page.Columns, new UrlRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable);

    /// <summary>A read, its refusals as the problems the UI keys on.</summary>
    private static T Read<T>(Func<T> read)
    {
        try
        {
            return EngineErrors.Read(read);
        }
        catch (UrlQueryException exception)
        {
            throw Problems.InvalidQuery(exception.Errors);
        }
        catch (NeedsAnalysisException exception)
        {
            throw Problems.NeedsAnalysis(exception.Reason, exception.Columns);
        }
    }
}
