using System;
using System.Windows;

namespace ShoutingIguana.Services;

/// <summary>
/// Service for updating the application status bar message.
/// Thread-safe implementation that dispatches updates to the UI thread.
/// </summary>
public class StatusService : IStatusService
{
    private readonly object _lock = new();

    /// <inheritdoc/>
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc/>
    public void UpdateStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // Ensure we're on the UI thread when raising the event
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            RaiseStatusChanged(message);
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(() => RaiseStatusChanged(message));
        }
    }

    private void RaiseStatusChanged(string message)
    {
        lock (_lock)
        {
            StatusChanged?.Invoke(this, message);
        }
    }
}

