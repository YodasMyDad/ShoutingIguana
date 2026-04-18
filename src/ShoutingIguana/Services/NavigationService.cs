using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Services;

public class NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService> logger) : INavigationService
{
    private UserControl? _currentView;

    public event EventHandler<UserControl>? NavigationRequested;

    public void NavigateTo<T>() where T : UserControl
    {
        var view = ActivatorUtilities.CreateInstance<T>(serviceProvider);
        NavigateTo(view);
    }

    public void NavigateTo(UserControl view)
    {
        // Dispose previous view's DataContext if it implements IDisposable
        if (_currentView?.DataContext is IDisposable disposable)
        {
            try
            {
                var typeName = disposable.GetType().FullName;
                disposable.Dispose();
                logger.LogDebug("Disposed previous view's DataContext of type {Type}", typeName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing previous view's DataContext");
            }
        }
        
        _currentView = view;
        NavigationRequested?.Invoke(this, view);
    }
}

