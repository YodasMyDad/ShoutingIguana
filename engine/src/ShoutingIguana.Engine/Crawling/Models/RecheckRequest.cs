using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>A recheck to run on a crawl file: its plan (<see cref="Storage.RecheckPlanReader"/>) and the options to fetch with.</summary>
/// <param name="Options">
/// The crawl's stored options with the secrets it doesn't keep given again (<see cref="CrawlOptions.PlanResume"/>), so a
/// recheck asks the site as the crawl did.
/// </param>
public sealed record RecheckRequest(string DatabasePath, RecheckPlan Plan, CrawlOptions Options)
{
    /// <summary>Tests only: called at each <see cref="RecheckStage"/> of the run, to make it fail there.</summary>
    internal Action<RecheckStage>? AtStage { get; init; }
}
