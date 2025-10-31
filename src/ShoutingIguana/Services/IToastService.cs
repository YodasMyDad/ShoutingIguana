using System.Collections.ObjectModel;
using ShoutingIguana.Services.Models;

namespace ShoutingIguana.Services;

/// <summary>
/// Service for displaying non-intrusive toast notifications to the user.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Gets the collection of active toast notifications for UI binding.
    /// </summary>
    ObservableCollection<ToastViewModel> Toasts { get; }

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    void ShowSuccess(string title, string message = "", int durationMs = 4000);

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    void ShowError(string title, string message = "", int durationMs = 6000);

    /// <summary>
    /// Shows an informational toast notification.
    /// </summary>
    void ShowInfo(string title, string message = "", int durationMs = 3000);

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    void ShowWarning(string title, string message = "", int durationMs = 5000);
}

