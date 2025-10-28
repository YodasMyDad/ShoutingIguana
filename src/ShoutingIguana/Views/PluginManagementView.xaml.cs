using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class PluginManagementView : UserControl
{
    public PluginManagementView(PluginManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

