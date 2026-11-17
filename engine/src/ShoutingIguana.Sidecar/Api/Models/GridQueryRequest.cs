using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A grid query, for <c>/urls</c>, a check's <c>/rows</c>, <c>/links</c> and a sitemap's <c>/sitemaps/urls</c>: the columns, filters, search and sort, and where the page
/// starts. Every field may be left out.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GridQueryRequest
{
    public IReadOnlyList<string>? Columns { get; init; }
    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }
    public string? Search { get; init; }
    public IReadOnlyList<UrlSortDto>? Sort { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public string? After { get; init; }

    /// <summary>For a check's rows: one of its groups, as <c>/groups</c> gave it.</summary>
    public IssueGroupKeyDto? Group { get; init; }

    /// <summary>For a sitemap's URLs (E3b): the sitemap, by its id.</summary>
    public long? SitemapId { get; init; }

    /// <summary>For the URL grids (E6): only the URLs at one end of the links these link filters match.</summary>
    public LinkEndsDto? LinkEnds { get; init; }

    /// <summary>
    /// The checks the user hid, by id (round 5 review): the issue counts (<c>issues</c>, <c>errors</c>, <c>warnings</c>,
    /// <c>notices</c>) leave their issues out wherever they're read, sorted or filtered, at either end of a link too.
    /// </summary>
    public IReadOnlyList<string>? HiddenChecks { get; init; }

    /// <summary>For a check's rows (the fix loop): <c>previous</c> or a crawl's id, to give each row its change since that crawl.</summary>
    public string? CompareWith { get; init; }
}
