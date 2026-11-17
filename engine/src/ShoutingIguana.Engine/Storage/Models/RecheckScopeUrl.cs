namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One URL a recheck fetches, or may, as the crawl has it, with what the recheck records of it as it stood before.</summary>
/// <param name="Phase">1, fetched first; 2, fetched next only if the URL it refers to still fails.</param>
/// <param name="FlaggedBefore">The check flagged it before the recheck.</param>
/// <param name="Targets">The URLs the check's issue on it named (<c>issue_targets</c>), before.</param>
/// <param name="Refers">For a referring page, the judged URL it refers to.</param>
internal sealed record RecheckScopeUrl(
    int Id,
    string Url,
    bool IsInternal,
    int Depth,
    DiscoverySource Source,
    RecheckRole Role,
    int Phase,
    bool FlaggedBefore,
    string? DetailBefore,
    int? RelatedBefore,
    IReadOnlyList<int> Targets,
    int? Refers);
