using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Metadata for tracking loaded plugins with their contexts.
/// </summary>
internal class PluginMetadata
{
    public required IPlugin Plugin { get; init; }
    public required PluginLoadContext LoadContext { get; init; }
    public required WeakReference WeakRef { get; init; }
    public required string SourcePath { get; init; }
    public List<IUrlTask> Tasks { get; init; } = [];
    public List<IExportProvider> Exporters { get; init; } = [];
    public DateTime UnloadedAt { get; set; }
}

/// <summary>
/// Implementation of IPluginRegistry for discovering and managing plugins with hot-loading support.
/// </summary>
public class PluginRegistry(ILogger<PluginRegistry> logger, ILoggerFactory loggerFactory, IServiceProvider serviceProvider, IPluginConfigurationService pluginConfig) : IPluginRegistry
{
    private readonly ILogger<PluginRegistry> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IPluginConfigurationService _pluginConfig = pluginConfig;
    private readonly Dictionary<string, PluginMetadata> _pluginMetadata = [];
    private readonly List<IUrlTask> _registeredTasks = [];
    private readonly List<IExportProvider> _registeredExporters = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isLoaded;

    public event EventHandler<PluginEventArgs>? PluginLoaded;
    public event EventHandler<PluginEventArgs>? PluginUnloaded;

