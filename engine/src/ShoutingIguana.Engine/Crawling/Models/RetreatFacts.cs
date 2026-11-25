namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>What the crawl did by itself about a host that started refusing it, for the <c>blocked</c> alert to say.</summary>
/// <param name="Retried">The host's refused URLs it asked again, more slowly.</param>
/// <param name="Slowest">The longest it waited between the host's pages.</param>
internal sealed record RetreatFacts(int Retried, TimeSpan Slowest);
