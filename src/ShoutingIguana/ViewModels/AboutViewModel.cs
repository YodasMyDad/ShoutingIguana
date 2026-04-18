using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.ViewModels;

public partial class AboutViewModel(ILogger<AboutViewModel> logger, Window dialog) : ObservableObject
{
    [ObservableProperty]
    private string _buildDate = DateTime.Now.ToString("MMMM dd, yyyy");

    [RelayCommand]
    private void Close()
    {
        dialog.Close();
    }
}

