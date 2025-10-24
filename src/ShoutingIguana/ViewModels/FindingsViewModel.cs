using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class FindingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<FindingsViewModel> _logger;
    private readonly ICsvExportService _csvExportService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private Timer? _searchDebounceTimer;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<Url> _urls = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    private List<Url> _allUrls = [];

    public FindingsViewModel(
        ILogger<FindingsViewModel> logger,
        ICsvExportService csvExportService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _csvExportService = csvExportService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending search
        _searchDebounceTimer?.Dispose();
        
        // Create a new timer that will fire after 300ms
        _searchDebounceTimer = new Timer(_ =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PerformSearch();
            });
        }, null, 300, Timeout.Infinite);
    }

    public async Task LoadUrlsAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            _logger.LogWarning("Cannot load URLs: no project is open");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            
            var projectId = _projectContext.CurrentProjectId!.Value;
            var urls = await urlRepository.GetByProjectIdAsync(projectId);
            _allUrls = urls.ToList();
            Urls = new ObservableCollection<Url>(_allUrls);
            TotalCount = _allUrls.Count;
            _logger.LogInformation("Loaded {Count} URLs for project {ProjectId}", TotalCount, projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load URLs");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to load URLs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private void PerformSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Urls = new ObservableCollection<Url>(_allUrls);
        }
        else
        {
            var filtered = _allUrls
                .Where(u => u.Address.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Urls = new ObservableCollection<Url>(filtered);
        }
        
        TotalCount = Urls.Count;
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return;
        }

        try
        {
            var dialog = new VistaSaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"shouting-iguana-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var projectId = _projectContext.CurrentProjectId!.Value;
                await _csvExportService.ExportUrlInventoryAsync(projectId, dialog.FileName);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Exported {TotalCount} URLs to {dialog.FileName}", "Export Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information));
                _logger.LogInformation("Exported URLs to {FilePath}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to CSV");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadUrlsAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _searchDebounceTimer?.Dispose();
        _disposed = true;
    }
}

