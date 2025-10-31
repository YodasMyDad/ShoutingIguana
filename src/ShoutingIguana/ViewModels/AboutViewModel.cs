using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.ViewModels;

public partial class AboutViewModel(ILogger<AboutViewModel> logger, Window dialog) : ObservableObject
{
    private readonly ILogger<AboutViewModel> _logger = logger;
    private readonly Window _dialog = dialog;

    [ObservableProperty]
    private string _buildDate = DateTime.Now.ToString("MMMM dd, yyyy");

    [RelayCommand]
    private void Close()
    {
        _dialog.Close();
    }
}

