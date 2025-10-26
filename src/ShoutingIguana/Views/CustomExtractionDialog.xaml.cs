using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class CustomExtractionDialog : Window
{
    public CustomExtractionDialog(IServiceProvider serviceProvider, int projectId)
    {
        InitializeComponent();
        
        // Create ViewModel
        var viewModel = new CustomExtractionViewModel(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CustomExtractionViewModel>>(),
            serviceProvider.GetRequiredService<ShoutingIguana.Core.Services.ICustomExtractionService>(),
            serviceProvider.GetRequiredService<Services.IToastService>(),
            projectId,
            this);
        
        DataContext = viewModel;
        
        // Load rules asynchronously
        Loaded += async (s, e) => await viewModel.LoadRulesAsync();
    }
}

