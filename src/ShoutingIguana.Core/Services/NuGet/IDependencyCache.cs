using System.Reflection;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for tracking and sharing dependencies across plugins.
/// </summary>
public interface IDependencyCache
{
    /// <summary>
    /// Checks if a dependency is already available (from host or another plugin).
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="version">The package version.</param>
    /// <returns>Path to existing dependency, or null if not found.</returns>
    string? GetExistingDependencyPath(string packageId, string version);

    /// <summary>
    /// Registers a downloaded dependency so other plugins can reuse it.
    /// </summary>
    void RegisterDependency(string packageId, string version, string path);

    /// <summary>
    /// Checks if an assembly is already loaded in the default context.
    /// </summary>
    bool IsAssemblyLoadedInHost(string assemblyName, Version? version = null);

    /// <summary>
    /// Gets all assemblies loaded in the default context.
    /// </summary>
    IReadOnlyList<AssemblyInfo> GetHostAssemblies();
}

/// <summary>
/// Information about a loaded assembly.
/// </summary>
public class AssemblyInfo
{
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public required string Location { get; init; }
}

