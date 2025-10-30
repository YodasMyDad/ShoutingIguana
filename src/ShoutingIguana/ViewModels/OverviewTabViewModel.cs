using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HtmlAgilityPack;
using ShoutingIguana.Core.Models;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the Overview tab that displays all crawled URLs.
/// </summary>
public partial class OverviewTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = "Overview";

    [ObservableProperty]
    private string _description = "All URLs discovered during the crawl";

    [ObservableProperty]
    private ObservableCollection<UrlDisplayModel> _urls = new();

    [ObservableProperty]
    private ObservableCollection<UrlDisplayModel> _filteredUrls = new();

    [ObservableProperty]
    private UrlDisplayModel? _selectedUrlModel;

    [ObservableProperty]
    private ObservableCollection<UrlPropertyViewModel> _urlProperties = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _hasMoreItems;

    private List<UrlDisplayModel> _allUrls = [];
    private List<UrlDisplayModel> _currentFilteredSet = [];
    private int _currentPage = 0;
    private const int PageSize = 100;
    private string? _baseUrl;

    public string TabHeader => DisplayName;

    public async Task LoadUrlsAsync(IEnumerable<Url> urls, string? baseUrl = null)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
        _baseUrl = baseUrl;
        
        try
        {
            // Offload ALL expensive processing to background thread
            var urlList = urls.ToList();
            var (allUrls, sortedUrls, firstPage) = await Task.Run(() =>
            {
                var all = urlList.Select(url =>
                {
                    var isInternal = IsInternalUrl(url.Address, baseUrl);
                    return new UrlDisplayModel
                    {
                        Url = url,
                        IsInternal = isInternal,
                        Type = isInternal ? "Internal" : "External"
                    };
                }).ToList();
                
                // Sort in background thread (expensive with 20K URLs!)
                var sorted = all.OrderBy(u => u.Url.Address).ToList();
                
                // Get first page
                var page = sorted.Take(PageSize).ToList();
                
                return (all, sorted, page);
            });
            
            // Only update UI elements on UI thread (fast operation - just assignments)
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allUrls = allUrls;
                _currentFilteredSet = sortedUrls;
                _currentPage = 0;
                
                TotalCount = _currentFilteredSet.Count;
                FilteredUrls = new ObservableCollection<UrlDisplayModel>(firstPage);
                HasMoreItems = _currentFilteredSet.Count > PageSize;
            });
        }
        finally
        {
            // Must set IsLoading on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }
    
    private bool IsInternalUrl(string urlAddress, string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return true; // Default to internal if no base URL provided
            
        try
        {
            var targetUri = new Uri(urlAddress);
            var baseUri = new Uri(baseUrl);
            
            // Compare host (including subdomains)
            return string.Equals(targetUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // Default to internal on error
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value; // Suppress unused warning
        _ = ApplyFiltersAsync();
    }

    partial void OnSelectedUrlModelChanged(UrlDisplayModel? value)
    {
        if (value?.Url != null)
        {
            UpdateUrlProperties(value.Url);
        }
        else
        {
            UrlProperties.Clear();
        }
    }

    private async Task ApplyFiltersAsync()
    {
        // Show loading state
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
        
        try
        {
            var searchText = SearchText; // Capture for Task.Run
            
            // Do expensive filtering and sorting in background
            var (filteredSet, firstPage) = await Task.Run(() =>
            {
                var filtered = _allUrls.AsEnumerable();

                // Filter by search text
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filtered = filtered.Where(u =>
                        u.Url.Address.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        (u.Url.Title != null && u.Url.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                        (u.Url.ContentType != null && u.Url.ContentType.Contains(searchText, StringComparison.OrdinalIgnoreCase)));
                }

                // Store the filtered and sorted set
                var sorted = filtered.OrderBy(u => u.Url.Address).ToList();
                var page = sorted.Take(PageSize).ToList();
                
                return (sorted, page);
            });
            
            // Update UI on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _currentFilteredSet = filteredSet;
                _currentPage = 0;
                TotalCount = _currentFilteredSet.Count;
                
                FilteredUrls = new ObservableCollection<UrlDisplayModel>(firstPage);
                HasMoreItems = _currentFilteredSet.Count > PageSize;
            });
        }
        finally
        {
            // Must set IsLoading on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    [RelayCommand]
    private void LoadNextPage()
    {
        if (IsLoadingMore || !HasMoreItems) return;
        
        IsLoadingMore = true;
        _currentPage++;
        
        var nextItems = _currentFilteredSet
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .ToList();
        
        foreach (var item in nextItems)
        {
            FilteredUrls.Add(item);
        }
        
        HasMoreItems = (_currentPage + 1) * PageSize < _currentFilteredSet.Count;
        IsLoadingMore = false;
    }

    private void UpdateUrlProperties(Url url)
    {
        var properties = new List<UrlPropertyViewModel>();

        // Essential SEO (Top Priority)
        properties.Add(new UrlPropertyViewModel { Category = "Essential SEO", Key = "Status Code", Value = url.HttpStatus?.ToString() ?? "N/A" });
        if (!string.IsNullOrWhiteSpace(url.Title))
            properties.Add(new UrlPropertyViewModel { Category = "Essential SEO", Key = "Title", Value = WebUtility.HtmlDecode(url.Title) });
        if (!string.IsNullOrWhiteSpace(url.MetaDescription))
            properties.Add(new UrlPropertyViewModel { Category = "Essential SEO", Key = "Meta Description", Value = WebUtility.HtmlDecode(url.MetaDescription) });
        
        // Parse H1-H6 from RenderedHtml if available
        if (!string.IsNullOrWhiteSpace(url.RenderedHtml))
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(url.RenderedHtml);
                
                // H1 - List all vertically
                var h1Nodes = doc.DocumentNode.SelectNodes("//h1");
                if (h1Nodes?.Count > 0)
                {
                    var h1Texts = h1Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h1Texts.Count > 0)
                    {
                        var h1Value = string.Join(Environment.NewLine, h1Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Essential SEO", Key = $"H1 ({h1Nodes.Count})", Value = h1Value });
                    }
                }
                
                // H2-H6 in Content Structure - List all vertically
                var h2Nodes = doc.DocumentNode.SelectNodes("//h2");
                if (h2Nodes?.Count > 0)
                {
                    var h2Texts = h2Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h2Texts.Count > 0)
                    {
                        var h2Value = string.Join(Environment.NewLine, h2Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Content Structure", Key = $"H2 ({h2Nodes.Count})", Value = h2Value });
                    }
                }
                
                var h3Nodes = doc.DocumentNode.SelectNodes("//h3");
                if (h3Nodes?.Count > 0)
                {
                    var h3Texts = h3Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h3Texts.Count > 0)
                    {
                        var h3Value = string.Join(Environment.NewLine, h3Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Content Structure", Key = $"H3 ({h3Nodes.Count})", Value = h3Value });
                    }
                }
                
                var h4Nodes = doc.DocumentNode.SelectNodes("//h4");
                if (h4Nodes?.Count > 0)
                {
                    var h4Texts = h4Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h4Texts.Count > 0)
                    {
                        var h4Value = string.Join(Environment.NewLine, h4Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Content Structure", Key = $"H4 ({h4Nodes.Count})", Value = h4Value });
                    }
                }
                
                var h5Nodes = doc.DocumentNode.SelectNodes("//h5");
                if (h5Nodes?.Count > 0)
                {
                    var h5Texts = h5Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h5Texts.Count > 0)
                    {
                        var h5Value = string.Join(Environment.NewLine, h5Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Content Structure", Key = $"H5 ({h5Nodes.Count})", Value = h5Value });
                    }
                }
                
                var h6Nodes = doc.DocumentNode.SelectNodes("//h6");
                if (h6Nodes?.Count > 0)
                {
                    var h6Texts = h6Nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? "")).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (h6Texts.Count > 0)
                    {
                        var h6Value = string.Join(Environment.NewLine, h6Texts);
                        properties.Add(new UrlPropertyViewModel { Category = "Content Structure", Key = $"H6 ({h6Nodes.Count})", Value = h6Value });
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue
                Debug.WriteLine($"Error parsing HTML for headings: {ex.Message}");
            }
        }

        // Technical Information
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Address", Value = url.Address });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Content Type", Value = url.ContentType ?? "N/A" });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Content Length", Value = url.ContentLength?.ToString("N0") ?? "N/A" });
        // Display "External" for external URLs (marked with depth=-1) instead of showing the implementation detail
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Depth", Value = url.Depth == -1 ? "External" : url.Depth.ToString() });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Status", Value = url.Status.ToString() });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Scheme", Value = url.Scheme });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Host", Value = url.Host });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Path", Value = url.Path });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "First Seen", Value = url.FirstSeenUtc.ToString("yyyy-MM-dd HH:mm:ss") });
        properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Last Crawled", Value = url.LastCrawledUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A" });
        if (!string.IsNullOrWhiteSpace(url.HtmlLang))
            properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "HTML Lang", Value = url.HtmlLang });
        if (!string.IsNullOrWhiteSpace(url.ContentLanguageHeader))
            properties.Add(new UrlPropertyViewModel { Category = "Technical", Key = "Content-Language Header", Value = url.ContentLanguageHeader });

        // Canonical & Indexability
        if (!string.IsNullOrWhiteSpace(url.CanonicalHtml))
            properties.Add(new UrlPropertyViewModel { Category = "Canonical", Key = "Canonical (HTML)", Value = url.CanonicalHtml });
        if (!string.IsNullOrWhiteSpace(url.CanonicalHttp))
            properties.Add(new UrlPropertyViewModel { Category = "Canonical", Key = "Canonical (HTTP)", Value = url.CanonicalHttp });
        if (url.HasMultipleCanonicals)
            properties.Add(new UrlPropertyViewModel { Category = "Canonical", Key = "Has Multiple Canonicals", Value = "Yes" });
        if (url.HasCrossDomainCanonical)
            properties.Add(new UrlPropertyViewModel { Category = "Canonical", Key = "Has Cross-Domain Canonical", Value = "Yes" });
        if (!string.IsNullOrWhiteSpace(url.CanonicalIssues))
            properties.Add(new UrlPropertyViewModel { Category = "Canonical", Key = "Canonical Issues", Value = url.CanonicalIssues });

        // Robots Directives
        if (url.RobotsAllowed.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Robots Allowed", Value = url.RobotsAllowed.Value ? "Yes" : "No" });
        if (url.RobotsNoindex.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Noindex", Value = url.RobotsNoindex.Value ? "Yes" : "No" });
        if (url.RobotsNofollow.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Nofollow", Value = url.RobotsNofollow.Value ? "Yes" : "No" });
        if (url.RobotsNoarchive.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Noarchive", Value = url.RobotsNoarchive.Value ? "Yes" : "No" });
        if (url.RobotsNosnippet.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Nosnippet", Value = url.RobotsNosnippet.Value ? "Yes" : "No" });
        if (url.RobotsNoimageindex.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Noimageindex", Value = url.RobotsNoimageindex.Value ? "Yes" : "No" });
        if (!string.IsNullOrWhiteSpace(url.RobotsSource))
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Robots Source", Value = url.RobotsSource });
        if (!string.IsNullOrWhiteSpace(url.XRobotsTag))
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "X-Robots-Tag", Value = url.XRobotsTag });
        if (url.HasRobotsConflict)
            properties.Add(new UrlPropertyViewModel { Category = "Robots", Key = "Has Robots Conflict", Value = "Yes" });

        // Indexability
        if (url.IsIndexable.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Indexability", Key = "Is Indexable", Value = url.IsIndexable.Value ? "Yes" : "No" });

        // Redirects
        if (!string.IsNullOrWhiteSpace(url.RedirectTarget))
            properties.Add(new UrlPropertyViewModel { Category = "Redirects", Key = "Redirect Target", Value = url.RedirectTarget });
        if (url.IsRedirectLoop)
            properties.Add(new UrlPropertyViewModel { Category = "Redirects", Key = "Is Redirect Loop", Value = "Yes" });
        if (url.RedirectChainLength.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Redirects", Key = "Redirect Chain Length", Value = url.RedirectChainLength.ToString() });
        if (url.IsSoft404)
            properties.Add(new UrlPropertyViewModel { Category = "Redirects", Key = "Is Soft 404", Value = "Yes" });

        // Meta Refresh
        if (url.HasMetaRefresh)
        {
            properties.Add(new UrlPropertyViewModel { Category = "Meta Refresh", Key = "Has Meta Refresh", Value = "Yes" });
            if (url.MetaRefreshDelay.HasValue)
                properties.Add(new UrlPropertyViewModel { Category = "Meta Refresh", Key = "Meta Refresh Delay", Value = $"{url.MetaRefreshDelay}s" });
            if (!string.IsNullOrWhiteSpace(url.MetaRefreshTarget))
                properties.Add(new UrlPropertyViewModel { Category = "Meta Refresh", Key = "Meta Refresh Target", Value = url.MetaRefreshTarget });
        }

        // JavaScript Detection
        if (url.HasJsChanges)
        {
            properties.Add(new UrlPropertyViewModel { Category = "JavaScript", Key = "Has JS Changes", Value = "Yes" });
            if (!string.IsNullOrWhiteSpace(url.JsChangedElements))
                properties.Add(new UrlPropertyViewModel { Category = "JavaScript", Key = "JS Changed Elements", Value = url.JsChangedElements });
        }

        // HTTP Headers
        if (!string.IsNullOrWhiteSpace(url.CacheControl))
            properties.Add(new UrlPropertyViewModel { Category = "Headers", Key = "Cache-Control", Value = url.CacheControl });
        if (!string.IsNullOrWhiteSpace(url.Vary))
            properties.Add(new UrlPropertyViewModel { Category = "Headers", Key = "Vary", Value = url.Vary });
        if (!string.IsNullOrWhiteSpace(url.ContentEncoding))
            properties.Add(new UrlPropertyViewModel { Category = "Headers", Key = "Content-Encoding", Value = url.ContentEncoding });
        if (!string.IsNullOrWhiteSpace(url.LinkHeader))
            properties.Add(new UrlPropertyViewModel { Category = "Headers", Key = "Link Header", Value = url.LinkHeader });
        if (url.HasHsts)
            properties.Add(new UrlPropertyViewModel { Category = "Headers", Key = "Has HSTS", Value = "Yes" });

        // Content Analysis
        if (!string.IsNullOrWhiteSpace(url.ContentHash))
            properties.Add(new UrlPropertyViewModel { Category = "Content", Key = "Content Hash", Value = url.ContentHash });
        if (url.SimHash.HasValue)
            properties.Add(new UrlPropertyViewModel { Category = "Content", Key = "SimHash", Value = url.SimHash.ToString() });

        // Clear and repopulate the existing collection instead of creating a new one
        // This preserves the DataGrid's grouping configuration
        UrlProperties.Clear();
        foreach (var property in properties)
        {
            UrlProperties.Add(property);
        }
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (SelectedUrlModel?.Url.Address != null)
        {
            Clipboard.SetText(SelectedUrlModel.Url.Address);
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (SelectedUrlModel?.Url.Address != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedUrlModel.Url.Address,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Silently fail if browser cannot be opened
            }
        }
    }
}

