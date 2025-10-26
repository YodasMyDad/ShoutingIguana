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
        var proxyTestService = serviceProvider.GetRequiredService<IProxyTestService>();
        var feedConfigService = serviceProvider.GetRequiredService<ShoutingIguana.Core.Services.NuGet.IFeedConfigurationService>();
        
        DataContext = new SettingsViewModel(logger, appSettings, pluginRegistry, proxyTestService, feedConfigService, serviceProvider, this);
        
        // Load initial proxy password into PasswordBox
        if (DataContext is SettingsViewModel viewModel)
        {
            ProxyPasswordBox.Password = viewModel.ProxyPassword;
        }
    }

    private void ProxyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.ProxyPassword = ProxyPasswordBox.Password;
        }
    }
}

