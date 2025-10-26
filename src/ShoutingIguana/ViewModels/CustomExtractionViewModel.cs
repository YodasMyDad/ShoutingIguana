using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class CustomExtractionViewModel(
    ILogger<CustomExtractionViewModel> logger,
    ICustomExtractionService customExtractionService,
    IToastService toastService,
    int projectId,
    Window dialog) : ObservableObject
{
    private readonly ILogger<CustomExtractionViewModel> _logger = logger;
    private readonly ICustomExtractionService _customExtractionService = customExtractionService;
    private readonly IToastService _toastService = toastService;
    private readonly int _projectId = projectId;
    private readonly Window _dialog = dialog;

    [ObservableProperty]
    private ObservableCollection<CustomExtractionRule> _rules = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private CustomExtractionRule? _selectedRule;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand))]
    private string _editingName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand))]
    private string _editingFieldName = string.Empty;

    [ObservableProperty]
    private int _editingSelectorType; // 0=CSS, 1=XPath, 2=Regex

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand))]
    private string _editingSelector = string.Empty;

    [ObservableProperty]
    private bool _editingIsEnabled = true;

    [ObservableProperty]
    private int _editingRuleId;

    public bool CanEdit => SelectedRule != null;
    public bool CanDelete => SelectedRule != null;
    public bool CanSaveRule => !string.IsNullOrWhiteSpace(EditingName) 
                                && !string.IsNullOrWhiteSpace(EditingFieldName) 
                                && !string.IsNullOrWhiteSpace(EditingSelector);

    public async Task LoadRulesAsync()
    {
        IsLoading = true;
        try
        {
            var rules = await _customExtractionService.GetRulesByProjectIdAsync(_projectId);
            Rules = new ObservableCollection<CustomExtractionRule>(rules);
            _logger.LogInformation("Loaded {Count} extraction rules", rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading extraction rules");
            _toastService.ShowError("Error", "Failed to load extraction rules");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddRule()
    {
        IsEditing = true;
        EditingRuleId = 0;
        EditingName = string.Empty;
        EditingFieldName = string.Empty;
        EditingSelectorType = 0; // CSS by default
        EditingSelector = string.Empty;
        EditingIsEnabled = true;
        
        _logger.LogDebug("Starting new rule creation");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void EditRule()
    {
        if (SelectedRule == null) return;

        IsEditing = true;
        EditingRuleId = SelectedRule.Id;
        EditingName = SelectedRule.Name;
        EditingFieldName = SelectedRule.FieldName;
        EditingSelectorType = SelectedRule.SelectorType;
        EditingSelector = SelectedRule.Selector;
        EditingIsEnabled = SelectedRule.IsEnabled;
        
        _logger.LogDebug("Editing rule: {RuleName}", SelectedRule.Name);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteRuleAsync()
    {
        if (SelectedRule == null) return;

        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                $"Are you sure you want to delete the rule '{SelectedRule.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question));

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _customExtractionService.DeleteRuleAsync(SelectedRule.Id);
                Rules.Remove(SelectedRule);
                SelectedRule = null;
                
                _toastService.ShowSuccess("Deleted", "Rule deleted successfully");
                _logger.LogInformation("Deleted extraction rule");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting extraction rule");
                _toastService.ShowError("Error", "Failed to delete rule");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveRule))]
    private async Task SaveRuleAsync()
    {
        try
        {
            var rule = new CustomExtractionRule
            {
                Id = EditingRuleId,
                ProjectId = _projectId,
                Name = EditingName.Trim(),
                FieldName = EditingFieldName.Trim(),
                SelectorType = EditingSelectorType,
                Selector = EditingSelector.Trim(),
                IsEnabled = EditingIsEnabled
            };

            var savedRule = await _customExtractionService.SaveRuleAsync(rule);

            // Update UI
            if (EditingRuleId == 0)
            {
                // New rule
                Rules.Add(savedRule);
                _toastService.ShowSuccess("Created", "Rule created successfully");
            }
            else
            {
                // Update existing
                var existing = Rules.FirstOrDefault(r => r.Id == EditingRuleId);
                if (existing != null)
                {
                    var index = Rules.IndexOf(existing);
                    Rules[index] = savedRule;
                }
                _toastService.ShowSuccess("Updated", "Rule updated successfully");
            }

            IsEditing = false;
            _logger.LogInformation("Saved extraction rule: {RuleName}", savedRule.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving extraction rule");
            _toastService.ShowError("Error", "Failed to save rule");
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        _logger.LogDebug("Cancelled rule editing");
    }

    [RelayCommand]
    private void Close()
    {
        _dialog.DialogResult = true;
        _dialog.Close();
    }
}

