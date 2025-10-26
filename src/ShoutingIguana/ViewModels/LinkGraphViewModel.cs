using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Services;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

public partial class LinkGraphViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<LinkGraphViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectContext _projectContext;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<LinkDisplayModel> _links = [];

    [ObservableProperty]
    private ObservableCollection<LinkDisplayModel> _filteredLinks = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyFromUrlCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToUrlCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAnchorTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFromUrlCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenToUrlCommand))]
    private LinkDisplayModel? _selectedLink;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalLinkCount;

    [ObservableProperty]
    private int _filteredLinkCount;

    public LinkGraphViewModel(
        ILogger<LinkGraphViewModel> logger,
        IServiceProvider serviceProvider,
        IProjectContext projectContext)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _projectContext = projectContext;
    }

    public async Task LoadAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            _logger.LogWarning("Cannot load links: no project is open");
            return;
        }

        IsLoading = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var linkRepo = scope.ServiceProvider.GetRequiredService<ILinkRepository>();
            var urlRepo = scope.ServiceProvider.GetRequiredService<IUrlRepository>();

            var projectId = _projectContext.CurrentProjectId!.Value;
            var linksData = await linkRepo.GetByProjectIdAsync(projectId);
            var urls = (await urlRepo.GetByProjectIdAsync(projectId)).ToDictionary(u => u.Id, u => u.Address);

            var linkModels = linksData.Select(link => new LinkDisplayModel
            {
                Id = link.Id,
                FromUrl = urls.ContainsKey(link.FromUrlId) ? urls[link.FromUrlId] : "Unknown",
                ToUrl = urls.ContainsKey(link.ToUrlId) ? urls[link.ToUrlId] : "Unknown",
                AnchorText = link.AnchorText ?? "(No text)",
                LinkType = link.LinkType.ToString()
            }).ToList();

            // Update collections on UI thread since they're bound to UI
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Links = new ObservableCollection<LinkDisplayModel>(linkModels);
                TotalLinkCount = Links.Count;
                ApplyFilter();
            });

            _logger.LogInformation("Loaded {Count} links", Links.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load links");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to load links: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = value; // Suppress warning
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredLinks = new ObservableCollection<LinkDisplayModel>(Links);
        }
        else
        {
            var query = SearchQuery.ToLowerInvariant();
            var filtered = Links.Where(link =>
                link.FromUrl.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                link.ToUrl.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (link.AnchorText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

            FilteredLinks = new ObservableCollection<LinkDisplayModel>(filtered);
        }

        FilteredLinkCount = FilteredLinks.Count;
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
                FileName = $"link-graph-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                // Export filtered links to CSV
                await ExportLinksToCsv(dialog.FileName);
                _logger.LogInformation("Link graph exported to: {FilePath}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export link graph");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private void CopySelected()
    {
        try
        {
            if (SelectedLink != null)
            {
                var textToCopy = $"{SelectedLink.FromUrl}\t{SelectedLink.ToUrl}\t{SelectedLink.AnchorText}";
                Clipboard.SetText(textToCopy);
                _logger.LogDebug("Copied link to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy to clipboard");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteLinkCommand))]
    private void CopyFromUrl()
    {
        try
        {
            if (SelectedLink != null)
            {
                Clipboard.SetText(SelectedLink.FromUrl);
                _logger.LogDebug("Copied From URL to clipboard: {Url}", SelectedLink.FromUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy From URL to clipboard");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteLinkCommand))]
    private void CopyToUrl()
    {
        try
        {
            if (SelectedLink != null)
            {
                Clipboard.SetText(SelectedLink.ToUrl);
                _logger.LogDebug("Copied To URL to clipboard: {Url}", SelectedLink.ToUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy To URL to clipboard");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteLinkCommand))]
    private void CopyAnchorText()
    {
        try
        {
            if (SelectedLink != null)
            {
                var anchorText = SelectedLink.AnchorText ?? string.Empty;
                Clipboard.SetText(anchorText);
                _logger.LogDebug("Copied anchor text to clipboard: {Text}", anchorText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy anchor text to clipboard");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteLinkCommand))]
    private void OpenFromUrl()
    {
        try
        {
            if (SelectedLink != null && !string.IsNullOrEmpty(SelectedLink.FromUrl))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedLink.FromUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                _logger.LogInformation("Opened From URL in browser: {Url}", SelectedLink.FromUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open From URL in browser");
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteLinkCommand))]
    private void OpenToUrl()
    {
        try
        {
            if (SelectedLink != null && !string.IsNullOrEmpty(SelectedLink.ToUrl))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedLink.ToUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                _logger.LogInformation("Opened To URL in browser: {Url}", SelectedLink.ToUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open To URL in browser");
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private bool CanExecuteLinkCommand()
    {
        return SelectedLink != null;
    }

    private async Task ExportLinksToCsv(string filePath)
    {
        try
        {
            // Snapshot the collection to avoid issues with concurrent modification
            var linksSnapshot = FilteredLinks.ToList();
            
            using var writer = new System.IO.StreamWriter(filePath);
            
            // Write header
            await writer.WriteLineAsync("FromUrl,ToUrl,AnchorText,LinkType");
            
            // Write data rows
            foreach (var link in linksSnapshot)
            {
                var line = $"\"{EscapeCsv(link.FromUrl)}\",\"{EscapeCsv(link.ToUrl)}\",\"{EscapeCsv(link.AnchorText)}\",\"{link.LinkType}\"";
                await writer.WriteLineAsync(line);
            }
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Successfully exported {linksSnapshot.Count} links to CSV", "Export Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write CSV file");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export CSV: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        
        // Escape quotes by doubling them
        return value.Replace("\"", "\"\"");
    }

    [RelayCommand]
    private void SelectAll()
    {
        try
        {
            // In WPF DataGrid, SelectAll is typically handled by the view
            // This is a placeholder that the view can override
            _logger.LogDebug("Select all requested in link graph view");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select all");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}

