using System;

namespace ShoutingIguana.Services;

/// <summary>
/// Service for updating the application status bar message.
/// </summary>
public interface IStatusService
{
    /// <summary>
    /// Event raised when the status message changes.
    /// </summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Updates the status message.
    /// This method is thread-safe and will dispatch to the UI thread automatically.
    /// </summary>
    /// <param name="message">The status message to display.</param>
    void UpdateStatus(string message);
}

