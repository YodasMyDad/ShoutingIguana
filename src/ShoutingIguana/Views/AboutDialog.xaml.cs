using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class AboutDialog : Window
{
    public AboutDialog(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        var logger = serviceProvider.GetRequiredService<ILogger<AboutViewModel>>();
        DataContext = new AboutViewModel(logger, this);
    }
}

