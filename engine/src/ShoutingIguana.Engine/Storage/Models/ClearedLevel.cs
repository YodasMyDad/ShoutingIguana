namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What <see cref="CrawlComparisonReader.ReadCleared"/> lists: each URL, or each cause with no URL now.</summary>
public enum ClearedLevel
{
    Rows,
    Groups
}
