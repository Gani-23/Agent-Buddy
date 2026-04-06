using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using AgentBuddy.Models;

namespace AgentBuddy.ViewModels;

public sealed class ConfirmListDialogViewModel : ViewModelBase
{
    private string _searchQuery = string.Empty;
    private int _filterMode;
    private int _totalAccounts;

    public ConfirmListDialogViewModel(ConfirmListDialogRequest request)
    {
        Title = request.Title;
        Message = request.Message;
        YesText = request.YesText;
        NoText = request.NoText;

        Items = new ObservableCollection<ConfirmListItem>(request.Items);
        FilteredItems = new ObservableCollection<ConfirmListItem>(request.Items);
        UpdateTotals();
    }

    public string Title { get; }
    public string Message { get; }
    public string YesText { get; }
    public string NoText { get; }

    public ObservableCollection<ConfirmListItem> Items { get; }
    public ObservableCollection<ConfirmListItem> FilteredItems { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilter();
        }
    }

    public int FilterMode
    {
        get => _filterMode;
        set
        {
            if (_filterMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _filterMode, value);
            ApplyFilter();
        }
    }

    public int TotalAccounts
    {
        get => _totalAccounts;
        private set => this.RaiseAndSetIfChanged(ref _totalAccounts, value);
    }

    private void ApplyFilter()
    {
        var query = (SearchQuery ?? string.Empty).Trim();
        var filtered = Items
            .Where(item =>
                _filterMode switch
                {
                    1 => item.AccountCount > 0,
                    2 => item.AccountCount == 0,
                    _ => true
                })
            .Where(item =>
                string.IsNullOrWhiteSpace(query) ||
                item.ReferenceNumber.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                item.ListLabel.Contains(query, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilteredItems.Clear();
        foreach (var item in filtered)
        {
            FilteredItems.Add(item);
        }

        UpdateTotals();
    }

    private void UpdateTotals()
    {
        TotalAccounts = FilteredItems.Sum(item => item.AccountCount);
        this.RaisePropertyChanged(nameof(FilteredCount));
    }

    public int FilteredCount => FilteredItems.Count;
}