    public IReadOnlyList<IPlugin> LoadedPlugins
    {
        get
        {
            _lock.Wait();
            try
            {
                return _pluginMetadata.Values.Select(m => m.Plugin).ToList().AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public IReadOnlyList<IPlugin> EnabledPlugins
    {
        get
        {
            _lock.Wait();
            try
            {
                // Batch fetch all plugin states to avoid multiple async calls
                var allStates = _pluginConfig.GetAllPluginStatesAsync().GetAwaiter().GetResult();
                
                var enabledPlugins = new List<IPlugin>();
                foreach (var metadata in _pluginMetadata.Values)
                {
                    // Check enabled state from batch result (default to enabled if not in config)
                    var isEnabled = !allStates.TryGetValue(metadata.Plugin.Id, out var enabled) || enabled;
                    if (isEnabled)
                    {
                        enabledPlugins.Add(metadata.Plugin);
                    }
                }
                return enabledPlugins.AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public IReadOnlyList<IUrlTask> RegisteredTasks
    {
        get
        {
            _lock.Wait();
            try
            {
                return _registeredTasks.ToList().AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public IReadOnlyList<IUrlTask> EnabledTasks
    {
        get
        {
            _lock.Wait();
            try
            {
                // Batch fetch all plugin states to avoid multiple async calls
                var allStates = _pluginConfig.GetAllPluginStatesAsync().GetAwaiter().GetResult();
                
                var enabledTasks = new List<IUrlTask>();
                foreach (var metadata in _pluginMetadata.Values)
                {
                    // Check enabled state from batch result (default to enabled if not in config)
                    var isEnabled = !allStates.TryGetValue(metadata.Plugin.Id, out var enabled) || enabled;
                    if (isEnabled)
                    {
                        enabledTasks.AddRange(metadata.Tasks);
                    }
                }
                return enabledTasks.AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public IReadOnlyList<IExportProvider> RegisteredExporters
    {
        get
        {
            _lock.Wait();
            try
            {
                return _registeredExporters.ToList().AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public IReadOnlyList<IExportProvider> EnabledExporters
    {
        get
        {
            _lock.Wait();
            try
            {
                // Batch fetch all plugin states to avoid multiple async calls
                var allStates = _pluginConfig.GetAllPluginStatesAsync().GetAwaiter().GetResult();
                
                var enabledExporters = new List<IExportProvider>();
                foreach (var metadata in _pluginMetadata.Values)
                {
                    // Check enabled state from batch result (default to enabled if not in config)
                    var isEnabled = !allStates.TryGetValue(metadata.Plugin.Id, out var enabled) || enabled;
                    if (isEnabled)
                    {
                        enabledExporters.AddRange(metadata.Exporters);
                    }
                }
                return enabledExporters.AsReadOnly();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public async Task LoadPluginsAsync()
    {
        await _lock.WaitAsync();
        bool alreadyLoaded = _isLoaded;
        _lock.Release();
        
        if (alreadyLoaded)
        {
            _logger.LogInformation("Plugins already loaded, skipping initial discovery");
            return;
        }

        _logger.LogInformation("Starting plugin discovery...");

        var pluginsPath = GetPluginsPath();
        if (!Directory.Exists(pluginsPath))
        {
            _logger.LogWarning("Plugins directory not found: {Path}", pluginsPath);
            Directory.CreateDirectory(pluginsPath);
            await _lock.WaitAsync();
            _isLoaded = true;
            _lock.Release();
            return;
        }

        var pluginDirs = Directory.GetDirectories(pluginsPath);
        _logger.LogInformation("Found {Count} plugin directories", pluginDirs.Length);

        foreach (var pluginDir in pluginDirs)
        {
            // Load each plugin directory using hot-loading
            await LoadPluginFromDirectoryAsync(pluginDir);
        }

        await _lock.WaitAsync();
        try
        {
            _isLoaded = true;
            _logger.LogInformation("Plugin discovery complete. Loaded {Count} plugins, {TaskCount} tasks, {ExportCount} exporters",
                _pluginMetadata.Count, _registeredTasks.Count, _registeredExporters.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task LoadPluginAsync(string packagePath)
    {
        await LoadPluginFromDirectoryAsync(packagePath);
    }

    public async Task UnloadPluginAsync(string pluginId)
    {
        // ReSharper disable once RedundantDefaultMemberInitializer
        IPlugin? unloadedPlugin = null;
        WeakReference? weakRef = null;

        await _lock.WaitAsync();
        try
        {
            if (!_pluginMetadata.TryGetValue(pluginId, out var metadata))
            {
                _logger.LogWarning("Cannot unload plugin {PluginId}: not found", pluginId);
                return;
            }

            _logger.LogInformation("Unloading plugin: {PluginName}", metadata.Plugin.Name);

            // Capture plugin reference for event notification (used after lock release)
            unloadedPlugin = metadata.Plugin;
            
            // Capture weak reference for GC verification
            weakRef = metadata.WeakRef;
            metadata.UnloadedAt = DateTime.UtcNow;

            // Unregister all tasks and exporters from this plugin
            foreach (var task in metadata.Tasks)
            {
                _registeredTasks.Remove(task);
            }

            foreach (var exporter in metadata.Exporters)
            {
                _registeredExporters.Remove(exporter);
            }

            // Dispose plugin if it implements IDisposable
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (metadata.Plugin is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Remove from tracking
            _pluginMetadata.Remove(pluginId);

            // Unload the AssemblyLoadContext
            metadata.LoadContext.Unload();

            _logger.LogInformation("✓ Unloaded plugin: {PluginName}", metadata.Plugin.Name);
        }
        finally
        {
            _lock.Release();
        }

        // Notify listeners AFTER releasing lock to prevent deadlock
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (unloadedPlugin != null)
        {
            try
            {
                PluginUnloaded?.Invoke(this, new PluginEventArgs(unloadedPlugin));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error notifying plugin unloaded listeners");
            }
        }

        // Force GC to help clean up unloaded assemblies
        await Task.Run(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        });
        
        // Verify that the AssemblyLoadContext was collected
        // This helps detect memory leaks from plugin unloading
        if (weakRef != null)
        {
            if (weakRef.IsAlive)
            {
                _logger.LogWarning(
                    "Plugin AssemblyLoadContext for {PluginId} is still alive after GC. " +
                    "This may indicate a memory leak. Check for plugin references being held.",
                    pluginId);
            }
            else
            {
                _logger.LogDebug("✓ Plugin AssemblyLoadContext for {PluginId} was successfully garbage collected", pluginId);
            }
        }
    }

    public async Task ReloadPluginAsync(string pluginId)
    {
        string? sourcePath = null;

        await _lock.WaitAsync();
        try
        {
            if (_pluginMetadata.TryGetValue(pluginId, out var metadata))
            {
                sourcePath = metadata.SourcePath;
            }
        }
        finally
        {
            _lock.Release();
        }

        if (sourcePath == null)
        {
            _logger.LogWarning("Cannot reload plugin {PluginId}: not found", pluginId);
            return;
        }

        // Unload first
        await UnloadPluginAsync(pluginId);

        // Wait a moment for GC
        await Task.Delay(100);

        // Reload
        await LoadPluginFromDirectoryAsync(sourcePath);
    }

    public async Task<bool> IsPluginActiveAsync(string pluginId)
    {
        await _lock.WaitAsync();
        try
        {
            return _pluginMetadata.ContainsKey(pluginId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<IUrlTask> GetTasksByPriority()
    {
        _lock.Wait();
        try
        {
            // Batch fetch all plugin states to avoid multiple async calls
            var allStates = _pluginConfig.GetAllPluginStatesAsync().GetAwaiter().GetResult();
            
            // Only return tasks from enabled plugins
            var enabledTasks = new List<IUrlTask>();
            foreach (var metadata in _pluginMetadata.Values)
            {
                // Check enabled state from batch result (default to enabled if not in config)
                var isEnabled = !allStates.TryGetValue(metadata.Plugin.Id, out var enabled) || enabled;
                if (isEnabled)
                {
                    enabledTasks.AddRange(metadata.Tasks);
                }
            }
            return enabledTasks.OrderBy(t => t.Priority).ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public IPlugin? GetPluginById(string id)
    {
        _lock.Wait();
        try
        {
            return _pluginMetadata.TryGetValue(id, out var metadata) ? metadata.Plugin : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadPluginFromDirectoryAsync(string pluginDir)
    {
        try
        {
            var pluginName = Path.GetFileName(pluginDir);
            _logger.LogInformation("Loading plugin from: {PluginDir}", pluginName);

            // Find all DLL files in the plugin directory
            var dllFiles = Directory.GetFiles(pluginDir, "*.dll");
            
            foreach (var dllFile in dllFiles)
            {
                // Skip known framework assemblies
                var fileName = Path.GetFileNameWithoutExtension(dllFile);
                if (IsFrameworkAssembly(fileName))
                {
                    continue;
                }

                try
                {
                    // Create a new AssemblyLoadContext for this plugin
                    var loadContext = new PluginLoadContext(dllFile, _logger);
                    
                    // Load the assembly into the context
                    var assembly = loadContext.LoadFromAssemblyPath(dllFile);
                    
                    await LoadPluginsFromAssemblyAsync(assembly, pluginName, loadContext, pluginDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load assembly: {DllFile}", dllFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugin from directory: {PluginDir}", pluginDir);
        }
    }

    private async Task LoadPluginsFromAssemblyAsync(Assembly assembly, string source, PluginLoadContext loadContext, string sourcePath)
    {
        try
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t));

            foreach (var type in types)
            {
                try
                {
                    // Check for [Plugin] attribute
                    var pluginAttr = type.GetCustomAttribute<PluginAttribute>();
                    if (pluginAttr == null)
                    {
                    _logger.LogWarning("Plugin class {TypeName} missing [Plugin] attribute", type.Name);
                        continue;
                    }

                    // Validate SDK version compatibility
                    if (!ValidateSdkVersion(pluginAttr.MinSdkVersion))
                    {
                        _logger.LogWarning("Plugin {PluginName} requires SDK version {Version}, skipping",
                            pluginAttr.Name, pluginAttr.MinSdkVersion);
                        continue;
                    }

                    // Create instance
                    var plugin = Activator.CreateInstance(type) as IPlugin;
                    if (plugin == null)
                    {
                        _logger.LogWarning("Failed to create instance of plugin: {TypeName}", type.Name);
                        continue;
                    }

                    // Check if plugin already loaded
                    await _lock.WaitAsync();
                    try
                    {
                        if (_pluginMetadata.ContainsKey(plugin.Id))
                        {
                            _logger.LogWarning("Plugin {PluginId} already loaded, skipping", plugin.Id);
                            continue;
                        }

                        // Create metadata to track tasks/exporters for this plugin
                        var metadata = new PluginMetadata
                        {
                            Plugin = plugin,
                            LoadContext = loadContext,
                            WeakRef = new WeakReference(loadContext),
                            SourcePath = sourcePath,
                        };

                        // Create a host context that tracks registrations
                        var hostContext = new HostContext(
                            _loggerFactory, 
                            _serviceProvider, 
                            _registeredTasks, 
                            _registeredExporters,
                            metadata.Tasks,
                            metadata.Exporters);
                        
                        // Initialize plugin
                        plugin.Initialize(hostContext);
                        
                        // Track the plugin
                        _pluginMetadata.Add(plugin.Id, metadata);
                        
                        _logger.LogInformation("✓ Loaded plugin: {PluginName} v{Version} from {Source}",
                            plugin.Name, plugin.Version, source);

                        // Notify listeners (after releasing lock to avoid deadlock)
                        var pluginCopy = plugin;
                        _ = Task.Run(() => PluginLoaded?.Invoke(this, new PluginEventArgs(pluginCopy)));
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading plugin type: {TypeName}", type.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugins from assembly: {AssemblyName}", assembly.FullName);
        }

        await Task.CompletedTask;
    }

    private bool IsFrameworkAssembly(string fileName)
    {
        var frameworkPrefixes = new[]
        {
            "System.", "Microsoft.", "netstandard", "mscorlib",
            "ShoutingIguana.PluginSdk" // Our SDK is already loaded
        };

        return frameworkPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private bool ValidateSdkVersion(string minVersionString)
    {
        try
        {
            var minVersion = Version.Parse(minVersionString);
            var currentVersion = new Version(1, 0, 0); // Current SDK version
            return currentVersion >= minVersion;
        }
        catch
        {
            return false;
        }
    }

    private static string GetPluginsPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "plugins");
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}

/// <summary>
/// Implementation of IHostContext passed to plugins during initialization.
/// Tracks registrations per plugin for hot-unloading support.
/// </summary>
internal class HostContext(
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    List<IUrlTask> globalTasks,
    List<IExportProvider> globalExporters,
    List<IUrlTask> pluginTasks,
    List<IExportProvider> pluginExporters) : IHostContext
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly List<IUrlTask> _globalTasks = globalTasks;
    private readonly List<IExportProvider> _globalExporters = globalExporters;
    private readonly List<IUrlTask> _pluginTasks = pluginTasks;
    private readonly List<IExportProvider> _pluginExporters = pluginExporters;

    public void RegisterTask(IUrlTask task)
    {
        _globalTasks.Add(task);
        _pluginTasks.Add(task);
    }

    public void RegisterExport(IExportProvider export)
    {
        _globalExporters.Add(export);
        _pluginExporters.Add(export);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggerFactory.CreateLogger(categoryName);
    }

    public ILogger<T> CreateLogger<T>()
    {
        return _loggerFactory.CreateLogger<T>();
    }

    public IServiceProvider GetServiceProvider()
    {
        return _serviceProvider;
    }
    
    public IRepositoryAccessor GetRepositoryAccessor()
    {
        return _serviceProvider.GetRequiredService<IRepositoryAccessor>();
    }
}
