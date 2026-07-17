using System;
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
    public string SuggestedBankName { get; set; } = string.Empty;
    public string SuggestedChequeNo { get; set; } = string.Empty;
    public string SuggestedPaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptResult
{
    public string AccountNo { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string ChequeNo { get; set; } = string.Empty;
    public string PaymentAccountNo { get; set; } = string.Empty;
}

public sealed class DopChequePromptViewModel : ViewModelBase
{
    private string _bankName;
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
        _bankName = request.SuggestedBankName ?? string.Empty;
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
    public bool ShowBankNameField => IsNonDopChequeMode;
    public bool IsNonDopChequeMode => string.Equals(PaymentModeToken, "non_dop_cheque", StringComparison.Ordinal);

    public string WindowTitle => IsNonDopChequeMode
        ? "Non DOP Cheque Details Required"
        : "DOP Cheque Details Required";

    public string Heading => IsNonDopChequeMode
        ? "Enter Non DOP cheque details"
        : "Enter DOP cheque details";

    public string Hint => IsNonDopChequeMode
        ? "Fill bank name, cheque number, then account number for payment."
        : "Fill cheque number, then account number for payment.";

    public string BankNameLabel => "Bank Name";
    public string BankNameWatermark => "Enter bank name";
    public string ChequeNoLabel => "Cheque No";
    public string ChequeNoWatermark => "Enter cheque number";
    public string PaymentAccountLabel => "Account Number for Payment";
    public string PaymentAccountWatermark => "Enter account number for payment";

    public string BankName
    {
        get => _bankName;
        set => this.RaiseAndSetIfChanged(ref _bankName, value);
    }

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
