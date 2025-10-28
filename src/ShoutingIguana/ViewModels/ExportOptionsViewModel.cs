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
    
    public bool DialogResult { get; private set; }
    
    public ExportOptionsViewModel(Window dialog)
    {
        _dialog = dialog;
    }
    
    [RelayCommand]
    private void Ok()
    {
        DialogResult = true;
        _dialog.Close();
    }
    
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        _dialog.Close();
    }
}

