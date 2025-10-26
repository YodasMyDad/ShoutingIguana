using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace ShoutingIguana.Services;

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
public class ToastService : IToastService
{
    private readonly ObservableCollection<ToastViewModel> _toasts;
    private readonly Dispatcher _dispatcher;

    public ToastService()
    {
        _toasts = new ObservableCollection<ToastViewModel>();
        _dispatcher = Application.Current.Dispatcher;
    }

    public ObservableCollection<ToastViewModel> Toasts => _toasts;

    public void ShowSuccess(string title, string message = "", int durationMs = 4000)
    {
        ShowToast(title, message, ToastType.Success, durationMs);
    }

    public void ShowError(string title, string message = "", int durationMs = 6000)
    {
        ShowToast(title, message, ToastType.Error, durationMs);
    }

    public void ShowInfo(string title, string message = "", int durationMs = 3000)
    {
        ShowToast(title, message, ToastType.Info, durationMs);
    }

    public void ShowWarning(string title, string message = "", int durationMs = 5000)
    {
        ShowToast(title, message, ToastType.Warning, durationMs);
    }

    private void ShowToast(string title, string message, ToastType type, int durationMs)
    {
        _dispatcher.Invoke(() =>
        {
            var toast = new ToastViewModel(title, message, type);
            
            // Create timer for auto-dismiss
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            
            // Event handler for manual close - unsubscribe when toast is removed
            void OnCloseRequested()
            {
                timer.Stop();
                RemoveToast(toast);
                toast.CloseRequested -= OnCloseRequested;
            }
            
            // Event handler for timer - dispose timer when done
            void OnTimerTick(object? sender, EventArgs e)
            {
                timer.Stop();
                timer.Tick -= OnTimerTick;
                RemoveToast(toast);
                toast.CloseRequested -= OnCloseRequested;
            }
            
            toast.CloseRequested += OnCloseRequested;
            timer.Tick += OnTimerTick;
            
            _toasts.Add(toast);
            timer.Start();
        });
    }

    private void RemoveToast(ToastViewModel toast)
    {
        _dispatcher.Invoke(() =>
        {
            if (_toasts.Contains(toast))
            {
                _toasts.Remove(toast);
            }
        });
    }
}

/// <summary>
/// Toast notification type.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Info,
    Warning
}

/// <summary>
/// ViewModel for a toast notification.
/// </summary>
public partial class ToastViewModel
{
    public ToastViewModel(string title, string message, ToastType type)
    {
        Title = title;
        Message = message;
        Type = type;
        
        Icon = type switch
        {
            ToastType.Success => "\uE73E", // Icon.Success
            ToastType.Error => "\uE783",   // Icon.Error
            ToastType.Info => "\uE946",     // Icon.Info
            ToastType.Warning => "\uE7BA", // Icon.Warning
            _ => "\uE946"
        };
    }

    public string Title { get; }
    public string Message { get; }
    public ToastType Type { get; }
    public string Icon { get; }

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }
}


