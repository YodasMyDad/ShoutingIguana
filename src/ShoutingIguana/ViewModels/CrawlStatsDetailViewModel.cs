using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

public partial class CrawlStatsDetailViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 100;

    private static readonly IReadOnlyCollection<UrlStatus> CrawledStatuses =
        [UrlStatus.Completed, UrlStatus.Failed];

    private readonly ILogger<CrawlStatsDetailViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICrawlEngine _crawlEngine;

    private int _projectId;
    private int _currentPage;
    private int _lastKnownTotal;
    // Serialises every database-facing load so scroll-triggered paging
    // and ProgressUpdated-triggered refreshes can't interleave and corrupt
    // the displayed collections.
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _disposed;

    [ObservableProperty]
    private CrawlStatsKind _kind;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _iconGlyph = string.Empty;

    [ObservableProperty]
    private Brush _accentBrush = Brushes.SteelBlue;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _hasMoreItems;

    [ObservableProperty]
    private bool _isUrlView;

    [ObservableProperty]
    private bool _isQueueView;

    public ObservableCollection<Url> Urls { get; } = [];
    public ObservableCollection<CrawlQueueItem> QueueItems { get; } = [];

    public CrawlStatsDetailViewModel(
        ILogger<CrawlStatsDetailViewModel> logger,
        IServiceProvider serviceProvider,
        ICrawlEngine crawlEngine)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _crawlEngine = crawlEngine;
    }

    public async Task InitializeAsync(CrawlStatsKind kind, int projectId)
    {
        Kind = kind;
        _projectId = projectId;

        ConfigureForKind(kind);

        IsLoading = true;
        try
        {
            await _loadGate.WaitAsync().ConfigureAwait(true);
            try
            {
                var total = await FetchCountAsync().ConfigureAwait(true);
                TotalCount = total;
                _lastKnownTotal = total;

                Urls.Clear();
                QueueItems.Clear();
                _currentPage = 0;

                var page = await FetchPageAsync(0).ConfigureAwait(true);
                AppendToCollection(page);

                HasMoreItems = (_currentPage + 1) * PageSize < TotalCount;
            }
            finally
            {
                _loadGate.Release();
            }
        }
        finally
        {
            IsLoading = false;
        }

        _crawlEngine.ProgressUpdated += OnProgressUpdated;
    }

    private void ConfigureForKind(CrawlStatsKind kind)
    {
        switch (kind)
        {
            case CrawlStatsKind.UrlsCrawled:
                Title = "URLs Crawled";
                IconGlyph = "\uE73E"; // Icon.CheckCircle
                AccentBrush = ResolveBrush("SuccessBrush");
                IsUrlView = true;
                IsQueueView = false;
                break;
            case CrawlStatsKind.TotalDiscovered:
                Title = "Total Discovered";
                IconGlyph = "\uE71B"; // Icon.Link
                AccentBrush = ResolveBrush("InfoBrush");
                IsUrlView = true;
                IsQueueView = false;
                break;
            case CrawlStatsKind.Queue:
                Title = "Queue";
                IconGlyph = "\uE8FD"; // Icon.List
                AccentBrush = ResolveBrush("PrimaryBrush");
                IsUrlView = false;
                IsQueueView = true;
                break;
            case CrawlStatsKind.Errors:
                Title = "Errors";
                IconGlyph = "\uE7BA"; // Icon.AlertCircle
                AccentBrush = ResolveBrush("ErrorBrush");
                IsUrlView = true;
                IsQueueView = false;
                break;
        }
    }

    // Each database call runs inside its own DI scope so the scoped
    // IShoutingIguanaDbContext is short-lived — matches the convention
    // documented in ProjectDbContextProvider and used elsewhere in the app
    // (see FindingsViewModel.LoadFindingsAsync).
    private async Task<int> FetchCountAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var urlRepo = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
        var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();

        return Kind switch
        {
            CrawlStatsKind.UrlsCrawled => await urlRepo.CountByStatusesAsync(_projectId, CrawledStatuses).ConfigureAwait(false),
            CrawlStatsKind.TotalDiscovered => await urlRepo.CountByProjectIdAsync(_projectId).ConfigureAwait(false),
            CrawlStatsKind.Queue => await queueRepo.CountQueuedAsync(_projectId).ConfigureAwait(false),
            CrawlStatsKind.Errors => await urlRepo.CountErrorsAsync(_projectId).ConfigureAwait(false),
            _ => 0
        };
    }

    private async Task<object> FetchPageAsync(int page)
    {
        var skip = page * PageSize;
        using var scope = _serviceProvider.CreateScope();
        var urlRepo = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
        var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();

        return Kind switch
        {
            CrawlStatsKind.UrlsCrawled => await urlRepo.GetPagedByStatusesAsync(_projectId, CrawledStatuses, skip, PageSize).ConfigureAwait(false),
            CrawlStatsKind.TotalDiscovered => await urlRepo.GetPagedByProjectIdAsync(_projectId, skip, PageSize).ConfigureAwait(false),
            CrawlStatsKind.Queue => await queueRepo.GetPagedQueuedItemsAsync(_projectId, skip, PageSize).ConfigureAwait(false),
            CrawlStatsKind.Errors => await urlRepo.GetPagedErrorsAsync(_projectId, skip, PageSize).ConfigureAwait(false),
            _ => new List<Url>()
        };
    }

    [RelayCommand]
    private async Task LoadNextPageAsync()
    {
        if (_disposed || IsLoadingMore || !HasMoreItems)
        {
            return;
        }

        // Non-blocking: if another load is already running, skip this request.
        // Scroll events fire frequently and queuing them all would thrash.
        if (!await _loadGate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            IsLoadingMore = true;
            var nextPage = _currentPage + 1;

            var page = await FetchPageAsync(nextPage).ConfigureAwait(true);
            AppendToCollection(page);

            _currentPage = nextPage;
            HasMoreItems = (_currentPage + 1) * PageSize < TotalCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load next page for {Kind}", Kind);
        }
        finally
        {
            IsLoadingMore = false;
            _loadGate.Release();
        }
    }

    private void AppendToCollection(object page)
    {
        switch (page)
        {
            case List<Url> urls:
                foreach (var u in urls)
                {
                    Urls.Add(u);
                }
                break;
            case List<CrawlQueueItem> queue:
                foreach (var q in queue)
                {
                    QueueItems.Add(q);
                }
                break;
        }
    }

    private void OnProgressUpdated(object? sender, CrawlProgressEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            if (_disposed)
            {
                return;
            }

            if (!await _loadGate.WaitAsync(0).ConfigureAwait(true))
            {
                // Another load is in flight — next tick will pick up the change.
                return;
            }

            try
            {
                var newTotal = await FetchCountAsync().ConfigureAwait(true);
                if (newTotal == _lastKnownTotal)
                {
                    return;
                }

                TotalCount = newTotal;

                // Queue items disappear as workers dequeue; Url-backed kinds add
                // or reclassify rows as the crawl progresses. Re-loading every
                // page the user has scrolled into (100 rows each) gives a fresh,
                // correct snapshot without disturbing scroll position much.
                await ReloadVisibleAsync(_currentPage + 1).ConfigureAwait(true);

                _lastKnownTotal = newTotal;
                HasMoreItems = (_currentPage + 1) * PageSize < TotalCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live refresh failed for {Kind}", Kind);
            }
            finally
            {
                _loadGate.Release();
            }
        });
    }

    private async Task ReloadVisibleAsync(int pagesToLoad)
    {
        if (pagesToLoad <= 0)
        {
            pagesToLoad = 1;
        }

        var allUrls = new List<Url>();
        var allQueue = new List<CrawlQueueItem>();

        for (int p = 0; p < pagesToLoad; p++)
        {
            var page = await FetchPageAsync(p).ConfigureAwait(true);
            if (page is List<Url> urls)
            {
                allUrls.AddRange(urls);
            }
            else if (page is List<CrawlQueueItem> queue)
            {
                allQueue.AddRange(queue);
            }
        }

        if (IsUrlView)
        {
            Urls.Clear();
            foreach (var u in allUrls)
            {
                Urls.Add(u);
            }
            _currentPage = Math.Max(0, (allUrls.Count + PageSize - 1) / PageSize - 1);
        }
        else if (IsQueueView)
        {
            QueueItems.Clear();
            foreach (var q in allQueue)
            {
                QueueItems.Add(q);
            }
            _currentPage = Math.Max(0, (allQueue.Count + PageSize - 1) / PageSize - 1);
        }
    }

    private static Brush ResolveBrush(string key)
    {
        return Application.Current?.TryFindResource(key) is Brush brush
            ? brush
            : Brushes.SteelBlue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _crawlEngine.ProgressUpdated -= OnProgressUpdated;
        // Intentionally not disposing _loadGate: a BeginInvoke continuation
        // might still be mid-flight on the dispatcher and would throw
        // ObjectDisposedException on Release. GC handles it.
    }
}
