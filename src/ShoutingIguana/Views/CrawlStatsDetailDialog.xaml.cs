using System;
using System.Windows;
using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class CrawlStatsDetailDialog : Window
{
    private readonly CrawlStatsDetailViewModel _viewModel;

    public CrawlStatsDetailDialog(CrawlStatsDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.Dispose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0)
        {
            return;
        }

        if (e.OriginalSource is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight == 0)
        {
            return;
        }

        var ratio = scrollViewer.VerticalOffset / scrollViewer.ScrollableHeight;
        if (ratio >= 0.8)
        {
            _viewModel.LoadNextPageCommand?.Execute(null);
        }
    }
}
