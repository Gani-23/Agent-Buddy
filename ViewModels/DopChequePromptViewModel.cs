using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class DopChequePromptRequest
{
    public int ListNumber { get; set; }
    public string ListName { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int Installment { get; set; } = 1;
    public string PaymentModeToken { get; set; } = "dop_cheque";
    public bool RequireChequeNo { get; set; } = true;
    public string SuggestedChequeNo { get; set; } = string.Empty;
    public string SuggestedPaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptResult
{
    public string AccountNo { get; set; } = string.Empty;
    public string ChequeNo { get; set; } = string.Empty;
    public string PaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptViewModel : ViewModelBase
{
    private string _chequeNo;
    private string _paymentAccountNo;
    private string _validationMessage = string.Empty;

    public DopChequePromptViewModel(DopChequePromptRequest request)
    {
        ListNumber = request.ListNumber;
        ListName = request.ListName;
        AccountNo = request.AccountNo.Trim();
        AccountName = request.AccountName;
        Installment = request.Installment > 0 ? request.Installment : 1;
        PaymentModeToken = string.IsNullOrWhiteSpace(request.PaymentModeToken)
            ? "dop_cheque"
            : request.PaymentModeToken.Trim().ToLowerInvariant();
        RequireChequeNo = request.RequireChequeNo;
        _chequeNo = request.SuggestedChequeNo ?? string.Empty;
        _paymentAccountNo = request.SuggestedPaymentAccountNo ?? string.Empty;
    }

    public int ListNumber { get; }

    public string ListName { get; }

    public string AccountNo { get; }

    public string AccountName { get; }

    public int Installment { get; }

    public string PaymentModeToken { get; }

    public bool RequireChequeNo { get; }

    public bool ShowChequeNoField => RequireChequeNo;

    public string Heading => RequireChequeNo
        ? "Enter DOP cheque details"
        : "Enter Non DOP payment details";

    public string Hint => RequireChequeNo
        ? "Cheque number and payment account number are required."
        : "Payment account number is required for this account.";

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

    public string ValidationMessage
    {
        get => _validationMessage;
        set => this.RaiseAndSetIfChanged(ref _validationMessage, value);
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public void ClearValidationMessage()
    {
        ValidationMessage = string.Empty;
        this.RaisePropertyChanged(nameof(HasValidationMessage));
    }

    public void SetValidationMessage(string message)
    {
        ValidationMessage = message ?? string.Empty;
        this.RaisePropertyChanged(nameof(HasValidationMessage));
    }
}
