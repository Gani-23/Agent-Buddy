using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using AgentBuddy.Models;
using AgentBuddy.Services;
using System;

namespace AgentBuddy.ViewModels;

/// <summary>
/// SearchableAccount wraps RDAccount with selection state
/// </summary>
public class SearchableAccount : ReactiveObject
{
    private bool _isSelected;

    public RDAccount Account { get; set; }
    public bool IsDueSoon { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public SearchableAccount(RDAccount account, bool isDueSoon = false)
    {
        Account = account;
        IsDueSoon = isDueSoon;
    }
}

/// <summary>
/// ViewModel for account search dialog
/// </summary>
public class AccountSearchViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly ValidationService _validationService;
    private readonly List<string> _existingAccountsInLists;

    private string _searchQuery = string.Empty;
    private int _filterDueSoon = 0; // 0 = All, 1 = Due Soon Only
    private int _sortBy = 0; // 0 = Number, 1 = Name, 2 = Amount, 3 = Due Date
    private bool _isDarkTheme;
    private string _listName = string.Empty;
    private int _selectedCount;

    public AccountSearchViewModel(
        DatabaseService databaseService,
        ValidationService validationService,
        List<string> existingAccountsInLists,
        string listName,
        bool isDarkTheme = false)
    {
        _databaseService = databaseService;
        _validationService = validationService;
        _existingAccountsInLists = existingAccountsInLists;
        _listName = listName;
        _isDarkTheme = isDarkTheme;

        SearchResults = new ObservableCollection<SearchableAccount>();

        SearchCommand = ReactiveCommand.CreateFromTask(SearchAccountsAsync);
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);
        SelectAllCommand = ReactiveCommand.Create(SelectAll);
        SelectNoneCommand = ReactiveCommand.Create(SelectNone);
        SelectFilteredCommand = ReactiveCommand.Create(SelectFiltered);

        // Load initial results
        _ = SearchAccountsAsync();
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
    }

    public int FilterDueSoon
    {
        get => _filterDueSoon;
        set
        {
            this.RaiseAndSetIfChanged(ref _filterDueSoon, value);
            _ = SearchAccountsAsync();
        }
    }

    public int SortBy
    {
        get => _sortBy;
        set
        {
            this.RaiseAndSetIfChanged(ref _sortBy, value);
            ApplySorting();
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string ListName
    {
        get => _listName;
        set => this.RaiseAndSetIfChanged(ref _listName, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    public bool HasSelection => SelectedCount > 0;

    public bool AddSelectedAccounts { get; set; }

    public ObservableCollection<SearchableAccount> SearchResults { get; }

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectFilteredCommand { get; }

    private async Task SearchAccountsAsync()
    {
        var accounts = await _validationService.FilterAccountsAsync(
            searchQuery: string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
            isDueSoon: FilterDueSoon == 1 ? true : null,
            maxResults: 200
        );

        // Filter out accounts already in lists
        accounts = accounts.Where(a => !_existingAccountsInLists.Contains(a.AccountNo)).ToList();

        SearchResults.Clear();
        foreach (var account in accounts)
        {
            var isDueSoon = account.IsDueWithinDays(30);
            var searchableAccount = new SearchableAccount(account, isDueSoon);
            
            // Subscribe to selection changes
            searchableAccount.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => UpdateSelectedCount());
            
            SearchResults.Add(searchableAccount);
        }

        ApplySorting();
        UpdateSelectedCount();
    }

    private void ApplySorting()
    {
        var sorted = SortBy switch
        {
            1 => SearchResults.OrderBy(a => a.Account.AccountName).ToList(),
            2 => SearchResults.OrderByDescending(a => a.Account.GetAmount()).ToList(),
            3 => SearchResults.OrderBy(a => a.Account.GetNextInstallmentDate()).ToList(),
            _ => SearchResults.OrderBy(a => a.Account.AccountNo).ToList()
        };

        SearchResults.Clear();
        foreach (var item in sorted)
        {
            SearchResults.Add(item);
        }
    }

    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        FilterDueSoon = 0;
        SortBy = 0;
        _ = SearchAccountsAsync();
    }

    private void SelectAll()
    {
        foreach (var item in SearchResults)
        {
            item.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    private void SelectNone()
    {
        foreach (var item in SearchResults)
        {
            item.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    private void SelectFiltered()
    {
        SelectAll();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = SearchResults.Count(x => x.IsSelected);
        this.RaisePropertyChanged(nameof(HasSelection));
    }

    public List<string> GetSelectedAccountNumbers()
    {
        return SearchResults
            .Where(x => x.IsSelected)
            .Select(x => x.Account.AccountNo)
            .ToList();
    }
}
