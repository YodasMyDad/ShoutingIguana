namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>
/// A section its host limits apart that the crawl gave up on while URLs in it were still waiting their turn, which were
/// stored as never asked: for the notice at the end of the crawl to say why.
/// </summary>
/// <param name="Host">The section's host, named as <c>CrawlScope.HostName</c> names it.</param>
/// <param name="Section">The section: "/products/".</param>
/// <param name="TimeRanOut">
/// Given up on as the time for the held-back sections ran out, rather than for refusing every page at its slowest pace.
/// </param>
internal sealed record SectionGivenUp(string Host, string Section, bool TimeRanOut);
