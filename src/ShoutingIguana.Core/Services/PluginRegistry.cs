using System.Reflection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IPluginRegistry for discovering and managing plugins.
/// </summary>
public class PluginRegistry(ILogger<PluginRegistry> logger, ILoggerFactory loggerFactory, IServiceProvider serviceProvider) : IPluginRegistry
{
    private readonly ILogger<PluginRegistry> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly List<IPlugin> _loadedPlugins = [];
    private readonly List<IUrlTask> _registeredTasks = [];
    private readonly List<IExportProvider> _registeredExporters = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isLoaded;

    public IReadOnlyList<IPlugin> LoadedPlugins
    {
        get
        {
            _lock.Wait();
            try
            {
                return _loadedPlugins.ToList().AsReadOnly();
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

    public async Task LoadPluginsAsync()
    {
        await _lock.WaitAsync();
        bool alreadyLoaded = _isLoaded;
        _lock.Release();
        
        if (alreadyLoaded)
        {
            _logger.LogInformation("Plugins already loaded, skipping");
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
            await LoadPluginFromDirectoryAsync(pluginDir);
        }

        await _lock.WaitAsync();
        try
        {
            _isLoaded = true;
            _logger.LogInformation("Plugin discovery complete. Loaded {Count} plugins, {TaskCount} tasks, {ExportCount} exporters",
                _loadedPlugins.Count, _registeredTasks.Count, _registeredExporters.Count);
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
            return _registeredTasks.OrderBy(t => t.Priority).ToList().AsReadOnly();
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
            return _loadedPlugins.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
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
                    var assembly = Assembly.LoadFrom(dllFile);
                    await LoadPluginsFromAssemblyAsync(assembly, pluginName);
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

    private async Task LoadPluginsFromAssemblyAsync(Assembly assembly, string source)
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

                    // Initialize plugin (thread-safe)
                    await _lock.WaitAsync();
                    try
                    {
                        var hostContext = new HostContext(_loggerFactory, _serviceProvider, _registeredTasks, _registeredExporters);
                        plugin.Initialize(hostContext);
                        _loadedPlugins.Add(plugin);
                    }
                    finally
                    {
                        _lock.Release();
                    }
                    
                    _logger.LogInformation("✓ Loaded plugin: {PluginName} v{Version} from {Source}",
                        plugin.Name, plugin.Version, source);
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
}

/// <summary>
/// Implementation of IHostContext passed to plugins during initialization.
/// </summary>
internal class HostContext(
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    List<IUrlTask> tasks,
    List<IExportProvider> exporters) : IHostContext
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly List<IUrlTask> _tasks = tasks;
    private readonly List<IExportProvider> _exporters = exporters;

    public void RegisterTask(IUrlTask task)
    {
        _tasks.Add(task);
    }

    public void RegisterExport(IExportProvider export)
    {
        _exporters.Add(export);
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
}

