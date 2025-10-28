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
    
    public bool IncludeTechnicalMetadata => 
        DataContext is ExportOptionsViewModel vm && vm.IncludeTechnicalMetadata;
}

