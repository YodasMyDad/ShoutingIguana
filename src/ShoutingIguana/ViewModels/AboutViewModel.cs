using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ILogger<AboutViewModel> _logger;
    private readonly Window _dialog;

    [ObservableProperty]
    private string _buildDate = DateTime.Now.ToString("MMMM dd, yyyy");

    public AboutViewModel(ILogger<AboutViewModel> logger, Window dialog)
    {
        _logger = logger;
        _dialog = dialog;
    }

    [RelayCommand]
    private void Close()
    {
        _dialog.Close();
    }
}

