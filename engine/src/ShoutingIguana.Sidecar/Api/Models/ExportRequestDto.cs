using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// What to export, and where to: <c>POST /api/crawls/{id}/export</c>. A grid's export is the grid's own query (its
/// columns, filters, search and sort) with the kind, the format and the path; the other fields belong to one kind each,
/// and one given to a kind that doesn't take it is refused, not ignored.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ExportRequestDto
{
    /// <summary><c>urls</c>, <c>links</c>, <c>sitemapUrls</c>, <c>robotsBlocked</c>, <c>issueRows</c>, <c>check</c>, <c>workbook</c> or <c>report</c>.</summary>
    public string? Kind { get; init; }

    /// <summary><c>csv</c>, <c>xlsx</c> or <c>html</c>.</summary>
    public string? Format { get; init; }

    /// <summary>The file to write: a full path on this computer, ending in the format's extension.</summary>
    public string? Path { get; init; }

    public IReadOnlyList<string>? Columns { get; init; }

    public IReadOnlyList<UrlFilterDto>? Filters { get; init; }

    public string? Search { get; init; }

    public IReadOnlyList<UrlSortDto>? Sort { get; init; }

    /// <summary>For <c>issueRows</c>: one of the check's groups.</summary>
    public IssueGroupKeyDto? Group { get; init; }

    /// <summary>For <c>sitemapUrls</c>.</summary>
    public long? SitemapId { get; init; }

    /// <summary>For <c>robotsBlocked</c>.</summary>
    public string? Origin { get; init; }

    public int? Line { get; init; }

    /// <summary><c>rule</c>, <c>unreachable</c> or <c>unknown</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>For <c>issueRows</c> and <c>check</c>.</summary>
    public string? CheckId { get; init; }

    /// <summary>For <c>report</c>: <c>full</c> or <c>summary</c>.</summary>
    public string? Cut { get; init; }

    public int? Examples { get; init; }

    public int? LookExamples { get; init; }
}
