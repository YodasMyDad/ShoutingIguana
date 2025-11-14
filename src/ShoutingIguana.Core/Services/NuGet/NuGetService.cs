using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ShoutingIguana.PluginSdk;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NuGetLogLevel = NuGet.Common.LogLevel;
using NuGetILogger = NuGet.Common.ILogger;
using NuGetILogMessage = NuGet.Common.ILogMessage;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of INuGetService using NuGet.Protocol.
/// </summary>
public class NuGetService : INuGetService
{
    private readonly ILogger<NuGetService> _logger;
    private readonly IFeedConfigurationService _feedService;
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IPackageSecurityService _securityService;

    public NuGetService(
        ILogger<NuGetService> logger,
        IFeedConfigurationService feedService,
        IDependencyResolver dependencyResolver,
        IPackageSecurityService securityService)
    {
        _logger = logger;
        _feedService = feedService;
        _dependencyResolver = dependencyResolver;
        _securityService = securityService;
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchPackagesAsync(
        string searchTerm,
        string? tagFilter = null,
        bool includePrerelease = false,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PackageSearchResult>();

        try
        {
            var feeds = await _feedService.GetFeedsAsync();
            
            foreach (var feed in feeds)
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(feed.Url);
                    var searchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);

                    var searchFilter = new SearchFilter(includePrerelease: includePrerelease);
                    var searchResults = await searchResource.SearchAsync(
                        searchTerm,
                        searchFilter,
                        skip,
                        take,
                        new NuGetLogger(_logger),
                        cancellationToken);

                    foreach (var result in searchResults)
                    {
                        // Filter by tag if specified
                        if (tagFilter != null)
                        {
                            var tags = result.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (!tags.Any(t => t.Equals(tagFilter, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                        }

                        results.Add(new PackageSearchResult
                        {
                            Id = result.Identity.Id,
                            Version = result.Identity.Version.ToString(),
                            Description = result.Description,
                            Authors = result.Authors,
                            DownloadCount = result.DownloadCount ?? 0,
                            IconUrl = result.IconUrl?.ToString(),
                            ProjectUrl = result.ProjectUrl?.ToString(),
                            Tags = result.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to search feed: {FeedUrl}", feed.Url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching packages");
        }

        return results;
    }

    public async Task<PackageSearchResult?> GetPackageMetadataAsync(
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var feeds = await _feedService.GetFeedsAsync();

            foreach (var feed in feeds)
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(feed.Url);
                    var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

                    using var sourceCacheContext = new SourceCacheContext();
                    var metadata = await metadataResource.GetMetadataAsync(
                        packageId,
                        includePrerelease: false,
                        includeUnlisted: false,
                        sourceCacheContext,
                        new NuGetLogger(_logger),
                        cancellationToken);

                    var package = version == null
                        ? metadata.OrderByDescending(m => m.Identity.Version).FirstOrDefault()
                        : metadata.FirstOrDefault(m => m.Identity.Version.ToString() == version);

                    if (package != null)
                    {
                        return new PackageSearchResult
                        {
                            Id = package.Identity.Id,
                            Version = package.Identity.Version.ToString(),
                            Description = package.Description,
                            Authors = package.Authors,
                            DownloadCount = package.DownloadCount ?? 0,
                            IconUrl = package.IconUrl?.ToString(),
                            ProjectUrl = package.ProjectUrl?.ToString(),
                            Tags = package.Tags?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? []
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get metadata from feed: {FeedUrl}", feed.Url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting package metadata");
        }

        return null;
    }

    public async Task<string> DownloadPackageAsync(
        string packageId,
        string version,
        string targetDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var feeds = await _feedService.GetFeedsAsync();
            Directory.CreateDirectory(targetDirectory);

            foreach (var feed in feeds)
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(feed.Url);
                    var downloadResource = await repository.GetResourceAsync<DownloadResource>(cancellationToken);

                    var packageIdentity = new global::NuGet.Packaging.Core.PackageIdentity(
                        packageId,
                        NuGetVersion.Parse(version));

                    using var sourceCacheContext = new SourceCacheContext();
                    var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                        packageIdentity,
                        new PackageDownloadContext(sourceCacheContext),
                        Path.GetTempPath(),
                        new NuGetLogger(_logger),
                        cancellationToken);

                    if (downloadResult.Status == DownloadResourceResultStatus.Available)
                    {
                        var packageFile = Path.Combine(targetDirectory, $"{packageId}.{version}.nupkg");

                        using (var packageStream = downloadResult.PackageStream)
                        using (var fileStream = File.Create(packageFile))
                        {
                            await packageStream.CopyToAsync(fileStream, cancellationToken);
                        }

                        progress?.Report(100);

                        _logger.LogInformation("Downloaded package: {PackageId} v{Version}", packageId, version);
                        return packageFile;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download from feed: {FeedUrl}", feed.Url);
                }
            }

            throw new InvalidOperationException($"Failed to download package {packageId} v{version} from any feed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading package");
            throw;
        }
    }

    public async Task<ValidationResult> ValidatePackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating package: {PackagePath}", packagePath);

            // Validate package size first
            if (!await _securityService.ValidatePackageSizeAsync(packagePath, cancellationToken))
            {
                return new ValidationResult
                {
                    Result = PackageValidationResult.InvalidPackage,
                    ErrorMessage = "Package size exceeds security limits",
                    SecurityWarnings = ["Package file is too large"]
                };
            }

            // Extract package to temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Open the package
                using var packageReader = new PackageArchiveReader(packagePath);
                var packageIdentity = await packageReader.GetIdentityAsync(cancellationToken);
                var packageId = packageIdentity.Id;
                var packageVersion = packageIdentity.Version.ToString();

                // Extract lib folder
                var libItems = packageReader.GetLibItems().ToList();
                if (!libItems.Any())
                {
                    return new ValidationResult
                    {
                        Result = PackageValidationResult.InvalidPackage,
                        ErrorMessage = "Package contains no library files"
                    };
                }

                // Resolve dependencies
                _logger.LogDebug("Resolving dependencies for {PackageId} v{Version}", packageId, packageVersion);
                var targetFramework = NuGetFramework.Parse("net10.0");
                var dependencyResolution = await _dependencyResolver.ResolveDependenciesAsync(
                    packageId,
                    packageVersion,
                    targetFramework,
                    cancellationToken);

                var dependencies = dependencyResolution.Dependencies.ToList();
                var warnings = new List<string>(dependencyResolution.Warnings);

                // Get feed URL from first feed (for security validation)
                var feeds = await _feedService.GetFeedsAsync();
                var feedUrl = feeds.FirstOrDefault(f => f.Enabled)?.Url ?? "https://api.nuget.org/v3/index.json";

                // Perform security validation
                var securityResult = await _securityService.ValidatePackageSecurityAsync(
                    packageId,
                    packageVersion,
                    dependencies,
                    feedUrl,
                    cancellationToken);

                if (!securityResult.IsValid)
                {
                    return new ValidationResult
                    {
                        Result = PackageValidationResult.InvalidPackage,
                        ErrorMessage = securityResult.BlockReason ?? "Package failed security validation",
                        SecurityWarnings = securityResult.Errors.Concat(securityResult.Warnings).ToList(),
                        Dependencies = dependencies,
                        TotalDownloadSize = CalculateTotalSize(packagePath, dependencies)
                    };
                }

                warnings.AddRange(securityResult.Warnings);

                // Extract files
                var files = await packageReader.GetFilesAsync(cancellationToken);
                foreach (var file in files)
                {
                    if (file.StartsWith("lib/"))
                    {
                        var targetPath = Path.Combine(tempDir, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                        // Extract file using stream
                        using var sourceStream = packageReader.GetStream(file);
                        using var targetStream = File.Create(targetPath);
                        await sourceStream.CopyToAsync(targetStream, cancellationToken);
                    }
                }

                // Scan for IPlugin implementations using a temporary collectible ALC
                var dllFiles = Directory.GetFiles(tempDir, "*.dll", SearchOption.AllDirectories);
                
                foreach (var dllFile in dllFiles)
                {
                    ValidationResult? result = null;
                    
                    // Use a collectible ALC for inspection to avoid memory leaks
                    var inspectionContext = new PluginLoadContext(dllFile, _logger);
                    WeakReference weakRef = new(inspectionContext, trackResurrection: true);
                    
                    try
                    {
                        // Load assembly into temporary context
                        var assembly = inspectionContext.LoadFromAssemblyPath(dllFile);

                        var pluginTypes = assembly.GetTypes()
                            .Where(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t))
                            .ToList();

                        if (pluginTypes.Any())
                        {
                            var pluginType = pluginTypes.First();
                            var pluginAttr = pluginType.GetCustomAttribute<PluginAttribute>();

                            if (pluginAttr == null)
                            {
                                result = new ValidationResult
                                {
                                    Result = PackageValidationResult.NoPlugin,
                                    ErrorMessage = "Plugin class missing [Plugin] attribute",
                                    Dependencies = dependencies,
                                    TotalDownloadSize = CalculateTotalSize(packagePath, dependencies),
                                    SecurityWarnings = warnings
                                };
                            }
                            else
                            {
                                // Create instance to get ID
                                var plugin = Activator.CreateInstance(pluginType) as IPlugin;
                                if (plugin == null)
                                {
                                    result = new ValidationResult
                                    {
                                        Result = PackageValidationResult.InvalidPackage,
                                        ErrorMessage = "Failed to create plugin instance",
                                        Dependencies = dependencies,
                                        TotalDownloadSize = CalculateTotalSize(packagePath, dependencies),
                                        SecurityWarnings = warnings
                                    };
                                }
                                else
                                {
                                    // Check SDK version
                                    var minVersion = Version.Parse(pluginAttr.MinSdkVersion);
                                    var currentVersion = new Version(1, 0, 0); // Current SDK version

                                    if (currentVersion < minVersion)
                                    {
                                        result = new ValidationResult
                                        {
                                            Result = PackageValidationResult.IncompatibleSdk,
                                            ErrorMessage = $"Plugin requires SDK version {pluginAttr.MinSdkVersion}, but current version is {currentVersion}",
                                            MinSdkVersion = pluginAttr.MinSdkVersion,
                                            Dependencies = dependencies,
                                            TotalDownloadSize = CalculateTotalSize(packagePath, dependencies),
                                            SecurityWarnings = warnings
                                        };
                                    }
                                    else
                                    {
                                        result = new ValidationResult
                                        {
                                            Result = PackageValidationResult.Valid,
                                            PluginId = plugin.Id,
                                            PluginName = plugin.Name,
                                            PluginVersion = plugin.Version.ToString(),
                                            MinSdkVersion = pluginAttr.MinSdkVersion,
                                            Dependencies = dependencies,
                                            TotalDownloadSize = CalculateTotalSize(packagePath, dependencies),
                                            SecurityWarnings = warnings.Any() ? warnings : null
                                        };
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to inspect assembly: {DllFile}", dllFile);
                    }
                    finally
                    {
                        // Unload the inspection context to prevent memory leak
                        inspectionContext.Unload();
                        
                        // Force GC to clean up
                        for (int i = 0; i < 3 && weakRef.IsAlive; i++)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }
                    
                    if (result != null)
                    {
                        return result;
                    }
                }

                return new ValidationResult
                {
                    Result = PackageValidationResult.NoPlugin,
                    ErrorMessage = "Package does not contain a valid Shouting Iguana plugin",
                    Dependencies = dependencies,
                    TotalDownloadSize = CalculateTotalSize(packagePath, dependencies),
                    SecurityWarnings = warnings
                };
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating package");
            return new ValidationResult
            {
                Result = PackageValidationResult.InvalidPackage,
                ErrorMessage = $"Validation error: {ex.Message}"
            };
        }
    }

    private long CalculateTotalSize(string mainPackagePath, IReadOnlyList<DependencyInfo> dependencies)
    {
        long totalSize = 0;
        
        try
        {
            var mainFileInfo = new FileInfo(mainPackagePath);
            if (mainFileInfo.Exists)
            {
                totalSize += mainFileInfo.Length;
            }
        }
        catch
        {
            // Ignore errors
        }

        totalSize += dependencies.Sum(d => d.PackageSize);
        return totalSize;
    }

    public async Task<DownloadWithDependenciesResult> DownloadPackageWithDependenciesAsync(
        string packageId,
        string version,
        string targetDirectory,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading {PackageId} v{Version} with dependencies", packageId, version);
            
            progress?.Report(new InstallProgress { Status = "Resolving dependencies...", PercentComplete = 5 });

            // Determine target framework - prefer net10.0, fall back to netstandard2.1
            var targetFramework = NuGetFramework.Parse("net10.0");
            
            // Resolve all dependencies
            var resolutionResult = await _dependencyResolver.ResolveDependenciesAsync(
                packageId,
                version,
                targetFramework,
                cancellationToken);

            if (!resolutionResult.Success)
            {
                return new DownloadWithDependenciesResult
                {
                    Success = false,
                    ErrorMessage = resolutionResult.ErrorMessage ?? "Failed to resolve dependencies"
                };
            }

            var dependencies = resolutionResult.Dependencies.ToList();
            _logger.LogInformation("Resolved {Count} dependencies for {PackageId}", dependencies.Count, packageId);

            progress?.Report(new InstallProgress { Status = "Downloading packages...", PercentComplete = 20 });

            // Download main package
            var mainPackagePath = await DownloadPackageAsync(
                packageId,
                version,
                targetDirectory,
                null,
                cancellationToken);

            var dependencyPaths = new List<string> { mainPackagePath };
            var totalPackages = dependencies.Count + 1;
            var downloadedCount = 1;

            // Download each dependency
            foreach (var dep in dependencies)
            {
                try
                {
                    var percentComplete = 20 + (int)((downloadedCount / (double)totalPackages) * 70);
                    progress?.Report(new InstallProgress
                    {
                        Status = $"Downloading {dep.PackageId} v{dep.Version}...",
                        PercentComplete = percentComplete
                    });

                    var depPath = await DownloadPackageAsync(
                        dep.PackageId,
                        dep.Version,
                        targetDirectory,
                        null,
                        cancellationToken);

                    dependencyPaths.Add(depPath);
                    downloadedCount++;

                    // Update package size in dependency info
                    var fileInfo = new FileInfo(depPath);
                    if (fileInfo.Exists)
                    {
                        var depIndex = dependencies.FindIndex(d => 
                            d.PackageId.Equals(dep.PackageId, StringComparison.OrdinalIgnoreCase));
                        if (depIndex >= 0)
                        {
                            dependencies[depIndex] = new DependencyInfo
                            {
                                PackageId = dep.PackageId,
                                Version = dep.Version,
                                VersionRange = dep.VersionRange,
                                TargetFramework = dep.TargetFramework,
                                IsTransitive = dep.IsTransitive,
                                Depth = dep.Depth,
                                PackageSize = fileInfo.Length
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download dependency {PackageId} v{Version}", 
                        dep.PackageId, dep.Version);
                    // Continue with other dependencies rather than failing completely
                }
            }

            progress?.Report(new InstallProgress { Status = "Download complete", PercentComplete = 100 });

            return new DownloadWithDependenciesResult
            {
                Success = true,
                MainPackagePath = mainPackagePath,
                DependencyPackagePaths = dependencyPaths.Skip(1).ToList(),
                ResolvedDependencies = dependencies
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading package with dependencies: {PackageId}", packageId);
            return new DownloadWithDependenciesResult
            {
                Success = false,
                ErrorMessage = $"Download failed: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Adapter to use Microsoft.Extensions.Logging with NuGet.
/// </summary>
internal class NuGetLogger(ILogger logger) : NuGetILogger
{
    private readonly ILogger _logger = logger;

    public void LogDebug(string data) => _logger.LogDebug("{Message}", data);
    public void LogVerbose(string data) => _logger.LogTrace("{Message}", data);
    public void LogInformation(string data) => _logger.LogInformation("{Message}", data);
    public void LogMinimal(string data) => _logger.LogInformation("{Message}", data);
    public void LogWarning(string data) => _logger.LogWarning("{Message}", data);
    public void LogError(string data) => _logger.LogError("{Message}", data);
    public void LogInformationSummary(string data) => _logger.LogInformation("{Message}", data);

    public void Log(NuGetLogLevel level, string data)
    {
        switch (level)
        {
            case NuGetLogLevel.Debug:
                LogDebug(data);
                break;
            case NuGetLogLevel.Verbose:
                LogVerbose(data);
                break;
            case NuGetLogLevel.Information:
                LogInformation(data);
                break;
            case NuGetLogLevel.Minimal:
                LogMinimal(data);
                break;
            case NuGetLogLevel.Warning:
                LogWarning(data);
                break;
            case NuGetLogLevel.Error:
                LogError(data);
                break;
        }
    }

    public Task LogAsync(NuGetLogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public void Log(NuGetILogMessage message) => Log(message.Level, message.Message);
    public Task LogAsync(NuGetILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}

