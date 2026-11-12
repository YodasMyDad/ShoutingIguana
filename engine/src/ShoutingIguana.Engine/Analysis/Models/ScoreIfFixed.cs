namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What the score would be with the first problems to fix gone: "Fixing these five takes the score from 85 to 98".</summary>
/// <param name="CheckIds">
/// The first <see cref="ScoreCalculator.FIX_FIRST"/> checks in fix-first order (<see cref="FixFirstOrder"/>) that take
/// points off: errors and warnings. Fewer when fewer found anything, and none on a site with nothing to fix.
/// </param>
/// <param name="Value">The score without them.</param>
public sealed record ScoreIfFixed(IReadOnlyList<string> CheckIds, int Value);
