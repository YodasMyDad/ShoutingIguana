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

    /// <summary>Why the file couldn't be read, in words for the UI: given only when the status is unreadable.</summary>
    public string? Error { get; init; }

    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>URLs with a stored outcome, as <c>si stats</c> and a crawl's progress count them; live, the crawl's progress.</summary>
    public long FetchedUrlCount { get; init; }

    /// <summary>Internal HTML pages fetched and parsed.</summary>
    public long PageCount { get; init; }

    /// <summary>Null before the first analysis.</summary>
    public IssueCountsDto? Issues { get; init; }

    public DateTime? AnalyzedAt { get; init; }

    /// <summary>
    /// The last analysis no longer stands: the crawl changed after it, this engine would judge it differently, or its
    /// settings differ. False before the first analysis.
    /// </summary>
    public bool OutOfDate { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>A file opened from elsewhere that isn't there any more.</summary>
    public bool Missing { get; init; }
}
