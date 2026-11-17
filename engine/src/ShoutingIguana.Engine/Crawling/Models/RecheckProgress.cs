namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>How far a recheck has got: its phase (1, the judged URLs and what they name; 2, the pages referring to those still failing) and its fetches.</summary>
/// <param name="Done">The fetches it has stored, every phase's and every one step's.</param>
/// <param name="Total">The fetches it has planned so far, which grows as pages lead to URLs the crawl doesn't have.</param>
public sealed record RecheckProgress(int Phase, int Done, int Total);
