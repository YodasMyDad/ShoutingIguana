using CommunityToolkit.Mvvm.ComponentModel;

namespace ShoutingIguana.ViewModels.Models;

public class FeedInfo : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
}

