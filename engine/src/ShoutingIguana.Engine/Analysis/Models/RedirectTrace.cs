namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Where a redirect ends up after following every hop the crawl recorded.</summary>
/// <param name="FinalId">The first URL that is not a redirect, or where the trace stopped.</param>
/// <param name="Hops">Redirects followed, so 1 is a single redirect and 2 or more is a chain.</param>
/// <param name="IsLoop">The trace came back to a URL it had already visited.</param>
internal readonly record struct RedirectTrace(int FinalId, int Hops, bool IsLoop);
