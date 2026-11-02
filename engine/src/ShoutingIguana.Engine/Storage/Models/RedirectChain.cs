namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Where a URL's redirects lead, hop by hop, an instant meta refresh counting as one, as the analysis follows them.
/// </summary>
/// <param name="Hops">Each URL reached in turn, at most <see cref="Analysis.UrlTable.MAX_REDIRECT_HOPS"/>.</param>
/// <param name="Final">Where they end: the URL itself when it doesn't redirect.</param>
/// <param name="Loop">They come back on themselves.</param>
/// <param name="TooLong">There are more hops than browsers follow, so the trace stops at a redirect.</param>
public sealed record RedirectChain(IReadOnlyList<RedirectHop> Hops, UrlTarget Final, bool Loop, bool TooLong);
