using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging;
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
public class NuGetService(ILogger<NuGetService> logger, IFeedConfigurationService feedService) : INuGetService
{
    private readonly ILogger<NuGetService> _logger = logger;
    private readonly IFeedConfigurationService _feedService = feedService;

    public async Task<IReadOnlyList<PackageSearchResult>> SearchPackagesAsync(
        string searchTerm,
        string? tagFilter = null,
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

                    var searchFilter = new SearchFilter(includePrerelease: false);
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

                    var metadata = await metadataResource.GetMetadataAsync(
                        packageId,
                        includePrerelease: false,
                        includeUnlisted: false,
                        new SourceCacheContext(),
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
            // Extract package to temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Open the package
                using var packageReader = new PackageArchiveReader(packagePath);

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
                                    ErrorMessage = "Plugin class missing [Plugin] attribute"
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
                                        ErrorMessage = "Failed to create plugin instance"
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
                                            MinSdkVersion = pluginAttr.MinSdkVersion
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
                                            MinSdkVersion = pluginAttr.MinSdkVersion
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
                    ErrorMessage = "Package does not contain a valid Shouting Iguana plugin"
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

