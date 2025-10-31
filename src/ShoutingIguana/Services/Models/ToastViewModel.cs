using System;
using CommunityToolkit.Mvvm.Input;

namespace ShoutingIguana.Services.Models;

/// <summary>
/// ViewModel for a toast notification.
/// </summary>
public partial class ToastViewModel(string title, string message, ToastType type)
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public ToastType Type { get; } = type;
    public string Icon { get; } = type switch
    {
        ToastType.Success => "\uE73E", // Icon.Success
        ToastType.Error => "\uE783",   // Icon.Error
        ToastType.Info => "\uE946",     // Icon.Info
        ToastType.Warning => "\uE7BA", // Icon.Warning
        _ => "\uE946"
    };

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }
}

