using CommunityToolkit.Mvvm.ComponentModel;

namespace ShoutingIguana.ViewModels.Models;

public class PluginInfo : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

