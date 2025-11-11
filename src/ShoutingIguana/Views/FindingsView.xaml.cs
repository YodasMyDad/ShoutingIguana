using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.ViewModels;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.Views;

public partial class FindingsView
{
    private readonly FindingsViewModel _viewModel;
    private DataGrid? _urlPropertiesDataGrid;
    private DataGrid? _findingsDataGrid;
    
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
            
            // Unsubscribe from current tab's property changes
            if (_viewModel.SelectedTab is FindingTabViewModel currentFindingTab)
            {
                currentFindingTab.PropertyChanged -= FindingTab_PropertyChanged;
            }
        }
        
        // Unsubscribe from collection changes
        if (_currentOverviewTab != null)
        {
            _currentOverviewTab.UrlProperties.CollectionChanged -= UrlProperties_CollectionChanged;
            _currentOverviewTab = null;
        }
        
        // Clear references
        _currentFindingTab = null;
        _lastDynamicTab = null;
        _findingsDataGrid = null;
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
    private FindingTabViewModel? _currentFindingTab;
    
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
            
            // Unsubscribe from previous finding tab's property changes
            if (_currentFindingTab != null)
            {
                _currentFindingTab.PropertyChanged -= FindingTab_PropertyChanged;
                _currentFindingTab = null;
            }
            
            // Subscribe to new tab's events
            if (_viewModel.SelectedTab is OverviewTabViewModel newOverviewTab)
            {
                _currentOverviewTab = newOverviewTab;
                newOverviewTab.UrlProperties.CollectionChanged += UrlProperties_CollectionChanged;
                // Apply grouping immediately
                ApplyUrlPropertiesGrouping();
            }
            else if (_viewModel.SelectedTab is FindingTabViewModel findingTab)
            {
                // Subscribe to property changes to detect when dynamic schema is loaded
                _currentFindingTab = findingTab;
                findingTab.PropertyChanged += FindingTab_PropertyChanged;
            }
        }
    }
    
    private FindingTabViewModel? _lastDynamicTab;
    
    private void FindingTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindingTabViewModel.ReportColumns) && sender is FindingTabViewModel tab)
        {
            // Dynamic columns loaded - regenerate DataGrid columns
            // Guard: Only do this once per tab to avoid re-generating on every property change
            if (tab.HasDynamicSchema && tab.ReportColumns.Count > 0 && _lastDynamicTab != tab)
            {
                _lastDynamicTab = tab;
                Dispatcher.InvokeAsync(() => GenerateDynamicColumns(tab), System.Windows.Threading.DispatcherPriority.Normal);
            }
        }
    }
    
    private void GenerateDynamicColumns(FindingTabViewModel tab)
    {
        try
        {
            // Find the findings DataGrid for this tab
            var dataGrid = FindFindingsDataGrid();
            if (dataGrid == null)
            {
                // DataGrid not found yet - might be in visual tree initialization
                // Schedule retry after a short delay
                Dispatcher.InvokeAsync(() =>
                {
                    var retryDataGrid = FindFindingsDataGrid();
                    if (retryDataGrid != null)
                    {
                        ApplyDynamicColumnsToDataGrid(retryDataGrid, tab);
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            
            ApplyDynamicColumnsToDataGrid(dataGrid, tab);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error generating dynamic columns: {ex.Message}");
        }
    }
    
    private void ApplyDynamicColumnsToDataGrid(DataGrid dataGrid, FindingTabViewModel tab)
    {
        // Clear existing columns
        dataGrid.Columns.Clear();
        
        // Bind to ReportRows instead of FilteredFindings
        dataGrid.SetBinding(DataGrid.ItemsSourceProperty, new Binding("ReportRows"));
        dataGrid.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedReportRow") { Mode = BindingMode.TwoWay });
        
        // Generate columns from schema
        foreach (var column in tab.ReportColumns)
        {
            var dataGridColumn = CreateColumnFromDefinition(column);
            dataGrid.Columns.Add(dataGridColumn);
        }
    }
    
    private DataGridColumn CreateColumnFromDefinition(ReportColumnViewModel column)
    {
        switch (column.ColumnType)
        {
            case ReportColumnType.Url:
                return CreateUrlColumn(column);
            
            case ReportColumnType.DateTime:
                return CreateDateTimeColumn(column);
            
            case ReportColumnType.Integer:
            case ReportColumnType.Decimal:
                return CreateNumericColumn(column);
            
            case ReportColumnType.Boolean:
                return CreateBooleanColumn(column);
            
            case ReportColumnType.String:
            default:
                return CreateTextColumn(column);
        }
    }
    
    private DataGridTextColumn CreateTextColumn(ReportColumnViewModel column)
    {
        return new DataGridTextColumn
        {
            Header = column.DisplayName,
            Binding = new Binding($"[{column.Name}]"),
            Width = new DataGridLength(column.Width),
            MinWidth = 100,
            CanUserSort = column.IsSortable
        };
    }
    
    private DataGridHyperlinkColumn CreateUrlColumn(ReportColumnViewModel column)
    {
        var col = new DataGridHyperlinkColumn
        {
            Header = column.DisplayName,
            Binding = new Binding($"[{column.Name}]"),
            Width = new DataGridLength(column.Width),
            MinWidth = 150,
            CanUserSort = column.IsSortable
        };
        
        // Apply font style
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
        col.ElementStyle = style;
        
        return col;
    }
    
    private DataGridTextColumn CreateDateTimeColumn(ReportColumnViewModel column)
    {
        return new DataGridTextColumn
        {
            Header = column.DisplayName,
            Binding = new Binding($"[{column.Name}]") { StringFormat = "yyyy-MM-dd HH:mm:ss" },
            Width = new DataGridLength(column.Width),
            MinWidth = 140,
            CanUserSort = column.IsSortable,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
    }
    
    private DataGridTextColumn CreateNumericColumn(ReportColumnViewModel column)
    {
        var col = new DataGridTextColumn
        {
            Header = column.DisplayName,
            Binding = new Binding($"[{column.Name}]"),
            Width = new DataGridLength(column.Width),
            MinWidth = 80,
            CanUserSort = column.IsSortable
        };
        
        // Right-align numbers
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        col.ElementStyle = style;
        
        return col;
    }
    
    private DataGridCheckBoxColumn CreateBooleanColumn(ReportColumnViewModel column)
    {
        return new DataGridCheckBoxColumn
        {
            Header = column.DisplayName,
            Binding = new Binding($"[{column.Name}]"),
            Width = new DataGridLength(column.Width),
            MinWidth = 60,
            CanUserSort = column.IsSortable,
            IsReadOnly = true
        };
    }
    
    private DataGrid? FindFindingsDataGrid()
    {
        // Find the FindingsTab DataGrid in the visual tree
        _findingsDataGrid = FindVisualChild<DataGrid>(this);
        if (_findingsDataGrid?.Tag as string == "FindingsTab")
        {
            return _findingsDataGrid;
        }
        return null;
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
    
    private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Only trigger on scroll down
        if (e.VerticalChange <= 0) return;
        
        var scrollViewer = e.OriginalSource as ScrollViewer;
        if (scrollViewer == null || scrollViewer.ScrollableHeight == 0) return;
        
        // Trigger load when scrolled to 80% of content
        var threshold = 0.8;
        var scrollPosition = scrollViewer.VerticalOffset / scrollViewer.ScrollableHeight;
        
        if (scrollPosition >= threshold)
        {
            var dataGrid = sender as DataGrid;
            var tag = dataGrid?.Tag as string;
            
            if (tag == "OverviewTab" && DataContext is FindingsViewModel vm)
            {
                if (vm.SelectedTab is OverviewTabViewModel overviewVm)
                {
                    overviewVm.LoadNextPageCommand?.Execute(null);
                }
            }
            else if (tag == "FindingsTab" && DataContext is FindingsViewModel vm2)
            {
                if (vm2.SelectedTab is FindingTabViewModel findingVm)
                {
                    // MVVM Toolkit strips Async suffix from command name
                    findingVm.LoadNextPageCommand?.Execute(null);
                }
            }
        }
    }
}

