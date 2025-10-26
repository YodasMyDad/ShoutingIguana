using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class LinkGraphView : UserControl
{
    public LinkGraphView(LinkGraphViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Load links when view is created
        _ = viewModel.LoadAsync();
    }
}

