namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for interacting with NuGet feeds.
/// </summary>
public interface INuGetService
{
    /// <summary>
    /// Search for packages in the configured feeds.
    /// </summary>
    Task<IReadOnlyList<PackageSearchResult>> SearchPackagesAsync(
        string searchTerm, 
        string? tagFilter = null, 
        bool includePrerelease = false,
        int skip = 0, 
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed metadata for a specific package.
    /// </summary>
    Task<PackageSearchResult?> GetPackageMetadataAsync(
        string packageId, 
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a package to the specified path.
    /// </summary>
    Task<string> DownloadPackageAsync(
        string packageId, 
        string version, 
        string targetDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate that a package contains a valid plugin.
    /// </summary>
    Task<ValidationResult> ValidatePackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a package and all its dependencies.
    /// </summary>
    Task<DownloadWithDependenciesResult> DownloadPackageWithDependenciesAsync(
        string packageId,
        string version,
        string targetDirectory,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of downloading a package with dependencies.
/// </summary>
public class DownloadWithDependenciesResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MainPackagePath { get; init; }
    public IReadOnlyList<string> DependencyPackagePaths { get; init; } = [];
    public IReadOnlyList<DependencyInfo> ResolvedDependencies { get; init; } = [];
}

