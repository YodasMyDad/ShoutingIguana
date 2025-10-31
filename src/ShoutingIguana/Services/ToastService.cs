using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using ShoutingIguana.Services.Models;

namespace ShoutingIguana.Services;

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
public class ToastService() : IToastService
{
    private readonly ObservableCollection<ToastViewModel> _toasts = [];
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

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

