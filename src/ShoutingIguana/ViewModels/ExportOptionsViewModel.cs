using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the export options dialog.
/// </summary>
public partial class ExportOptionsViewModel : ObservableObject
{
    private readonly Window _dialog;
    
    [ObservableProperty]
    private bool _includeTechnicalMetadata;
    
    [ObservableProperty]
    private string _exportFormat = "Excel";
    
    public ExportOptionsViewModel(Window dialog)
    {
        _dialog = dialog;
    }
    
    [RelayCommand]
    private void Ok()
    {
        _dialog.DialogResult = true;
    }
    
    [RelayCommand]
    private void Cancel()
    {
        _dialog.DialogResult = false;
    }
}

