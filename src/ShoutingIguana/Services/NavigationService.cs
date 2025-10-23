using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace ShoutingIguana.Services;

public class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public event EventHandler<UserControl>? NavigationRequested;

    public void NavigateTo<T>() where T : UserControl
    {
        var view = ActivatorUtilities.CreateInstance<T>(_serviceProvider);
        NavigateTo(view);
    }

    public void NavigateTo(UserControl view)
    {
        NavigationRequested?.Invoke(this, view);
    }
}

