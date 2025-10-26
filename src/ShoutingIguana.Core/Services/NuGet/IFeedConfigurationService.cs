namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for managing NuGet feed configurations.
/// </summary>
public interface IFeedConfigurationService : IDisposable
{
    /// <summary>
    /// Get all configured feeds.
    /// </summary>
    Task<IReadOnlyList<NuGetFeed>> GetFeedsAsync();

    /// <summary>
    /// Add a new feed.
    /// </summary>
    Task AddFeedAsync(NuGetFeed feed);

    /// <summary>
    /// Remove a feed by name.
    /// </summary>
    Task RemoveFeedAsync(string name);

    /// <summary>
    /// Update an existing feed.
    /// </summary>
    Task UpdateFeedAsync(NuGetFeed feed);
}

