namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// A site's score, 0 to 100, as <see cref="ScoreCalculator"/> works it out when a crawl is analysed, with all it takes to
/// show how: the analysis stores it, so every reader gives the same number.
/// </summary>
/// <param name="Version">The <see cref="ScoreCalculator.VERSION"/> that worked it out.</param>
/// <param name="Value">The score: 100 less <paramref name="Lost"/>, no lower than 0, and no higher than the ceiling while an error check has issues.</param>
/// <param name="Lost">The points every line takes off, added up. It can pass 100.</param>
/// <param name="BeforeCeiling">The score before the ceiling: 100 less <paramref name="Lost"/>, no lower than 0.</param>
/// <param name="HeldBySerious">The ceiling is what holds the score: it would be <paramref name="BeforeCeiling"/> without an error check's issues standing.</param>
/// <param name="Partial">The crawl didn't read all of the site, so the score is of what it read so far (<see cref="ScoreCoverage.Complete"/> is false).</param>
/// <param name="Lines">Every check that found something, in fix-first order (<see cref="FixFirstOrder"/>), a notice at 0 points.</param>
public sealed record SiteScore(
    int Version,
    int Value,
    ScoreBand Band,
    int Lost,
    int BeforeCeiling,
    bool HeldBySerious,
    bool Partial,
    ScoreCoverage Covers,
    IReadOnlyList<ScoreLine> Lines,
    ScoreIfFixed IfFixed);
