using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentBuddy.Models;

namespace AgentBuddy.ViewModels;

public class SegmentAccountsWindowViewModel : ViewModelBase
{
    public SegmentAccountsWindowViewModel(string title, IEnumerable<RDAccount> accounts)
    {
        Title = title;
        var list = accounts.ToList();
        Accounts = new ObservableCollection<RDAccount>(list);
        Count = list.Count;
        TotalAmount = list.Sum(a => a.GetAmount());
    }

    public string Title { get; }
    public int Count { get; }
    public decimal TotalAmount { get; }
    public ObservableCollection<RDAccount> Accounts { get; }
}
