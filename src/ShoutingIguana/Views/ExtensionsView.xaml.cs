using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class ExtensionsView : UserControl
{
    public ExtensionsView(ExtensionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

