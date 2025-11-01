using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Services;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class ExportOptionsDialog : Window
{
    public ExportOptionsDialog(
        ICsvExportService csvExportService,
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        ILogger<ExportOptionsViewModel> logger)
    {
        InitializeComponent();
        DataContext = new ExportOptionsViewModel(
            this, 
            csvExportService, 
            excelExportService, 
            projectContext, 
            logger);
        
        // Prevent closing the dialog during export
        Closing += OnClosing;
    }
    
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Prevent closing if export is in progress
        if (DataContext is ExportOptionsViewModel vm && vm.IsExporting)
        {
            e.Cancel = true;
        }
    }
    
    public bool ExportSucceeded => 
        DataContext is ExportOptionsViewModel vm && vm.ExportSucceeded;
}

