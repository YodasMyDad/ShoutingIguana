using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Custom AssemblyLoadContext for hot-loading plugins.
/// Each plugin gets its own isolated context that can be unloaded.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly ILogger? _logger;
    private readonly string _pluginPath;

    public PluginLoadContext(string pluginPath, ILogger? logger = null) 
        : base(isCollectible: true)
    {
        _pluginPath = pluginPath;
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _logger = logger;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Check if this is a shared assembly (SDK types, framework types)
        if (IsSharedAssembly(assemblyName))
        {
            // Load from default context (already loaded in host)
            _logger?.LogDebug("Loading shared assembly from default context: {AssemblyName}", assemblyName.Name);
            return null; // Return null to load from default context
        }

        // Try to resolve from plugin directory
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null && File.Exists(assemblyPath))
        {
            _logger?.LogDebug("Loading plugin assembly from: {Path}", assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Fall back to default resolution
        _logger?.LogDebug("Falling back to default resolution for: {AssemblyName}", assemblyName.Name);
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Determines if an assembly should be shared from the host rather than loaded per-plugin.
    /// </summary>
    private static bool IsSharedAssembly(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;

        // Share SDK types (must be same instance across all plugins)
        if (name.StartsWith("ShoutingIguana.PluginSdk", StringComparison.OrdinalIgnoreCase))
            return true;

        // Share framework assemblies
        if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
            return true;

        // Share common libraries
        if (name.StartsWith("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public string PluginPath => _pluginPath;
}

