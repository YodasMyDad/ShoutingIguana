namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Another check a fix of one check clears, and on how many of its pages.</summary>
/// <param name="CheckId">The check cleared.</param>
/// <param name="Pages">
/// The pages it flagged whose every target is a URL the fixed check flagged: the pages the two checks share, as a fix clears
/// them. A page that also links to another broken URL isn't counted.
/// </param>
/// <param name="All">Every page the check flagged is among them: the fix clears the check entirely.</param>
public sealed record ClearedCheck(string CheckId, long Pages, bool All);
