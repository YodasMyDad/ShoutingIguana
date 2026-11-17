using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What a recheck would do, worked out without doing it (<see cref="RecheckPlanReader"/>): the URLs it judges, what it
/// fetches first and next, whether it passes its cap, how long it should take, and whether it can run at all.
/// </summary>
/// <param name="Kinds">What it reads again: the check's <see cref="IssueDefinition.Recheck"/>, or the URLs alone for a list.</param>
/// <param name="Judged">The URLs whose result it gives: the check's flagged URLs, the group's, or the list's, past the cap too.</param>
/// <param name="Phase1">What it fetches first: the judged URLs within the cap, with every URL a targets check's issues name.</param>
/// <param name="Phase2Max">For a check of what others refer to, the most it fetches next: the pages referring to the judged URLs still failing.</param>
/// <param name="Limit">The cap on <paramref name="Phase1"/> and <paramref name="Phase2Max"/> together.</param>
/// <param name="Capped">The judged URLs and what they need pass the cap: the most important are taken first.</param>
/// <param name="NotRechecked">The judged URLs past the cap, left as the crawl found them.</param>
/// <param name="EstimateMs">How long it should take: the fetches at the crawl's own pace, and the analysis after it.</param>
/// <param name="SuggestFullCheck">It would fetch more than half the crawl's URLs: checking the whole site takes about as long, and settles every check.</param>
/// <param name="AnalysisFirst">The crawl's analysis is out of date, so it's analysed first, and the "before" is today's.</param>
/// <param name="CountBefore">The check's count in the analysis as it stands, or null without a check.</param>
/// <param name="Refused">Why it can't run, or null when it can.</param>
public sealed record RecheckPlan(
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
    RecheckRefusal? Refused)
{
    /// <summary>The URLs it fetches or may, with what it records of each as it stood: the judged and named first, the referring after.</summary>
    internal IReadOnlyList<RecheckScopeUrl> Scope { get; init; } = [];

    /// <summary>For a robots.txt recheck, the origins whose robots.txt it reads again.</summary>
    internal IReadOnlyList<string> RobotsOrigins { get; init; } = [];
}
