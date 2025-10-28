using System;
using System.Windows;
using System.Windows.Controls;
using ShoutingIguana.Core.Models;
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
    
    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Only handle the IsEnabled column changes on commit
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is CustomExtractionRule rule)
        {
            // Check if this is the Enabled column by checking if it's a CheckBoxColumn
            if (e.Column is DataGridCheckBoxColumn)
            {
                // The checkbox binding hasn't updated yet at this point in the event lifecycle
                // Schedule the save to run after the binding completes
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await _viewModel.SaveExtractionRuleToggleAsync(rule);
                    }
                    catch (Exception ex)
                    {
                        // Error is already logged in the ViewModel, but ensure exception doesn't go unhandled
                        System.Diagnostics.Debug.WriteLine($"Error saving extraction rule toggle: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}

