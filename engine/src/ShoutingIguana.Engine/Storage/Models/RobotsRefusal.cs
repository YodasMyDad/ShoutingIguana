namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Why robots.txt refused a URL, as a value of the URL grid's <c>robotsRule</c> column: the origin whose file decided it,
/// and the line of that file that did, as the inspector's robots.txt test says it.
/// </summary>
/// <param name="Origin">The origin, as <c>/robots</c> names it, to open its file at <paramref name="Line"/>.</param>
/// <param name="Line">The line of the stored file that refused it, for <see cref="RobotsBlockReason.Rule"/>.</param>
/// <param name="Rule">That line's text: "Disallow: /cart/".</param>
public sealed record RobotsRefusal(string? Origin, RobotsBlockReason Reason, int? Line, string? Rule);
