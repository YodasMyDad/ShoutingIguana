using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class ListModeImportViewModel(
    ILogger<ListModeImportViewModel> logger,
    IListModeService listModeService,
    IToastService toastService,
    int projectId,
    Window dialog) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _csvFilePath = string.Empty;

    [ObservableProperty]
    private int _priority = 1000;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _importComplete;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public bool CanImport => !string.IsNullOrWhiteSpace(CsvFilePath) && !IsImporting;

    [RelayCommand]
    private void BrowseFile()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select CSV File",
            Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FilterIndex = 1,
            CheckFileExists = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            CsvFilePath = openFileDialog.FileName;
            logger.LogInformation("Selected file: {FilePath}", CsvFilePath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsImporting = true;
        ImportComplete = false;
        ImportStatus = "Starting import...";

        try
        {
            var progress = new Progress<string>(status => ImportStatus = status);

            var result = await listModeService.ImportUrlListAsync(
                projectId,
                CsvFilePath,
                Priority,
                progress);

            if (result.Success)
            {
                ResultMessage = $"✓ Imported {result.ImportedCount} URLs ({result.SkippedCount} skipped, {result.InvalidCount} invalid)";
                ImportComplete = true;
                
                toastService.ShowSuccess("Import Complete", ResultMessage);
                logger.LogInformation("List-mode import successful: {Result}", ResultMessage);

                // Close dialog after short delay
                await Task.Delay(1500);
                dialog.DialogResult = true;
                dialog.Close();
            }
            else
            {
                toastService.ShowError("Import Failed", result.ErrorMessage ?? "Unknown error");
                ResultMessage = $"✗ Import failed: {result.ErrorMessage}";
                
                if (result.Errors.Count > 0)
                {
                    logger.LogWarning("Import errors: {Errors}", string.Join("; ", result.Errors.Take(5)));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during import");
            toastService.ShowError("Import Failed", ex.Message);
            ResultMessage = $"✗ Error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
            ImportStatus = string.Empty;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        dialog.DialogResult = false;
        dialog.Close();
    }
}

