namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One URL in a recheck's record, as it stood before and as it is now.</summary>
/// <param name="Outcome">For a judged URL once settled, what the recheck found; null for the other roles, and until settled.</param>
/// <param name="FlaggedBefore">The check flagged it before the recheck.</param>
/// <param name="DetailAfter">The check's detail on it now, or null when it doesn't flag it.</param>
/// <param name="StatusCode">Its status now, or null without one.</param>
public sealed record RecheckRow(
    int UrlId,
    string Url,
    RecheckRole Role,
    RecheckOutcome? Outcome,
    bool FlaggedBefore,
    string? DetailBefore,
    string? DetailAfter,
    int? StatusCode);
