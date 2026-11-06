using System;
using System.Reactive;
using System.Text;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class CashDenominationViewModel : ViewModelBase
{
    private int _count500;
    private int _count200;
    private int _count100;
    private int _count50;
    private int _count20;
    private int _count10;
    private decimal _targetLotAmount;
    private string _statusMessage = "Enter note quantities to balance deposit slip.";
    private string _agentName = "DOP Agent";

    public CashDenominationViewModel(decimal targetAmount = 0, string? agentName = null)
    {
        _targetLotAmount = targetAmount;
        if (!string.IsNullOrWhiteSpace(agentName)) _agentName = agentName;

        ClearCommand = ReactiveCommand.Create(Clear);
        CopySlipCommand = ReactiveCommand.Create(GenerateSlipText);

        UpdateTotals();
    }

    public int Count500
    {
        get => _count500;
        set { this.RaiseAndSetIfChanged(ref _count500, Math.Max(0, value)); UpdateTotals(); }
    }

    public int Count200
    {
        get => _count200;
        set { this.RaiseAndSetIfChanged(ref _count200, Math.Max(0, value)); UpdateTotals(); }
    }

    public int Count100
    {
        get => _count100;
        set { this.RaiseAndSetIfChanged(ref _count100, Math.Max(0, value)); UpdateTotals(); }
    }

    public int Count50
    {
        get => _count50;
        set { this.RaiseAndSetIfChanged(ref _count50, Math.Max(0, value)); UpdateTotals(); }
    }

    public int Count20
    {
        get => _count20;
        set { this.RaiseAndSetIfChanged(ref _count20, Math.Max(0, value)); UpdateTotals(); }
    }

    public int Count10
    {
        get => _count10;
        set { this.RaiseAndSetIfChanged(ref _count10, Math.Max(0, value)); UpdateTotals(); }
    }

    public decimal TargetLotAmount
    {
        get => _targetLotAmount;
        set { this.RaiseAndSetIfChanged(ref _targetLotAmount, Math.Max(0, value)); UpdateTotals(); }
    }

    public decimal Total500 => Count500 * 500m;
    public decimal Total200 => Count200 * 200m;
    public decimal Total100 => Count100 * 100m;
    public decimal Total50 => Count50 * 50m;
    public decimal Total20 => Count20 * 20m;
    public decimal Total10 => Count10 * 10m;

    public int TotalNotesCount => Count500 + Count200 + Count100 + Count50 + Count20 + Count10;
    public decimal GrandTotal => Total500 + Total200 + Total100 + Total50 + Total20 + Total10;

    public decimal Difference => GrandTotal - TargetLotAmount;
    public bool IsExactMatch => TargetLotAmount > 0 && Difference == 0;
    public bool HasTarget => TargetLotAmount > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public ReactiveCommand<Unit, string> CopySlipCommand { get; }

    private void UpdateTotals()
    {
        this.RaisePropertyChanged(nameof(Total500));
        this.RaisePropertyChanged(nameof(Total200));
        this.RaisePropertyChanged(nameof(Total100));
        this.RaisePropertyChanged(nameof(Total50));
        this.RaisePropertyChanged(nameof(Total20));
        this.RaisePropertyChanged(nameof(Total10));
        this.RaisePropertyChanged(nameof(TotalNotesCount));
        this.RaisePropertyChanged(nameof(GrandTotal));
        this.RaisePropertyChanged(nameof(Difference));
        this.RaisePropertyChanged(nameof(IsExactMatch));
        this.RaisePropertyChanged(nameof(HasTarget));

        if (TargetLotAmount <= 0)
        {
            StatusMessage = $"Total Cash Counted: ₹{GrandTotal:N0} ({TotalNotesCount} notes)";
        }
        else if (Difference == 0)
        {
            StatusMessage = $"✅ Exact Match! Total matches Lot Amount (₹{GrandTotal:N0})";
        }
        else if (Difference > 0)
        {
            StatusMessage = $"⚠️ Excess Cash: ₹{Difference:N0} above Lot Target";
        }
        else
        {
            StatusMessage = $"⚠️ Shortfall: ₹{Math.Abs(Difference):N0} remaining to balance";
        }
    }

    public void Clear()
    {
        _count500 = 0;
        _count200 = 0;
        _count100 = 0;
        _count50 = 0;
        _count20 = 0;
        _count10 = 0;
        this.RaisePropertyChanged(nameof(Count500));
        this.RaisePropertyChanged(nameof(Count200));
        this.RaisePropertyChanged(nameof(Count100));
        this.RaisePropertyChanged(nameof(Count50));
        this.RaisePropertyChanged(nameof(Count20));
        this.RaisePropertyChanged(nameof(Count10));
        UpdateTotals();
    }

    public string GenerateSlipText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("      DEPARTMENT OF POSTS (INDIA)       ");
        sb.AppendLine("         CASH DEPOSIT SLIP              ");
        sb.AppendLine("========================================");
        sb.AppendLine($"Date: {DateTime.Now:dd-MM-yyyy HH:mm}");
        sb.AppendLine($"Agent: {_agentName}");
        if (TargetLotAmount > 0) sb.AppendLine($"Lot Total: ₹{TargetLotAmount:N0}");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("DENOMINATION    COUNT          AMOUNT   ");
        sb.AppendLine("----------------------------------------");
        if (Count500 > 0) sb.AppendLine($"  ₹500       x  {Count500,4}   =  ₹{Total500,10:N0}");
        if (Count200 > 0) sb.AppendLine($"  ₹200       x  {Count200,4}   =  ₹{Total200,10:N0}");
        if (Count100 > 0) sb.AppendLine($"  ₹100       x  {Count100,4}   =  ₹{Total100,10:N0}");
        if (Count50 > 0)  sb.AppendLine($"   ₹50       x  {Count50,4}   =  ₹{Total50,10:N0}");
        if (Count20 > 0)  sb.AppendLine($"   ₹20       x  {Count20,4}   =  ₹{Total20,10:N0}");
        if (Count10 > 0)  sb.AppendLine($"   ₹10       x  {Count10,4}   =  ₹{Total10,10:N0}");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"TOTAL NOTES:    {TotalNotesCount,4}      ₹{GrandTotal,10:N0}");
        sb.AppendLine("========================================");
        return sb.ToString();
    }
}
