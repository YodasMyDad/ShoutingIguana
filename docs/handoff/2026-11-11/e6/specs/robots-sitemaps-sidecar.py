S = 'src/ShoutingIguana.Sidecar/'
A = S + 'Api/Models/'

WRITES = [
(A + 'RobotsOriginsRequest.cs', '''using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of a crawl's origins with their robots.txt, narrowed by a text in their name and by whether they blocked anything.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RobotsOriginsRequest
{
    public int? Offset { get; init; }

    public int? Limit { get; init; }

    /// <summary>Only the origins whose name contains this text.</summary>
    public string? Search { get; init; }

    /// <summary>True for only the origins that blocked something, false for only those that blocked nothing.</summary>
    public bool? Blocked { get; init; }
}
'''),
]

EDITS = [
(A + 'RobotsOriginPageDto.cs',
'''/// <summary>One page of a crawl's origins with their robots.txt.</summary>
internal sealed record RobotsOriginPageDto(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOriginDto> Origins);
''',
'''/// <summary>One page of a crawl's origins with their robots.txt.</summary>
/// <param name="Matching">The origins the search and the filter match, which the page is of.</param>
/// <param name="Counts">The origins the search matches, by whether they blocked anything: what a filter bar shows.</param>
internal sealed record RobotsOriginPageDto(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOriginDto> Origins, long Matching,
    RobotsOriginCounts Counts);
'''),
(A + 'RobotsOriginPageDto.cs',
'''namespace ShoutingIguana.Sidecar.Api.Models;
''',
'''using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;
'''),
(A + 'RobotsFileDto.cs',
'''    IReadOnlyList<RobotsFileLine> Lines,
    RobotsUnmatched Unmatched);
''',
'''    IReadOnlyList<RobotsFileLine> Lines,
    RobotsUnmatched Unmatched,
    bool Respected);
'''),
(A + 'RobotsBlockedRequest.cs',
'''    /// <summary><c>rule</c>, <c>unreachable</c> or <c>unknown</c>.</summary>
    public string? Reason { get; init; }
''',
'''    /// <summary><c>rule</c>, <c>unreachable</c> or <c>unknown</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Only this URL: its row, with why it was refused, or none when the origin didn't block it.</summary>
    public long? UrlId { get; init; }
'''),
(A + 'IndexabilityTestDto.cs',
'''    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Meta { get; init; }
''',
'''    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RobotsBlockReason? Reason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rule { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Meta { get; init; }
'''),
(A + 'IndexabilityTestDto.cs',
'''        StatusCode = test.StatusCode, MetaRefresh = test.MetaRefresh, Unreachable = test.Unreachable, Meta = test.Meta, Header = test.Header,
        Canonical = test.Canonical, CanonicalUrl = test.CanonicalUrl
''',
'''        StatusCode = test.StatusCode, MetaRefresh = test.MetaRefresh, Unreachable = test.Unreachable, Origin = test.Origin, Reason = test.Reason,
        Line = test.Line, Rule = test.Rule, Meta = test.Meta, Header = test.Header, Canonical = test.Canonical, CanonicalUrl = test.CanonicalUrl
'''),
(A + 'UrlDetailDto.cs',
'''    SummaryState Summary,
    SourceStateDto Source);
''',
'''    SummaryState Summary,
    SourceStateDto Source,
    UrlSitemaps ListedIn);
'''),
(S + 'Crawls/GridData.cs',
'''            url.Content, url.Limits, url.Links, url.LinkTotals, url.Headers, url.StructuredData, url.Hreflang, url.Issues, url.NamedBy, url.Summary,
            new SourceStateDto(url.SourceStored));
''',
'''            url.Content, url.Limits, url.Links, url.LinkTotals, url.Headers, url.StructuredData, url.Hreflang, url.Issues, url.NamedBy, url.Summary,
            new SourceStateDto(url.SourceStored), url.ListedIn);
'''),
(S + 'Crawls/GridData.cs',
'''    public async Task<RobotsOriginPageDto> RobotsAsync(string id, GroupsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var page = await ReadAsync(token => RobotsReader.ReadOrigins(path, request?.Offset ?? 0, request?.Limit ?? UrlGridReader.DEFAULT_LIMIT, cache, token),
            cancellationToken).ConfigureAwait(false);
        return new RobotsOriginPageDto(page.Total, page.Offset, page.Respected, page.UserAgent, page.Blocked, [.. page.Origins.Select(RobotsOriginDto.From)]);
    }
''',
'''    public async Task<RobotsOriginPageDto> RobotsAsync(string id, RobotsOriginsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var query = new RobotsOriginQuery
        {
            Search = request?.Search, Blocked = request?.Blocked, Offset = request?.Offset ?? 0, Limit = request?.Limit ?? UrlGridReader.DEFAULT_LIMIT
        };
        var page = await ReadAsync(token => RobotsReader.ReadOrigins(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new RobotsOriginPageDto(page.Total, page.Offset, page.Respected, page.UserAgent, page.Blocked, [.. page.Origins.Select(RobotsOriginDto.From)], page.Matching,
            page.Counts);
    }
'''),
(S + 'Crawls/GridData.cs',
'''        return new RobotsFileDto(read.Origin, read.IsInternal, read.State, read.StatusCode, read.FetchedAt, read.Error, read.Bytes, read.Blocked, file.UserAgent, file.Lines,
            file.Unmatched);
''',
'''        return new RobotsFileDto(read.Origin, read.IsInternal, read.State, read.StatusCode, read.FetchedAt, read.Error, read.Bytes, read.Blocked, file.UserAgent, file.Lines,
            file.Unmatched, file.Respected);
'''),
(S + 'Crawls/GridData.cs',
'''        return await ReadAsync(token => RobotsReader.ReadBlocked(path, origin, request.Line, reason, request.Offset ?? 0, request.Limit ?? UrlGridReader.DEFAULT_LIMIT, cache, token),
                   cancellationToken).ConfigureAwait(false)
               ?? throw Problems.OriginNotFound();
''',
'''        // An id no URL could have is one the origin didn't block: no rows, as for any other URL it didn't.
        int? urlId = request.UrlId is { } asked ? asked is < 1 or > int.MaxValue ? 0 : (int)asked : null;
        return await ReadAsync(
                       token => RobotsReader.ReadBlocked(path, origin, request.Line, reason, request.Offset ?? 0, request.Limit ?? UrlGridReader.DEFAULT_LIMIT, cache, token, urlId),
                       cancellationToken).ConfigureAwait(false)
               ?? throw Problems.OriginNotFound();
'''),
(S + 'Api/GridEndpoints.cs',
'''            TypedResults.Json(await data.RobotsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GroupsRequest).ConfigureAwait(false),
''',
'''            TypedResults.Json(await data.RobotsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.RobotsOriginsRequest).ConfigureAwait(false),
'''),
(S + 'Serialization/SidecarJsonContext.cs',
'''[JsonSerializable(typeof(SearchRequest))]
''',
'''[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(RobotsOriginsRequest))]
'''),
]
