using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using AgentBuddy.Models;

namespace AgentBuddy.ViewModels;

public class SegmentAccountsWindowViewModel : ViewModelBase
{
    private string _searchQuery = string.Empty;

    public SegmentAccountsWindowViewModel(string title, IEnumerable<RDAccount> accounts)
    {
        Title = title;
        var list = accounts.ToList();
        Accounts = new ObservableCollection<RDAccount>(list);
        FilteredAccounts = new ObservableCollection<RDAccount>(list);
        Count = FilteredAccounts.Count;
        TotalAmount = FilteredAccounts.Sum(a => a.GetAmount());
    }

    public string Title { get; }
    public int Count { get; private set; }
    public decimal TotalAmount { get; private set; }
    public ObservableCollection<RDAccount> Accounts { get; }
    public ObservableCollection<RDAccount> FilteredAccounts { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var query = (SearchQuery ?? string.Empty).Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? Accounts.ToList()
            : Accounts
                .Where(a =>
                    (!string.IsNullOrWhiteSpace(a.AccountNo) &&
                     a.AccountNo.Contains(query, System.StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(a.AccountName) &&
                     a.AccountName.Contains(query, System.StringComparison.OrdinalIgnoreCase)))
                .ToList();

        FilteredAccounts.Clear();
        foreach (var account in filtered)
        {
            FilteredAccounts.Add(account);
        }

        Count = FilteredAccounts.Count;
        TotalAmount = FilteredAccounts.Sum(a => a.GetAmount());
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(TotalAmount));
    }
}
