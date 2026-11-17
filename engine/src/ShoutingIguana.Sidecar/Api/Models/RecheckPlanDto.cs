using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// What a recheck would do (<c>POST …/rechecks/plan</c>), or, with <see cref="RecheckId"/>, the one just started
/// (<c>POST …/rechecks</c>): the engine's plan, the secrets it would ask for, and why it can't run, if it can't.
/// </summary>
/// <param name="CountBefore">The check's count in the analysis as it stands; null without a check.</param>
/// <param name="Refused">Null, or why it can't run now.</param>
internal sealed record RecheckPlanDto(
    string? CheckId,
    IssueGroupKey? Group,
    bool UrlList,
    IReadOnlyList<RecheckKind> Kinds,
    int Judged,
    int Phase1,
    int Phase2Max,
    int Limit,
    bool Capped,
    int NotRechecked,
    long EstimateMs,
    bool SuggestFullCheck,
    bool AnalysisFirst,
    long? CountBefore,
    RecheckSecretsDto Secrets,
    RecheckRefusalDto? Refused)
{
    /// <summary>The recheck started: its number in the crawl file. Left out of a plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(-1)]
    public int? RecheckId { get; init; }

    public static RecheckPlanDto From(RecheckPlan plan, RecheckSecretsDto secrets, RecheckRefusalDto? refused) => new(
        plan.CheckId, plan.Group, plan.UrlList, plan.Kinds, plan.Judged, plan.Phase1, plan.Phase2Max, plan.Limit, plan.Capped, plan.NotRechecked, plan.EstimateMs,
        plan.SuggestFullCheck, plan.AnalysisFirst, plan.CountBefore, secrets, refused);
}
