using System.Windows;
using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class CrawlDashboardView : UserControl
{
    private readonly CrawlDashboardViewModel _viewModel;

    public CrawlDashboardView(CrawlDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        
        // Subscribe to events
        Loaded += CrawlDashboardView_Loaded;
        Unloaded += CrawlDashboardView_Unloaded;
    }
    
    private async void CrawlDashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-start crawl when view is loaded
        await _viewModel.InitializeAsync(autoStart: true);
    }
    
    private void CrawlDashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from events to prevent memory leaks
        Loaded -= CrawlDashboardView_Loaded;
        Unloaded -= CrawlDashboardView_Unloaded;
    }
}

