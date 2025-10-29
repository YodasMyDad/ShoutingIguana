using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using FrameworkReducer = NuGet.Frameworks.FrameworkReducer;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of IDependencyResolver that recursively resolves package dependencies.
/// </summary>
public class DependencyResolver(
    ILogger<DependencyResolver> logger,
    IFeedConfigurationService feedService,
    IDependencyCache dependencyCache) : IDependencyResolver
{
    private readonly ILogger<DependencyResolver> _logger = logger;
    private readonly IFeedConfigurationService _feedService = feedService;
    private readonly IDependencyCache _dependencyCache = dependencyCache;

    public async Task<DependencyResolutionResult> ResolveDependenciesAsync(
        string packageId,
        string version,
        NuGetFramework targetFramework,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var resolvedDependencies = new Dictionary<string, DependencyInfo>(StringComparer.OrdinalIgnoreCase);
        var visitedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            _logger.LogInformation("Resolving dependencies for {PackageId} v{Version} (target: {Framework})",
                packageId, version, targetFramework.GetShortFolderName());

            var success = await ResolveDependenciesRecursiveAsync(
                packageId,
                version,
                targetFramework,
                depth: 0,
                isTransitive: false,
                resolvedDependencies,
                visitedPackages,
                warnings,
                cancellationToken);

            if (!success)
            {
                return new DependencyResolutionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to resolve one or more dependencies",
                    Warnings = warnings
                };
            }

            _logger.LogInformation("Resolved {Count} dependencies for {PackageId}",
                resolvedDependencies.Count, packageId);

            return new DependencyResolutionResult
            {
                Success = true,
                Dependencies = resolvedDependencies.Values.ToList(),
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving dependencies for {PackageId}", packageId);
            return new DependencyResolutionResult
            {
                Success = false,
                ErrorMessage = $"Dependency resolution failed: {ex.Message}",
                Warnings = warnings
            };
        }
    }

    private async Task<bool> ResolveDependenciesRecursiveAsync(
        string packageId,
        string version,
        NuGetFramework targetFramework,
        int depth,
        bool isTransitive,
        Dictionary<string, DependencyInfo> resolvedDependencies,
        HashSet<string> visitedPackages,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        // Check for circular dependency
        var packageKey = $"{packageId}:{version}";
        if (visitedPackages.Contains(packageKey))
        {
            warnings.Add($"Circular dependency detected: {packageId} v{version}");
            return true; // Continue but don't re-process
        }

        visitedPackages.Add(packageKey);

        // Check depth limit
        if (depth > 50) // Safety limit
        {
            warnings.Add($"Maximum dependency depth exceeded at {packageId} v{version}");
            return false;
        }

        try
        {
            var feeds = await _feedService.GetFeedsAsync();
            
            foreach (var feed in feeds.Where(f => f.Enabled))
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(feed.Url);
                    var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

                    var packageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
                    
                    using var sourceCacheContext = new SourceCacheContext();
                    var metadata = await metadataResource.GetMetadataAsync(
                        packageId,
                        includePrerelease: true,
                        includeUnlisted: false,
                        sourceCacheContext,
                        new NuGetLogger(_logger),
                        cancellationToken);

                    var packageMetadata = metadata.FirstOrDefault(m => m.Identity.Version.ToString() == version);
                    if (packageMetadata == null)
                    {
                        continue; // Try next feed
                    }

                    // Get dependency groups for this package
                    var dependencyGroups = packageMetadata.DependencySets;
                    
                    // Find the most compatible dependency group for our target framework
                    var reducer = new FrameworkReducer();
                    var compatibleGroup = dependencyGroups
                        .Where(g => reducer.GetNearest(targetFramework, new[] { g.TargetFramework }) != null)
                        .OrderByDescending(g => g.TargetFramework.Version)
                        .FirstOrDefault();

                    if (compatibleGroup == null && dependencyGroups.Any())
                    {
                        // Try to find any dependency group if no exact match
                        compatibleGroup = dependencyGroups
                            .OrderByDescending(g => g.TargetFramework.Version)
                            .FirstOrDefault();
                        
                        if (compatibleGroup != null)
                        {
                            warnings.Add($"No exact framework match for {packageId} v{version}, using {compatibleGroup.TargetFramework.GetShortFolderName()}");
                        }
                    }

                    if (compatibleGroup != null && compatibleGroup.Packages.Any())
                    {
                        foreach (var dependency in compatibleGroup.Packages)
                        {
                            // Skip framework assemblies
                            if (IsFrameworkAssembly(dependency.Id))
                            {
                                _logger.LogDebug("Skipping framework assembly: {PackageId}", dependency.Id);
                                continue;
                            }

                            // Resolve version from version range
                            var resolvedVersion = await ResolveVersionFromRangeAsync(
                                dependency.Id,
                                dependency.VersionRange,
                                feed.Url,
                                cancellationToken);

                            if (resolvedVersion == null)
                            {
                                warnings.Add($"Could not resolve version for {dependency.Id} with range {dependency.VersionRange}");
                                continue;
                            }

                            // Check if the host application already has this assembly loaded
                            var systemVersion = new Version(resolvedVersion.Major, resolvedVersion.Minor, resolvedVersion.Patch);
                            if (_dependencyCache.IsAssemblyLoadedInHost(dependency.Id, systemVersion))
                            {
                                _logger.LogInformation("Skipping {PackageId} v{Version} - already loaded in host application", 
                                    dependency.Id, resolvedVersion);
                                continue;
                            }

                            var depKey = dependency.Id.ToLowerInvariant();
                            
                            // If already resolved, check if we need a newer version
                            if (resolvedDependencies.ContainsKey(depKey))
                            {
                                var existing = resolvedDependencies[depKey];
                                var existingVersion = NuGetVersion.Parse(existing.Version);
                                
                                if (resolvedVersion > existingVersion)
                                {
                                    _logger.LogDebug("Upgrading {PackageId} from {OldVersion} to {NewVersion}",
                                        dependency.Id, existingVersion.ToString(), resolvedVersion.ToString());
                                    
                                    // Update to newer version
                                    resolvedDependencies[depKey] = new DependencyInfo
                                    {
                                        PackageId = dependency.Id,
                                        Version = resolvedVersion.ToString(),
                                        VersionRange = dependency.VersionRange.ToString(),
                                        TargetFramework = compatibleGroup.TargetFramework.GetShortFolderName(),
                                        IsTransitive = true,
                                        Depth = depth + 1,
                                        PackageSize = 0 // Will be filled in later
                                    };
                                }
                                continue; // Don't recursively process again
                            }

                            // Add this dependency
                            resolvedDependencies[depKey] = new DependencyInfo
                            {
                                PackageId = dependency.Id,
                                Version = resolvedVersion.ToString(),
                                VersionRange = dependency.VersionRange.ToString(),
                                TargetFramework = compatibleGroup.TargetFramework.GetShortFolderName(),
                                IsTransitive = isTransitive || depth > 0,
                                Depth = depth + 1,
                                PackageSize = 0 // Will be filled in later
                            };

                            // Recursively resolve this dependency's dependencies
                            await ResolveDependenciesRecursiveAsync(
                                dependency.Id,
                                resolvedVersion.ToString(),
                                targetFramework,
                                depth + 1,
                                true,
                                resolvedDependencies,
                                visitedPackages,
                                warnings,
                                cancellationToken);
                        }
                    }

                    return true; // Successfully processed
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve dependencies from feed {FeedUrl}", feed.Url);
                }
            }

            warnings.Add($"Could not resolve dependencies for {packageId} v{version} from any feed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving dependencies for {PackageId} v{Version}", packageId, version);
            return false;
        }
    }

    private async Task<NuGetVersion?> ResolveVersionFromRangeAsync(
        string packageId,
        VersionRange versionRange,
        string feedUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var repository = Repository.Factory.GetCoreV3(feedUrl);
            var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

            using var sourceCacheContext = new SourceCacheContext();
            var allVersions = await metadataResource.GetMetadataAsync(
                packageId,
                includePrerelease: versionRange.IsMinInclusive && versionRange.MinVersion?.IsPrerelease == true,
                includeUnlisted: false,
                sourceCacheContext,
                new NuGetLogger(_logger),
                cancellationToken);

            var compatibleVersions = allVersions
                .Select(m => m.Identity.Version)
                .Where(v => versionRange.Satisfies(v))
                .OrderByDescending(v => v)
                .ToList();

            return versionRange.FindBestMatch(compatibleVersions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve version for {PackageId} with range {VersionRange}",
                packageId, versionRange);
            return null;
        }
    }

    private static bool IsFrameworkAssembly(string packageId)
    {
        // List of common framework packages that shouldn't be downloaded as dependencies
        var frameworkPrefixes = new[]
        {
            "System.",
            "Microsoft.NETCore.",
            "Microsoft.Win32.",
            "Microsoft.CSharp",
            "netstandard.library",
            "NETStandard.Library"
        };

        return frameworkPrefixes.Any(prefix =>
            packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

