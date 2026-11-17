using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The score then and now, each as its analysis stored it. A partial one on either side is a change "so far".</summary>
/// <param name="Change">Now less then; null when the two were worked out by different versions of the score.</param>
/// <param name="Lines">Every check that took points on either side: in this crawl's order, then those only the other crawl's score has.</param>
public sealed record ScoreChange(SiteScoreSummary Then, SiteScoreSummary Now, int? Change, IReadOnlyList<ScoreLineChange> Lines);
