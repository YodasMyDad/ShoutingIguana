namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="FromHttpHeader">
/// The refresh came from a <c>Refresh</c> response header, which browsers follow as they do the meta tag; false for a
/// <c>&lt;meta http-equiv="refresh"&gt;</c>, and for every page crawled before the crawler read the header.
/// </param>
/// <param name="FromScript">
/// No tag or header says it: the page's script sent the browser on when the page was rendered. Kept as an instant
/// refresh, because that is what it does to a visitor and to a search engine that renders the page.
/// </param>
public sealed record MetaRefreshFact(int? DelaySeconds, string? Url, bool FromHttpHeader = false, bool FromScript = false);
