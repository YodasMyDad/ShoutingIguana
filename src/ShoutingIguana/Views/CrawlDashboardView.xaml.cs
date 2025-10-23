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
        
        Loaded += async (s, e) =>
        {
            // Auto-start crawl when view is loaded
            await _viewModel.InitializeAsync(autoStart: true);
        };
    }
}

