using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One check compared between two crawls by its counts alone (<see cref="SummaryComparison"/>).</summary>
/// <param name="Then">The URLs it flagged in the other crawl; null when it didn't run then.</param>
/// <param name="Now">The URLs it flags in this crawl; null when it didn't run now.</param>
public sealed record CheckCountChange(string CheckId, CheckChangeState State, long? Then, long? Now);
