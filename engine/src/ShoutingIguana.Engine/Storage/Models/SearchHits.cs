namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a search of <see cref="SearchText"/> found: the best few, best first, and how many URLs match in all.</summary>
internal sealed record SearchHits(long Matching, IReadOnlyList<SearchHit> Best);
