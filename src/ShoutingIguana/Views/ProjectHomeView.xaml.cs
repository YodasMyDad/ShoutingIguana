using System.Windows;
using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class ProjectHomeView : UserControl
{
    private readonly ProjectHomeViewModel _viewModel;
    
    public ProjectHomeView(ProjectHomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        // Subscribe to events
        Loaded += ProjectHomeView_Loaded;
        Unloaded += ProjectHomeView_Unloaded;
    }
    
    private async void ProjectHomeView_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
    
    private void ProjectHomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from events to prevent memory leaks
        Loaded -= ProjectHomeView_Loaded;
        Unloaded -= ProjectHomeView_Unloaded;
    }
}

