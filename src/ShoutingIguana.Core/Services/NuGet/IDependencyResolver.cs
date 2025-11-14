using NuGet.Frameworks;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for resolving NuGet package dependencies.
/// </summary>
public interface IDependencyResolver
{
    /// <summary>
    /// Resolves all dependencies for a package, including transitive dependencies.
    /// </summary>
    /// <param name="packageId">The package ID to resolve dependencies for.</param>
    /// <param name="version">The package version.</param>
    /// <param name="targetFramework">The target framework (e.g., net10.0, netstandard2.1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A flattened list of all dependencies.</returns>
    Task<DependencyResolutionResult> ResolveDependenciesAsync(
        string packageId,
        string version,
        NuGetFramework targetFramework,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of dependency resolution.
/// </summary>
public class DependencyResolutionResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<DependencyInfo> Dependencies { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool HasCircularDependency { get; init; }
}

