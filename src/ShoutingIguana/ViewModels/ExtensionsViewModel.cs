using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Services;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.ViewModels;

public partial class ExtensionsViewModel : ObservableObject
{
    private readonly ILogger<ExtensionsViewModel> _logger;
    private readonly IPluginRegistry _pluginRegistry;

    [ObservableProperty]
    private ObservableCollection<PluginInfo> _plugins = new();

    [ObservableProperty]
    private int _totalPlugins;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private bool _isLoading;

    public ExtensionsViewModel(
        ILogger<ExtensionsViewModel> logger,
        IPluginRegistry pluginRegistry)
    {
        _logger = logger;
        _pluginRegistry = pluginRegistry;
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        IsLoading = true;
        var loadedPlugins = _pluginRegistry.LoadedPlugins;
        var registeredTasks = _pluginRegistry.RegisteredTasks;

        var pluginInfos = loadedPlugins.Select(p => new PluginInfo
        {
            Name = p.Name,
            Version = p.Version.ToString(),
            Description = p.Description,
            Id = p.Id,
            Status = "Loaded",
            TaskCount = registeredTasks.Count(t => GetPluginForTask(t) == p)
        }).ToList();

        Plugins = new ObservableCollection<PluginInfo>(pluginInfos);
        TotalPlugins = Plugins.Count;
        TotalTasks = registeredTasks.Count;
        IsLoading = false;

        _logger.LogInformation("Loaded {Count} plugins with {TaskCount} tasks", TotalPlugins, TotalTasks);
    }

    private IPlugin? GetPluginForTask(IUrlTask task)
    {
        // Simple heuristic: match task key prefix to plugin name
        // e.g., "BrokenLinks" task belongs to "Broken Links" plugin
        return _pluginRegistry.LoadedPlugins.FirstOrDefault(p => 
            task.Key.Contains(p.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }
}

public class PluginInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TaskCount { get; set; }
}

