using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>An image file a page shows, with how the page shows it and what the file is.</summary>
/// <param name="Links">How many times the page shows it: each <c>img</c>, <c>srcset</c> candidate or other image link to it.</param>
/// <param name="Text">The alt text of the first of them that has one; null when none has.</param>
/// <param name="Positions">Where on the page they sit, in the positions' order.</param>
/// <param name="Target">
/// What the file is, as a check's related URL reads (<see cref="IssueGroup.Related"/>): its size and pixels after any
/// redirects, which the large-image check judges it by.
/// </param>
public sealed record PageImage(int Id, string Url, long Links, string? Text, IReadOnlyList<LinkPosition> Positions, UrlFacts Target);
