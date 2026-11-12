namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One URL a search of <see cref="SearchText"/> found: its place there, how well it matches, and where.</summary>
internal readonly record struct SearchHit(int Ordinal, UrlSearchRank Rank, UrlSearchField Field);
