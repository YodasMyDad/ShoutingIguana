namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>A score's headline, for a list of crawls: the number, its word, and whether it is of a crawl that didn't read all of the site.</summary>
public sealed record SiteScoreSummary(int Value, ScoreBand Band, bool Partial);
