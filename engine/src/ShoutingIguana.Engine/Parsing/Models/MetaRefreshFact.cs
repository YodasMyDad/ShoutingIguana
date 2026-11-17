namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="FromHttpHeader">
/// The refresh came from a <c>Refresh</c> response header, which browsers follow as they do the meta tag; false for a
/// <c>&lt;meta http-equiv="refresh"&gt;</c>, and for every page crawled before the crawler read the header.
/// </param>
public sealed record MetaRefreshFact(int? DelaySeconds, string? Url, bool FromHttpHeader = false);
