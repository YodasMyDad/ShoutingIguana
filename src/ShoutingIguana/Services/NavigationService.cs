using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Services;

public class NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService> logger) : INavigationService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<NavigationService> _logger = logger;
    private UserControl? _currentView;

    public event EventHandler<UserControl>? NavigationRequested;

    public void NavigateTo<T>() where T : UserControl
    {
        var view = ActivatorUtilities.CreateInstance<T>(_serviceProvider);
        NavigateTo(view);
    }

    public void NavigateTo(UserControl view)
    {
        // Dispose previous view's DataContext if it implements IDisposable
        if (_currentView?.DataContext is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
                _logger.LogDebug("Disposed previous view's DataContext: {Type}", disposable.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing previous view's DataContext");
            }
        }
        
        _currentView = view;
        NavigationRequested?.Invoke(this, view);
    }
}

