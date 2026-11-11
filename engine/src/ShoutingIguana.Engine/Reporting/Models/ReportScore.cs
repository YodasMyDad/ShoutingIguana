namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// A crawl's score as the report prints it: the number and its word, what took points off, what it covers, and what it
/// would be with the first problems fixed. The report reads the score here and nowhere else, so where it comes from (the
/// analysis that stored it) can change without the document changing.
/// </summary>
/// <param name="Version">The formula's version, printed in the breakdown and the foot.</param>
/// <param name="Lost">The points taken off in all.</param>
/// <param name="BeforeCeiling">100 less the points lost, before a serious problem holds it to the ceiling.</param>
/// <param name="HeldBySerious">The ceiling is what holds the score: it would be <paramref name="BeforeCeiling"/> otherwise.</param>
/// <param name="Ceiling">The most a score can be while a serious problem stands.</param>
/// <param name="Partial">The crawl didn't finish: the score is of what was read, "so far".</param>
/// <param name="Lines">A line for each check that found something, things worth a look among them at 0 points.</param>
/// <param name="FixedValue">The score with the first problems of "Fix first" order fixed (up to five), or null when there are none to fix.</param>
/// <param name="FixedChecks">Those problems' checks.</param>
internal sealed record ReportScore(
    int Version,
    int Value,
    ScoreBand Band,
    int Lost,
    int BeforeCeiling,
    bool HeldBySerious,
    int Ceiling,
    bool Partial,
    ScoreCovers Covers,
    IReadOnlyList<ScoreLine> Lines,
    int? FixedValue,
    IReadOnlyList<string> FixedChecks);
