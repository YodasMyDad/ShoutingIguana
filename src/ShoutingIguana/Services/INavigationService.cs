using System;
using System.Windows.Controls;

namespace ShoutingIguana.Services;

public interface INavigationService
{
    event EventHandler<UserControl>? NavigationRequested;
    void NavigateTo<T>() where T : UserControl;
    void NavigateTo(UserControl view);
}

