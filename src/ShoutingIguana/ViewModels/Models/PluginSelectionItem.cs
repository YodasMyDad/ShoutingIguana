using CommunityToolkit.Mvvm.ComponentModel;

namespace ShoutingIguana.ViewModels.Models;

/// <summary>
/// Represents a plugin that can be selected for export.
/// </summary>
public partial class PluginSelectionItem : ObservableObject
{
    [ObservableProperty]
    private string _taskKey = string.Empty;
    
    [ObservableProperty]
    private string _displayName = string.Empty;
    
    [ObservableProperty]
    private bool _isSelected = true;
    
    [ObservableProperty]
    private int _findingCount;
}

