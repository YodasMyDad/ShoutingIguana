using System.Windows;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class ExportOptionsDialog : Window
{
    public ExportOptionsDialog()
    {
        InitializeComponent();
        DataContext = new ExportOptionsViewModel(this);
    }
    
    public string ExportFormat => 
        DataContext is ExportOptionsViewModel vm ? vm.ExportFormat : "Excel";
    
    public bool IncludeTechnicalMetadata => 
        DataContext is ExportOptionsViewModel vm && vm.IncludeTechnicalMetadata;
    
    public bool IncludeErrors => 
        DataContext is ExportOptionsViewModel vm && vm.IncludeErrors;
    
    public bool IncludeWarnings => 
        DataContext is ExportOptionsViewModel vm && vm.IncludeWarnings;
    
    public bool IncludeInfo => 
        DataContext is ExportOptionsViewModel vm && vm.IncludeInfo;
}

