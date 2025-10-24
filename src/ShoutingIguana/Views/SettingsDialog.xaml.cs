using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Services;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        // Create ViewModel with this window instance
        var logger = serviceProvider.GetRequiredService<ILogger<SettingsViewModel>>();
        var appSettings = serviceProvider.GetRequiredService<IAppSettingsService>();
        var pluginRegistry = serviceProvider.GetRequiredService<IPluginRegistry>();
        
        DataContext = new SettingsViewModel(logger, appSettings, pluginRegistry, this);
    }
}

