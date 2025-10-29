using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of IDependencyCache that tracks dependencies across plugins.
/// </summary>
public class DependencyCache : IDependencyCache
{
    private readonly ILogger<DependencyCache> _logger;
    private readonly ConcurrentDictionary<string, string> _dependencyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<AssemblyInfo>> _hostAssemblies;

    public DependencyCache(ILogger<DependencyCache> logger)
    {
        _logger = logger;
        _hostAssemblies = new Lazy<IReadOnlyList<AssemblyInfo>>(LoadHostAssemblies);
    }

    public string? GetExistingDependencyPath(string packageId, string version)
    {
        var key = GetKey(packageId, version);
        
        if (_dependencyPaths.TryGetValue(key, out var path) && Directory.Exists(path))
        {
            _logger.LogDebug("Found existing dependency: {PackageId} v{Version} at {Path}", 
                packageId, version, path);
            return path;
        }

        return null;
    }

    public void RegisterDependency(string packageId, string version, string path)
    {
        var key = GetKey(packageId, version);
        _dependencyPaths[key] = path;
        _logger.LogDebug("Registered dependency: {PackageId} v{Version} at {Path}", 
            packageId, version, path);
    }

    public bool IsAssemblyLoadedInHost(string assemblyName, Version? version = null)
    {
        var hostAssemblies = _hostAssemblies.Value;
        
        if (version == null)
        {
            return hostAssemblies.Any(a => a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
        }

        return hostAssemblies.Any(a => 
            a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) && 
            a.Version >= version);
    }

    public IReadOnlyList<AssemblyInfo> GetHostAssemblies()
    {
        return _hostAssemblies.Value;
    }

    private static string GetKey(string packageId, string version)
    {
        return $"{packageId}:{version}";
    }

    private IReadOnlyList<AssemblyInfo> LoadHostAssemblies()
    {
        try
        {
            var assemblies = new List<AssemblyInfo>();
            
            // Get all assemblies loaded in the default context
            var loadedAssemblies = AssemblyLoadContext.Default.Assemblies;
            
            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    var assemblyName = assembly.GetName();
                    if (assemblyName.Name != null && assemblyName.Version != null)
                    {
                        assemblies.Add(new AssemblyInfo
                        {
                            Name = assemblyName.Name,
                            Version = assemblyName.Version,
                            Location = assembly.Location
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get info for assembly: {Assembly}", assembly.FullName);
                }
            }

            _logger.LogInformation("Loaded {Count} host assemblies for dependency cache", assemblies.Count);
            return assemblies.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load host assemblies");
            return Array.Empty<AssemblyInfo>();
        }
    }
}

