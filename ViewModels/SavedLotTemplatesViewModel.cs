using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using AgentBuddy.Models;

namespace AgentBuddy.ViewModels;

public sealed class SavedLotTemplatesViewModel : ViewModelBase
{
    private readonly ObservableCollection<SavedLotTemplate> _allTemplates;
    private SavedLotTemplate? _selectedTemplate;
    private string _searchText = string.Empty;
    private string _newTemplateName = string.Empty;
    private ListPanelViewModel? _selectedOpenList;
    private string _statusMessage = string.Empty;

    public SavedLotTemplatesViewModel(
        IEnumerable<SavedLotTemplate> templates,
        IEnumerable<ListPanelViewModel> openLists,
        ListPanelViewModel? activeList = null)
    {
        _allTemplates = new ObservableCollection<SavedLotTemplate>(templates ?? Enumerable.Empty<SavedLotTemplate>());
        FilteredTemplates = new ObservableCollection<SavedLotTemplate>(_allTemplates);
        OpenLists = new ObservableCollection<ListPanelViewModel>(openLists ?? Enumerable.Empty<ListPanelViewModel>());
        _selectedOpenList = activeList ?? OpenLists.FirstOrDefault();
        _selectedTemplate = FilteredTemplates.FirstOrDefault();

        if (_selectedOpenList != null && _selectedOpenList.Count > 0)
        {
            _newTemplateName = $"{_selectedOpenList.Name} ({_selectedOpenList.Count} accounts)";
        }
        else
        {
            _newTemplateName = string.Empty;
        }

        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;
    public event EventHandler<SavedLotTemplate>? LoadIntoNewListRequested;
    public event EventHandler<(SavedLotTemplate Template, ListPanelViewModel TargetList)>? LoadIntoListRequested;
    public event EventHandler<SavedLotTemplate>? TemplateDeleted;
    public event EventHandler<(ListPanelViewModel SourceList, string TemplateName)>? SaveTemplateRequested;

    public ObservableCollection<SavedLotTemplate> FilteredTemplates { get; }
    public ObservableCollection<ListPanelViewModel> OpenLists { get; }

    public bool HasOpenLists => OpenLists.Count > 0;
    public bool HasTemplates => FilteredTemplates.Count > 0;

    public SavedLotTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTemplate, value);
            this.RaisePropertyChanged(nameof(HasSelectedTemplate));
            this.RaisePropertyChanged(nameof(SelectedTemplateItemCountText));
        }
    }

    public bool HasSelectedTemplate => SelectedTemplate != null;

    public string SelectedTemplateItemCountText =>
        SelectedTemplate != null ? $"{SelectedTemplate.AccountCount} Accounts" : string.Empty;

    public bool CanSaveCurrentList => SelectedOpenList != null && SelectedOpenList.Count > 0;

    public string SaveButtonTooltip => CanSaveCurrentList
        ? "Save this list's accounts as a template"
        : "Select an open list that contains accounts to save as a template";

    public ListPanelViewModel? SelectedOpenList
    {
        get => _selectedOpenList;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedOpenList, value);
            this.RaisePropertyChanged(nameof(CanSaveCurrentList));
            this.RaisePropertyChanged(nameof(SaveButtonTooltip));
            if (value != null && value.Count > 0 && string.IsNullOrWhiteSpace(NewTemplateName))
            {
                NewTemplateName = $"{value.Name} ({value.Count} accounts)";
            }
            else if (value != null && value.Count == 0 && (NewTemplateName?.Contains("(0 accounts)") == true || string.IsNullOrWhiteSpace(NewTemplateName)))
            {
                NewTemplateName = string.Empty;
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ApplyFilter();
        }
    }

    public string NewTemplateName
    {
        get => _newTemplateName;
        set => this.RaiseAndSetIfChanged(ref _newTemplateName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public void ApplyFilter()
    {
        FilteredTemplates.Clear();
        var query = (SearchText ?? string.Empty).Trim().ToLowerInvariant();

        foreach (var t in _allTemplates)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                t.Name.ToLowerInvariant().Contains(query) ||
                (t.Description ?? string.Empty).ToLowerInvariant().Contains(query) ||
                t.Items.Any(i => i.AccountNo.Contains(query)))
            {
                FilteredTemplates.Add(t);
            }
        }

        if (SelectedTemplate == null || !FilteredTemplates.Contains(SelectedTemplate))
        {
            SelectedTemplate = FilteredTemplates.FirstOrDefault();
        }

        this.RaisePropertyChanged(nameof(HasTemplates));
    }

    public void RequestSaveCurrentList()
    {
        if (SelectedOpenList == null)
        {
            StatusMessage = "Select an open list to save.";
            return;
        }

        if (SelectedOpenList.Count == 0)
        {
            StatusMessage = "Selected list has no accounts to save.";
            return;
        }

        var name = (NewTemplateName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{SelectedOpenList.Name} ({SelectedOpenList.Count} accounts)";
        }

        SaveTemplateRequested?.Invoke(this, (SelectedOpenList, name));
    }

    public void RequestLoadIntoNewList()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Select a template to load.";
            return;
        }

        LoadIntoNewListRequested?.Invoke(this, SelectedTemplate);
    }

    public void RequestLoadIntoSelectedList()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Select a template to load.";
            return;
        }

        if (SelectedOpenList == null)
        {
            StatusMessage = "Select a target list to load into.";
            return;
        }

        LoadIntoListRequested?.Invoke(this, (SelectedTemplate, SelectedOpenList));
    }

    public void RequestDeleteTemplate()
    {
        if (SelectedTemplate == null)
        {
            return;
        }

        var toDelete = SelectedTemplate;
        _allTemplates.Remove(toDelete);
        FilteredTemplates.Remove(toDelete);
        SelectedTemplate = FilteredTemplates.FirstOrDefault();
        this.RaisePropertyChanged(nameof(HasTemplates));

        TemplateDeleted?.Invoke(this, toDelete);
    }

    public void AddTemplate(SavedLotTemplate template)
    {
        _allTemplates.Insert(0, template);
        ApplyFilter();
        SelectedTemplate = template;
        StatusMessage = $"Saved template '{template.Name}'.";
    }
}
