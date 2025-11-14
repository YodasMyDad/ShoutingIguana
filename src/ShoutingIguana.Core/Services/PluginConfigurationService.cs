using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IPluginConfigurationService that stores plugin states in a JSON file.
/// </summary>
public class PluginConfigurationService : IPluginConfigurationService
{
    private readonly ILogger<PluginConfigurationService> _logger;
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, bool> _pluginStates = new();
    
    public event EventHandler<PluginStateChangedEventArgs>? PluginStateChanged;
    
    public PluginConfigurationService(ILogger<PluginConfigurationService> logger)
    {
        _logger = logger;
        
        // Store configuration in user's local app data
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoutingIguana");
        
        Directory.CreateDirectory(appDataPath);
        _configFilePath = Path.Combine(appDataPath, "pluginconfig.json");
        
        // Load existing configuration synchronously during initialization
        // This is acceptable in a constructor for a singleton service as it only happens once at startup
        LoadConfigurationAsync().GetAwaiter().GetResult();
    }
    
    public async Task<bool> IsPluginEnabledAsync(string pluginId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Default to enabled if not configured
            return !_pluginStates.TryGetValue(pluginId, out var enabled) || enabled;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var previousState = _pluginStates.TryGetValue(pluginId, out var prevEnabled) ? prevEnabled : true;
            
            _pluginStates[pluginId] = enabled;
            await SaveConfigurationAsync().ConfigureAwait(false);
            
            // Only raise event if state actually changed
            if (previousState != enabled)
            {
                _logger.LogInformation("Plugin {PluginId} {State}", pluginId, enabled ? "enabled" : "disabled");
                
                // Raise event outside the lock to avoid deadlocks
                var handler = PluginStateChanged;
                if (handler != null)
                {
                    _ = Task.Run(() => handler.Invoke(this, new PluginStateChangedEventArgs(pluginId, enabled)));
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<Dictionary<string, bool>> GetAllPluginStatesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return new Dictionary<string, bool>(_pluginStates);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private async Task LoadConfigurationAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogDebug("Plugin configuration file not found, using defaults");
                return;
            }
            
            var json = await File.ReadAllTextAsync(_configFilePath).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<PluginConfiguration>(json);
            
            if (config?.PluginStates != null)
            {
                _pluginStates = config.PluginStates;
                _logger.LogInformation("Loaded plugin configuration for {Count} plugins", _pluginStates.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugin configuration, using defaults");
            _pluginStates = new Dictionary<string, bool>();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private async Task SaveConfigurationAsync()
    {
        try
        {
            var config = new PluginConfiguration
            {
                PluginStates = _pluginStates
            };
            
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(_configFilePath, json).ConfigureAwait(false);
            _logger.LogDebug("Saved plugin configuration to {Path}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving plugin configuration");
        }
    }
    
    private class PluginConfiguration
    {
        public Dictionary<string, bool> PluginStates { get; set; } = new();
    }
}

