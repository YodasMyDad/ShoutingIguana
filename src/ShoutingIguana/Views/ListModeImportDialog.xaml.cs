using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class ListModeImportDialog : Window
{
    public ListModeImportDialog(IServiceProvider serviceProvider, int projectId)
    {
        InitializeComponent();
        
        // Create ViewModel
        DataContext = new ListModeImportViewModel(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ListModeImportViewModel>>(),
            serviceProvider.GetRequiredService<ShoutingIguana.Core.Services.IListModeService>(),
            serviceProvider.GetRequiredService<Services.IToastService>(),
            projectId,
            this);
    }
}

