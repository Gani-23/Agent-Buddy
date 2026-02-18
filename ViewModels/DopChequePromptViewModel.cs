using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class DopChequePromptRequest
{
    public int ListNumber { get; set; }
    public string ListName { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int Installment { get; set; } = 1;
    public string SuggestedChequeNo { get; set; } = string.Empty;
    public string SuggestedPaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptResult
{
    public string ChequeNo { get; set; } = string.Empty;
    public string PaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptViewModel : ViewModelBase
{
    private string _chequeNo;
    private string _paymentAccountNo;

    public DopChequePromptViewModel(DopChequePromptRequest request)
    {
        ListNumber = request.ListNumber;
        ListName = request.ListName;
        AccountNo = request.AccountNo;
        AccountName = request.AccountName;
        Installment = request.Installment > 0 ? request.Installment : 1;
        _chequeNo = request.SuggestedChequeNo ?? string.Empty;
        _paymentAccountNo = request.SuggestedPaymentAccountNo ?? string.Empty;
    }

    public int ListNumber { get; }

    public string ListName { get; }

    public string AccountNo { get; }

    public string AccountName { get; }

    public int Installment { get; }

    public string ChequeNo
    {
        get => _chequeNo;
        set => this.RaiseAndSetIfChanged(ref _chequeNo, value);
    }

    public string PaymentAccountNo
    {
        get => _paymentAccountNo;
        set => this.RaiseAndSetIfChanged(ref _paymentAccountNo, value);
    }
}
