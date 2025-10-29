using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ShoutingIguana.ViewModels;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.Views;

public partial class FindingsView
{
    private readonly FindingsViewModel _viewModel;
    private DataGrid? _urlPropertiesDataGrid;
    
    public FindingsView(FindingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        // Subscribe to events
        Loaded += FindingsView_Loaded;
        Unloaded += FindingsView_Unloaded;
        DataContextChanged += FindingsView_DataContextChanged;
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
        DataContextChanged -= FindingsView_DataContextChanged;
        
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        
        // Unsubscribe from collection changes
        if (_currentOverviewTab != null)
        {
            _currentOverviewTab.UrlProperties.CollectionChanged -= UrlProperties_CollectionChanged;
            _currentOverviewTab = null;
        }
    }
    
    private void FindingsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Subscribe to SelectedTab changes to handle URL properties grouping
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }
    
    private OverviewTabViewModel? _currentOverviewTab;
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindingsViewModel.SelectedTab))
        {
            // Unsubscribe from previous tab's collection changes
            if (_currentOverviewTab != null)
            {
                _currentOverviewTab.UrlProperties.CollectionChanged -= UrlProperties_CollectionChanged;
                _currentOverviewTab = null;
            }
            
            // Subscribe to new tab's collection changes
            if (_viewModel.SelectedTab is OverviewTabViewModel newOverviewTab)
            {
                _currentOverviewTab = newOverviewTab;
                newOverviewTab.UrlProperties.CollectionChanged += UrlProperties_CollectionChanged;
                // Apply grouping immediately
                ApplyUrlPropertiesGrouping();
            }
        }
    }
    
    private void UrlProperties_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reapply grouping whenever the collection changes
        ApplyUrlPropertiesGrouping();
    }
    
    private void UrlPropertiesDataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _urlPropertiesDataGrid = sender as DataGrid;
        ApplyUrlPropertiesGrouping();
        
        // Note: Collection change subscription is handled by ViewModel_PropertyChanged
        // to avoid duplicate subscriptions and ensure proper cleanup
    }
    
    private void ApplyUrlPropertiesGrouping()
    {
        // Use the stored reference or find it if not available
        var dataGrid = _urlPropertiesDataGrid;
        if (dataGrid == null && _viewModel?.SelectedTab is OverviewTabViewModel)
        {
            // Try to find the DataGrid in the visual tree
            dataGrid = FindVisualChild<DataGrid>(this);
        }
        
        if (dataGrid != null && dataGrid.ItemsSource != null)
        {
            var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
            if (view != null && view.CanGroup)
            {
                // Check if grouping is already applied
                bool hasGrouping = view.GroupDescriptions.Count > 0 && 
                                   view.GroupDescriptions[0] is PropertyGroupDescription pgd && 
                                   pgd.PropertyName == "Category";
                
                if (!hasGrouping)
                {
                    // Clear existing grouping first
                    view.GroupDescriptions.Clear();
                    // Apply grouping by Category
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
                }
            }
        }
    }
    
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is T result)
                return result;
            
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        
        return null;
    }
    
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Pass the mouse wheel event to the parent ScrollViewer
        // This allows scrolling the page even when the mouse is over the DataGrid
        if (sender is not DataGrid dataGrid || e.Handled)
            return;
            
        e.Handled = true;
        
        // Find the parent ScrollViewer by walking up the visual tree
        var scrollViewer = FindVisualParent<ScrollViewer>(dataGrid);
        if (scrollViewer != null)
        {
            // Create a new mouse wheel event and raise it on the ScrollViewer
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = e.Source
            };
            scrollViewer.RaiseEvent(eventArg);
        }
    }
    
    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        if (child == null) return null;
        
        var parent = VisualTreeHelper.GetParent(child);
        if (parent == null) return null;
        
        if (parent is T result)
            return result;
            
        return FindVisualParent<T>(parent);
    }
}

