using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A target of the links a view matches, with what they are.</summary>
/// <param name="Links">The view's links to it.</param>
/// <param name="Pages">The pages those links are on, each once.</param>
/// <param name="Text">
/// The text the most of its links have (an anchor, an image's alt text), among the first
/// <see cref="LinkGroupPage.TEXT_SAMPLE"/> in crawl order, the first found on a tie; null when none of those has any.
/// </param>
/// <param name="Texts">How many texts those links have between them, none counted as one.</param>
/// <param name="Positions">Where on their pages its links sit, in the positions' order; a position this engine has no name for is left out.</param>
/// <param name="LikelyTemplate">
/// It's linked from the template the view's pages share: from at least <see cref="IssueGroupReader.TEMPLATE_MIN_PAGES"/>
/// pages, and at least half the pages the view's links are on, as a check's group is called one.
/// </param>
/// <param name="Target">What the target is, as a check's related URL reads (<see cref="IssueGroup.Related"/>).</param>
public sealed record LinkGroupRow(int Id, string Url, long Links, long Pages, string? Text, int Texts, IReadOnlyList<LinkPosition> Positions, bool LikelyTemplate,
    UrlFacts Target);
