namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a recheck found, as the analysis after it settled it: kept as found, never worked out again.</summary>
/// <param name="Fixed">Judged URLs the check no longer flags (read again, or let go as no longer linked).</param>
/// <param name="Still">Judged URLs the check still flags.</param>
/// <param name="NotChecked">Judged URLs it couldn't judge.</param>
/// <param name="NotRechecked">Judged URLs past the cap or the stop.</param>
/// <param name="New">URLs in its scope the check didn't flag before and flags now.</param>
public sealed record RecheckResult(int Fixed, int Still, int NotChecked, int NotRechecked, int New, int? ScoreBefore, int? ScoreAfter);
