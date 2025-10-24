using System;
using System.Windows;
using System.Windows.Controls;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana.Views;

public partial class FindingsView : UserControl
{
    private readonly FindingsViewModel _viewModel;
    
    public FindingsView(FindingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        // Subscribe to events
        Loaded += FindingsView_Loaded;
        Unloaded += FindingsView_Unloaded;
    }
    
    private async void FindingsView_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadFindingsAsync();
    }
    
    private void FindingsView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from events to prevent memory leaks
        Loaded -= FindingsView_Loaded;
        Unloaded -= FindingsView_Unloaded;
    }
}

