namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One crawl in the library.</summary>
internal sealed record CrawlListItem
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>The start URL's host without a leading www., which the UI groups crawls by.</summary>
    public string? Site { get; init; }

    public string? StartUrl { get; init; }
    public required CrawlListStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>URLs fetched, whatever the answer; live, the crawl's progress.</summary>
    public long UrlCount { get; init; }

    /// <summary>Internal HTML pages fetched successfully.</summary>
    public long PageCount { get; init; }

    /// <summary>Null before the first analysis.</summary>
    public IssueCountsDto? Issues { get; init; }

    public DateTime? AnalyzedAt { get; init; }

    /// <summary>The crawl changed after its last analysis.</summary>
    public bool OutOfDate { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>A file opened from elsewhere that isn't there any more.</summary>
    public bool Missing { get; init; }
}
