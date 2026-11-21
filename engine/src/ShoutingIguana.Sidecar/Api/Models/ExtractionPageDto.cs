using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Which rows of a crawl's extracted values to read. Left out, the first page of every rule.</summary>
internal sealed record ExtractionRequest
{
    public string? Rule { get; init; }
    public string? Search { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
}

/// <summary>One rule, and how many pages it matched or failed on.</summary>
internal sealed record ExtractionRuleCountDto
{
    public string Name { get; init; } = "";
    public ExtractionSelector SelectorType { get; init; }
    public string Selector { get; init; } = "";
    public bool Enabled { get; init; }
    public long Matches { get; init; }
    public long Errors { get; init; }
}

/// <summary>One rule's result on one page.</summary>
internal sealed record ExtractionRowDto
{
    public string Url { get; init; } = "";
    public string Name { get; init; } = "";
    public ExtractionSelector SelectorType { get; init; }
    public string Selector { get; init; } = "";
    public string? Value { get; init; }
    public int Count { get; init; }
    public string? Error { get; init; }
}

/// <summary>The crawl's rules, and one page of what they found.</summary>
internal sealed record ExtractionPageDto
{
    public IReadOnlyList<ExtractionRuleCountDto> Rules { get; init; } = [];
    public long Total { get; init; }
    public IReadOnlyList<ExtractionRowDto> Rows { get; init; } = [];
}
