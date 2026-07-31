using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using AgentBuddy.Models;
using AgentBuddy.Services;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public enum ListRunState
{
    Pending,
    Processing,
    Success,
    Failed
}

public class ListPanelViewModel : ReactiveObject
{
    private enum EntryMessageTone
    {
        Neutral,
        Success,
        Error
    }

    private static readonly string[] PaymentModeOptions =
    {
        "Cash",
        "DOP Cheque",
        "Non DOP Cheque"
    };

    private readonly DatabaseService _databaseService;
    private readonly ValidationService _validationService;
    private readonly Func<ListPanelViewModel, string, int, Task<bool>>? _addAccountHandler;

    private int _listNumber;
    private string _name;
    private int _count;
    private decimal _totalAmount;
    private string _pendingAccountNo = string.Empty;
    private string _pendingInstallmentText = "1";
    private string _entryMessage = string.Empty;
    private EntryMessageTone _entryMessageTone = EntryMessageTone.Neutral;
    private ListRunState _runState = ListRunState.Pending;
    private string _referenceNumber = string.Empty;
    private string _failureReason = string.Empty;
    private bool _isInstallmentSuggestionActive;
    private string _installmentSuggestionHint = string.Empty;
    private int _suggestedInstallment = 1;
    private int _installmentSuggestionRequestId;
    private bool _isApplyingInstallmentSuggestion;
    private string _lastProcessedSignature = string.Empty;
    private string _selectedPaymentMode = "Cash";
    private string _lastProcessedPaymentMode = "Cash";
    private bool _isDuplicateFocus;
    public event Action? StateChanged;

    public ListPanelViewModel(
        int listNumber,
        DatabaseService databaseService,
        ValidationService validationService,
        Func<ListPanelViewModel, string, int, Task<bool>>? addAccountHandler = null)
    {
        _listNumber = Math.Max(1, listNumber);
        _name = $"List {ListNumber}";
        _databaseService = databaseService;
        _validationService = validationService;
        _addAccountHandler = addAccountHandler;

        Items = new ObservableCollection<ListItem>();
        Items.CollectionChanged += (_, _) =>
        {
            RecalculateTotals();
            ResetRunStateIfPayloadChanged();
            NotifyStateChanged();
        };

        AddPendingAccountCommand = ReactiveCommand.CreateFromTask(async () => { await SubmitPendingAsync(); });
        RemoveAccountCommand = ReactiveCommand.Create<ListItem?>(RemoveAccount);
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    public int ListNumber
    {
        get => _listNumber;
        private set => this.RaiseAndSetIfChanged(ref _listNumber, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public int Count
    {
        get => _count;
        private set => this.RaiseAndSetIfChanged(ref _count, value);
    }

    public decimal TotalAmount
    {
        get => _totalAmount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _totalAmount, value);
            this.RaisePropertyChanged(nameof(IsFull));
            this.RaisePropertyChanged(nameof(RemainingAmount));
        }
    }

    public string PendingAccountNo
    {
        get => _pendingAccountNo;
        set
        {
            var normalized = NormalizeAccountInput(value);
            if (string.Equals(_pendingAccountNo, normalized, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _pendingAccountNo, normalized);
            _ = RefreshInstallmentSuggestionAsync(normalized);
            NotifyStateChanged();
        }
    }

    public string PendingInstallmentText
    {
        get => _pendingInstallmentText;
        set
        {
            var normalized = NormalizeInstallmentInput(value);
            this.RaiseAndSetIfChanged(ref _pendingInstallmentText, normalized);
            if (_isApplyingInstallmentSuggestion)
            {
                return;
            }

            var parsed = ParseInstallment(normalized);
            SetInstallmentSuggestionState(_suggestedInstallment > 1 && parsed == _suggestedInstallment);
            NotifyStateChanged();
        }
    }

    public string EntryMessage
    {
        get => _entryMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _entryMessage, value);
            this.RaisePropertyChanged(nameof(HasEntryMessage));
        }
    }

    public ListRunState RunState
    {
        get => _runState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _runState, value);
            RaiseRunStateProperties();
        }
    }

    public string ReferenceNumber
    {
        get => _referenceNumber;
        private set
        {
            this.RaiseAndSetIfChanged(ref _referenceNumber, value);
            RaiseRunStateProperties();
        }
    }

    public string FailureReason
    {
        get => _failureReason;
        private set
        {
            this.RaiseAndSetIfChanged(ref _failureReason, value);
            RaiseRunStateProperties();
        }
    }

    public IReadOnlyList<string> PaymentModes => PaymentModeOptions;
    public bool IsDuplicateFocus
    {
        get => _isDuplicateFocus;
        set => this.RaiseAndSetIfChanged(ref _isDuplicateFocus, value);
    }

    public string SelectedPaymentMode
    {
        get => _selectedPaymentMode;
        set
        {
            var normalized = NormalizePaymentModeSelection(value);
            var previous = _selectedPaymentMode;
            this.RaiseAndSetIfChanged(ref _selectedPaymentMode, normalized);
            if (!string.Equals(previous, _selectedPaymentMode, StringComparison.Ordinal))
            {
                this.RaisePropertyChanged(nameof(HasAmountLimit));
                this.RaisePropertyChanged(nameof(MaxAmount));
                this.RaisePropertyChanged(nameof(RemainingAmount));
                this.RaisePropertyChanged(nameof(IsFull));
                this.RaisePropertyChanged(nameof(AmountLimitText));
                ResetRunStateIfModeChanged();
                NotifyStateChanged();
            }
        }
    }

    public string StatusTag => RunState.ToString();
    public string StatusText => RunState switch
    {
        ListRunState.Pending => "Pending",
        ListRunState.Processing => "Processing",
        ListRunState.Success => "Completed",
        ListRunState.Failed => "Failed",
        _ => "Pending"
    };

    public bool HasEntryMessage => !string.IsNullOrWhiteSpace(EntryMessage);
    public string EntryMessageForeground => _entryMessageTone switch
    {
        EntryMessageTone.Success => "#1B5E20",
        EntryMessageTone.Error => "#C62828",
        _ => "#667085"
    };
    public bool IsPendingState => RunState == ListRunState.Pending;
    public bool IsProcessingState => RunState == ListRunState.Processing;
    public bool IsSuccessState => RunState == ListRunState.Success;
    public bool IsFailedState => RunState == ListRunState.Failed;
    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);
    public bool HasAmountLimit => !string.Equals(SelectedPaymentMode, "DOP Cheque", StringComparison.Ordinal);
    public decimal MaxAmount => HasAmountLimit ? 20000m : decimal.MaxValue;
    public decimal RemainingAmount => HasAmountLimit ? Math.Max(0, MaxAmount - TotalAmount) : 0m;
    public bool IsFull => HasAmountLimit && TotalAmount >= MaxAmount;
    public string AmountLimitText => HasAmountLimit ? " / Rs. 20,000" : " / No Limit";
    public string PaymentModeToken => GetPaymentModeToken();
    public bool IsInstallmentSuggestionActive => _isInstallmentSuggestionActive;
    public string InstallmentInputTag => IsInstallmentSuggestionActive ? "InstallmentSuggested" : "InstallmentInput";
    public string InstallmentSuggestionHint => _installmentSuggestionHint;
    public bool HasInstallmentSuggestionHint => !string.IsNullOrWhiteSpace(InstallmentSuggestionHint);

    public ObservableCollection<ListItem> Items { get; }

    public ReactiveCommand<Unit, Unit> AddPendingAccountCommand { get; }
    public ReactiveCommand<ListItem?, Unit> RemoveAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public bool HasProcessableItems =>
        Items.Any(item => item.AccountDetails != null && !string.IsNullOrWhiteSpace(item.AccountNo));

    public async Task<bool> AddAccountWithInstallmentAsync(
        string accountNo,
        int installment,
        List<string>? existingAccountsInLists = null)
    {
        accountNo = (accountNo ?? string.Empty).Trim();
        installment = Math.Max(1, installment);

        if (string.IsNullOrWhiteSpace(accountNo))
        {
            SetEntryErrorMessage("Enter an account number.");
            return false;
        }

        var existing = existingAccountsInLists ?? Items.Select(i => i.AccountNo).ToList();
        var (status, account) = await _validationService.ValidateAccountAsync(accountNo, existing);
        if (status != AccountValidationStatus.Valid && status != AccountValidationStatus.DueSoon)
        {
            SetEntryErrorMessage(status switch
            {
                AccountValidationStatus.Duplicate => $"{accountNo} already exists in a list.",
                AccountValidationStatus.Closed => $"{accountNo} is closed and cannot be added.",
                AccountValidationStatus.Matured => $"{accountNo} is already matured and cannot be added.",
                AccountValidationStatus.Invalid => $"{accountNo} not found in database.",
                _ => $"{accountNo} cannot be added."
            });
            return false;
        }

        if (account == null)
        {
            SetEntryErrorMessage($"{accountNo} not found in database.");
            return false;
        }

        var amountToAdd = account.GetAmount() * installment;
        if (HasAmountLimit && TotalAmount + amountToAdd > MaxAmount)
        {
            SetEntryErrorMessage($"Cannot add {accountNo}. This list is limited to Rs. 20,000.");
            return false;
        }

        var paymentAnalysis = account.AnalyzePayment(installment);

        Items.Add(new ListItem
        {
            AccountNo = accountNo,
            Installment = installment,
            Status = status,
            AccountDetails = account
        });

        SetEntrySuccessMessage(BuildAddedMessage(accountNo, status, paymentAnalysis));

        return true;
    }

    public void RemoveAccount(ListItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (Items.Remove(item))
        {
            SetEntrySuccessMessage($"{item.AccountNo} removed.");
        }
    }

    public void Clear()
    {
        Items.Clear();
        ClearInstallmentSuggestion(resetInstallmentValue: true);
        SetEntrySuccessMessage("List cleared.");
    }

    public void ResetProcessingMarkers()
    {
        foreach (var item in Items)
        {
            item.IsProcessedInCurrentRun = false;
        }
    }

    public void MarkAccountProcessed(string accountNo)
    {
        var normalized = (accountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var matched = Items.FirstOrDefault(item =>
            string.Equals(item.AccountNo?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (matched != null)
        {
            matched.IsProcessedInCurrentRun = true;
        }
    }

    public void MarkAllAccountsProcessed()
    {
        foreach (var item in Items)
        {
            if (!string.IsNullOrWhiteSpace(item.AccountNo))
            {
                item.IsProcessedInCurrentRun = true;
            }
        }
    }

    public void ClearDuplicateFocus()
    {
        IsDuplicateFocus = false;
        foreach (var item in Items)
        {
            item.IsDuplicateFocus = false;
        }
    }

    public void SetEntryMessage(string message)
    {
        SetEntryMessageInternal(message, EntryMessageTone.Neutral);
    }

    public void SetEntryErrorMessage(string message)
    {
        SetEntryMessageInternal(message, EntryMessageTone.Error);
    }

    public void SetEntrySuccessMessage(string message)
    {
        SetEntryMessageInternal(message, EntryMessageTone.Success);
    }

    private void SetEntryMessageInternal(string? message, EntryMessageTone tone)
    {
        _entryMessageTone = tone;
        this.RaisePropertyChanged(nameof(EntryMessageForeground));
        EntryMessage = message ?? string.Empty;
    }

    public string ToScriptFormat()
    {
        var seenAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = Items
            .Where(i =>
                i.AccountDetails != null &&
                !string.IsNullOrWhiteSpace(i.AccountNo) &&
                seenAccounts.Add(i.AccountNo.Trim()))
            .Select(i => i.GetFormattedString());
        return $"[{string.Join(", ", items)}]";
    }

    public string ToScriptFormatWithMode()
    {
        return $"{GetPaymentModeToken()}:{ToScriptFormat()}";
    }

    public string GetPayloadSignature()
    {
        return string.Join(
            ",",
            Items
                .Where(item => item.AccountDetails != null && !string.IsNullOrWhiteSpace(item.AccountNo))
                .Select(item => item.GetFormattedString().Trim())
                .Where(token => !string.IsNullOrWhiteSpace(token)));
    }

    public void MarkPending()
    {
        RunState = ListRunState.Pending;
        ReferenceNumber = string.Empty;
        FailureReason = string.Empty;
        ResetProcessingMarkers();
        RefreshDisplayName();
    }

    public void MarkProcessing()
    {
        RunState = ListRunState.Processing;
        FailureReason = string.Empty;
        RefreshDisplayName();
    }

    public void MarkSuccess(string referenceNumber)
    {
        ReferenceNumber = (referenceNumber ?? string.Empty).Trim();
        FailureReason = string.Empty;
        RunState = ListRunState.Success;
        MarkAllAccountsProcessed();
        _lastProcessedSignature = GetPayloadSignature();
        _lastProcessedPaymentMode = SelectedPaymentMode;
        RefreshDisplayName();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Processing failed." : reason.Trim();
        RunState = ListRunState.Failed;
        _lastProcessedSignature = GetPayloadSignature();
        _lastProcessedPaymentMode = SelectedPaymentMode;
        RefreshDisplayName();
    }

    public void UpdateListNumber(int newListNumber)
    {
        var normalized = Math.Max(1, newListNumber);
        if (normalized == ListNumber)
        {
            return;
        }

        ListNumber = normalized;
        RefreshDisplayName();
    }

    private void RefreshDisplayName()
    {
        Name = RunState == ListRunState.Success && !string.IsNullOrWhiteSpace(ReferenceNumber)
            ? ReferenceNumber
            : $"List {ListNumber}";
    }

    private void RecalculateTotals()
    {
        Count = Items.Count;
        TotalAmount = Items.Sum(i => i.TotalAmount);
    }

    private void ResetRunStateIfPayloadChanged()
    {
        var currentSignature = GetPayloadSignature();
        if (string.IsNullOrWhiteSpace(_lastProcessedSignature) ||
            string.Equals(currentSignature, _lastProcessedSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastProcessedSignature = string.Empty;
        MarkPending();
    }

    private void ResetRunStateIfModeChanged()
    {
        if (string.IsNullOrWhiteSpace(_lastProcessedSignature))
        {
            return;
        }

        if (string.Equals(_lastProcessedPaymentMode, SelectedPaymentMode, StringComparison.Ordinal))
        {
            return;
        }

        _lastProcessedSignature = string.Empty;
        _lastProcessedPaymentMode = SelectedPaymentMode;
        MarkPending();
    }

    private void RaiseRunStateProperties()
    {
        this.RaisePropertyChanged(nameof(StatusTag));
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(IsPendingState));
        this.RaisePropertyChanged(nameof(IsProcessingState));
        this.RaisePropertyChanged(nameof(IsSuccessState));
        this.RaisePropertyChanged(nameof(IsFailedState));
        this.RaisePropertyChanged(nameof(HasFailureReason));
    }

    private async Task<bool> AddPendingAccountAsync()
    {
        var accountNo = PendingAccountNo.Trim();
        var installment = ParseInstallment(PendingInstallmentText);

        var wasAdded = _addAccountHandler != null
            ? await _addAccountHandler(this, accountNo, installment)
            : await AddAccountWithInstallmentAsync(accountNo, installment);

        if (wasAdded)
        {
            PendingAccountNo = string.Empty;
            PendingInstallmentText = "1";
        }

        return wasAdded;
    }

    public Task<bool> SubmitPendingAsync()
    {
        return AddPendingAccountAsync();
    }

    private static string BuildAddedMessage(
        string accountNo,
        AccountValidationStatus status,
        PaymentAnalysis analysis)
    {
        var suffix = status == AccountValidationStatus.DueSoon ? " (due soon)." : ".";

        return analysis.Classification switch
        {
            PaymentClassification.AdvancePayment => $"{accountNo} added with advance coverage{suffix}",
            PaymentClassification.CatchUpPayment => $"{accountNo} added for catch-up payment{suffix}",
            PaymentClassification.MixedCatchUpAndAdvance => $"{accountNo} added with catch-up + advance coverage{suffix}",
            PaymentClassification.LongOverdueResolved => $"{accountNo} added for a long-overdue account. Review carefully before processing.",
            PaymentClassification.PartialCatchUp => $"{accountNo} added, but the account will still remain overdue after this payment.",
            PaymentClassification.LongOverduePartialCatchUp => $"{accountNo} added, but the long-overdue account will still remain pending after this payment.",
            PaymentClassification.MissingDueDate => $"{accountNo} added. Due date is unavailable; review before processing.",
            _ => $"{accountNo} added{suffix}"
        };
    }

    private static string NormalizeAccountInput(string? value)
    {
        var input = value ?? string.Empty;
        return new string(input.Where(char.IsDigit).Take(12).ToArray());
    }

    private static string NormalizeInstallmentInput(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return "1";
        }

        var trimmed = digits.TrimStart('0');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "1";
        }

        return int.TryParse(trimmed, out var parsed) && parsed > 0 ? parsed.ToString() : "1";
    }

    private static int ParseInstallment(string? text)
    {
        return int.TryParse(text, out var parsed) && parsed > 0 ? parsed : 1;
    }

    private async Task RefreshInstallmentSuggestionAsync(string accountNo)
    {
        var requestId = ++_installmentSuggestionRequestId;
        // Support both 10-digit and 12-digit account numbers.
        if (string.IsNullOrWhiteSpace(accountNo) || accountNo.Length < 10)
        {
            ClearInstallmentSuggestion(resetInstallmentValue: true);
            return;
        }

        RDAccount? account;
        try
        {
            account = await _databaseService.GetAccountByNumberAsync(accountNo);
        }
        catch
        {
            return;
        }

        if (requestId != _installmentSuggestionRequestId)
        {
            return;
        }

        if (account == null || !account.IsActive)
        {
            ClearInstallmentSuggestion(resetInstallmentValue: true);
            return;
        }

        var suggestedInstallments = account.GetPendingInstallmentsTill(DateTime.Today);
        _suggestedInstallment = suggestedInstallments;
        if (suggestedInstallments <= 1)
        {
            ClearInstallmentSuggestion(resetInstallmentValue: true);
            return;
        }

        _installmentSuggestionHint =
            $"Suggested to clear dues: {suggestedInstallments}. Edit if needed.";
        this.RaisePropertyChanged(nameof(InstallmentSuggestionHint));
        this.RaisePropertyChanged(nameof(HasInstallmentSuggestionHint));

        var currentInstallment = ParseInstallment(PendingInstallmentText);
        if (currentInstallment <= 1 || IsInstallmentSuggestionActive)
        {
            _isApplyingInstallmentSuggestion = true;
            PendingInstallmentText = suggestedInstallments.ToString(CultureInfo.InvariantCulture);
            _isApplyingInstallmentSuggestion = false;
        }

        SetInstallmentSuggestionState(ParseInstallment(PendingInstallmentText) == suggestedInstallments);
    }

    private void ClearInstallmentSuggestion(bool resetInstallmentValue)
    {
        _suggestedInstallment = 1;
        if (resetInstallmentValue)
        {
            _isApplyingInstallmentSuggestion = true;
            PendingInstallmentText = "1";
            _isApplyingInstallmentSuggestion = false;
        }

        if (!string.IsNullOrWhiteSpace(_installmentSuggestionHint))
        {
            _installmentSuggestionHint = string.Empty;
            this.RaisePropertyChanged(nameof(InstallmentSuggestionHint));
            this.RaisePropertyChanged(nameof(HasInstallmentSuggestionHint));
        }

        SetInstallmentSuggestionState(false);
    }

    private void SetInstallmentSuggestionState(bool active)
    {
        if (_isInstallmentSuggestionActive == active)
        {
            return;
        }

        _isInstallmentSuggestionActive = active;
        this.RaisePropertyChanged(nameof(IsInstallmentSuggestionActive));
        this.RaisePropertyChanged(nameof(InstallmentInputTag));
    }

    private static string NormalizePaymentModeSelection(string? value)
    {
        var mode = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Cash";
        }

        var normalized = mode
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.ToLowerInvariant() switch
        {
            "dop cheque" => "DOP Cheque",
            "dopcheque" => "DOP Cheque",
            "non dop cheque" => "Non DOP Cheque",
            "nondop cheque" => "Non DOP Cheque",
            "non dopcheque" => "Non DOP Cheque",
            "cash" => "Cash",
            _ => "Cash"
        };
    }

    private string GetPaymentModeToken()
    {
        return SelectedPaymentMode switch
        {
            "DOP Cheque" => "dop_cheque",
            "Non DOP Cheque" => "non_dop_cheque",
            _ => "cash"
        };
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}

public class ListManagementViewModel : ViewModelBase
{
    private const string DopChequeDefaultChequeNoKey = "dop_cheque_default_cheque_no";
    private const string DopChequeDefaultPaymentAccountNoKey = "dop_cheque_default_payment_account_no";
    private const string DopChequeDefaultBankNameKey = "dop_cheque_default_bank_name";
    private const string SavedLotRetentionDaysKey = AppSettingKeys.SavedLotRetentionDays;
    private const int AutoSaveIntervalSeconds = 20;

    private static readonly Regex ProcessingListRegex = new(
        @"PROCESSING LIST #\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReferenceRegex = new(
        @"Reference:\s*([A-Z0-9]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FailedListRegex = new(
        @"^\s*List #\s*(\d+)\s*:\s*(.+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ErrorProcessingRegex = new(
        @"Error processing list #\s*(\d+)\s*:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReferenceEntryRegex = new(
        @"Timestamp:\s*(?<timestamp>[^\r\n]+)\s*[\r\n]+List #:\s*(?<list>\d+)\s*[\r\n]+Reference Number:\s*(?<reference>[^\r\n]+)\s*[\r\n]+Accounts:\s*(?<accounts>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AccountTokenRegex = new(
        @"\d+(?:_\d+)?",
        RegexOptions.Compiled);

    private static readonly Regex AccountProcessedRegex = new(
        @"(?:\bSet\s+\d+\s+installments?\s+for|\bSetting\s+\d+\s+installments?\s+for)\s+(\d{6,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxProcessLogEntries = 300;

    private readonly DatabaseService _databaseService;
    private readonly ValidationService _validationService;
    private readonly PythonService _pythonService;
    private readonly ReportsService _reportsService;
    private readonly NotificationService? _notificationService;
    private readonly string _processingStatePath;
    private readonly string _lotSnapshotPath;
    private readonly string _lotCheckpointDirectory;
    private readonly string _autoSaveSnapshotPath;
    private readonly string _referencesFilePath;
    private readonly Dictionary<string, PersistedListRunState> _persistedStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingAslaasUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _autoSaveTimer;

    private bool _isDarkTheme;
    private bool _isProcessing;
    private bool _isUpdatingDatabase;
    private bool _isAutoSaving;
    private bool _hasPendingAutoSave;
    private bool _isRestoringAutoSave;
    private string _processStatus = string.Empty;
    private DateTime? _lastAutoSaveAt;
    private DateTime? _lastSavedLotAt;
    private DateTime? _databaseLastUpdated;
    private bool _hasStaleDatabaseOverride;
    private int _duplicateHighlightVersion;

    public ListManagementViewModel(
        DatabaseService databaseService,
        ValidationService validationService,
        PythonService pythonService,
        ReportsService reportsService,
        NotificationService? notificationService = null)
    {
        _databaseService = databaseService;
        _validationService = validationService;
        _pythonService = pythonService;
        _reportsService = reportsService;
        _notificationService = notificationService;

        var stateDirectory = Path.Combine(AppPaths.BaseDirectory, "State");
        Directory.CreateDirectory(stateDirectory);
        _processingStatePath = Path.Combine(stateDirectory, "list_processing_state.json");
        _lotSnapshotPath = Path.Combine(stateDirectory, "list_lot_snapshot.json");
        _lotCheckpointDirectory = Path.Combine(stateDirectory, "lot_checkpoints");
        _autoSaveSnapshotPath = Path.Combine(stateDirectory, "list_autosave.json");
        _referencesFilePath = Path.Combine(AppPaths.BaseDirectory, "Reports", "references", "payment_references.txt");
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoSaveIntervalSeconds)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        LoadPersistedState();
        LoadLatestSavedLotStatus();

        Lists = new ObservableCollection<ListPanelViewModel>();
        ReferenceNumbers = new ObservableCollection<string>();
        ProcessingLogs = new ObservableCollection<string>();
        ProcessingLogs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasProcessingLogs));

        AddNewListCommand = ReactiveCommand.Create(AddNewList);
        SaveLotCommand = ReactiveCommand.CreateFromTask(SaveLotAsync);
        ReloadLotCommand = ReactiveCommand.CreateFromTask(ReloadLotAsync);
        DeleteAllListsCommand = ReactiveCommand.Create(DeleteAllLists);
        ProcessAllListsCommand = ReactiveCommand.CreateFromTask(ProcessAllListsAsync);
        RetryFailedListsCommand = ReactiveCommand.CreateFromTask(RetryFailedListsAsync);
        RefreshDatabaseStatusCommand = ReactiveCommand.CreateFromTask(RefreshDatabaseStatusAsync);
        UpdateDatabaseCommand = ReactiveCommand.CreateFromTask(UpdateDatabaseAsync);
        OverrideDatabaseGuardCommand = ReactiveCommand.Create(EnableStaleDatabaseOverride);

        AddNewList();
        _databaseService.DatabaseChanged += OnDatabaseChanged;
        _autoSaveTimer.Start();
        _ = RestoreAutoSaveAsync();
        _ = RefreshDatabaseStatusAsync();
        _ = CleanupSavedLotSnapshotsAsync();
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            this.RaiseAndSetIfChanged(ref _isProcessing, value);
            this.RaisePropertyChanged(nameof(CanProcessLists));
            this.RaisePropertyChanged(nameof(CanRetryFailedLists));
        }
    }

    public string ProcessStatus
    {
        get => _processStatus;
        set => this.RaiseAndSetIfChanged(ref _processStatus, value);
    }

    public bool IsUpdatingDatabase
    {
        get => _isUpdatingDatabase;
        private set => this.RaiseAndSetIfChanged(ref _isUpdatingDatabase, value);
    }

    public DateTime? DatabaseLastUpdated
    {
        get => _databaseLastUpdated;
        private set
        {
            this.RaiseAndSetIfChanged(ref _databaseLastUpdated, value);
            RaiseDatabaseGuardProperties();
        }
    }

    public bool HasStaleDatabaseOverride
    {
        get => _hasStaleDatabaseOverride;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasStaleDatabaseOverride, value);
            RaiseDatabaseGuardProperties();
        }
    }

    public bool HasFailedLists => Lists.Any(list => list.IsFailedState);
    public bool HasProcessingLogs => ProcessingLogs.Count > 0;
    public bool IsDatabaseFresh => DatabaseLastUpdated.HasValue && DatabaseLastUpdated.Value.Date >= DateTime.Today;
    public bool CanEditLists => IsDatabaseFresh || HasStaleDatabaseOverride;
    public bool CanProcessLists => CanEditLists && !HasFailedLists && !IsProcessing;
    public bool CanRetryFailedLists => CanEditLists && HasFailedLists && !IsProcessing;
    public bool ShowRetryFailedWarning => HasFailedLists;
    public bool ShowDatabaseGuard => !IsDatabaseFresh;
    public bool ShowOverrideButton => ShowDatabaseGuard && !HasStaleDatabaseOverride;
    public bool ShowOverrideWarning => ShowDatabaseGuard && HasStaleDatabaseOverride;
    public DateTime? LastAutoSaveAt
    {
        get => _lastAutoSaveAt;
        private set
        {
            this.RaiseAndSetIfChanged(ref _lastAutoSaveAt, value);
            this.RaisePropertyChanged(nameof(AutoSaveStatusText));
        }
    }

    public DateTime? LastSavedLotAt
    {
        get => _lastSavedLotAt;
        private set
        {
            this.RaiseAndSetIfChanged(ref _lastSavedLotAt, value);
            this.RaisePropertyChanged(nameof(SavedLotStatusText));
        }
    }

    public string AutoSaveStatusText => LastAutoSaveAt.HasValue
        ? $"Autosaved {LastAutoSaveAt.Value:dd-MMM hh:mm tt}"
        : "Autosave on";
    public string SavedLotStatusText => LastSavedLotAt.HasValue
        ? $"Checkpoint saved {LastSavedLotAt.Value:dd-MMM hh:mm tt}"
        : "Checkpoint not saved yet";
    public string DatabaseStatusTitle => IsDatabaseFresh ? "Database updated today" : "Database update required";
    public string DatabaseStatusMessage => IsDatabaseFresh
        ? $"Last updated: {DatabaseLastUpdated:dd-MMM-yyyy hh:mm tt}"
        : DatabaseLastUpdated.HasValue
            ? $"Last updated on {DatabaseLastUpdated:dd-MMM-yyyy hh:mm tt}. Update the database before creating lists, or override carefully."
            : "No database update history found. Update the database before creating lists, or override carefully.";

    public ObservableCollection<ListPanelViewModel> Lists { get; }
    public ObservableCollection<string> ReferenceNumbers { get; }
    public ObservableCollection<string> ProcessingLogs { get; }
    public Interaction<AslaasPromptRequest, string?> AslaasPrompt { get; } = new();
    public Interaction<DopChequePromptRequest, DopChequePromptResult?> DopChequePrompt { get; } = new();
    public Interaction<ConfirmDialogRequest, bool> ConfirmPrompt { get; } = new();
    public Interaction<ConfirmListDialogRequest, bool> ConfirmListPrompt { get; } = new();
    public Interaction<DuplicateFocusRequest, Unit> DuplicateFocusPrompt { get; } = new();

    public ReactiveCommand<Unit, Unit> AddNewListCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveLotCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadLotCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteAllListsCommand { get; }
    public ReactiveCommand<Unit, Unit> ProcessAllListsCommand { get; }
    public ReactiveCommand<Unit, Unit> RetryFailedListsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshDatabaseStatusCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> OverrideDatabaseGuardCommand { get; }

    private void AddNewList()
    {
        if (Lists.Count > 0 && !CanEditLists)
        {
            ProcessStatus = "Update the database before creating new lists, or use Proceed Anyway.";
            _notificationService?.Warning("Database Update Needed", "Update the database before creating new lists.");
            return;
        }

        var index = Lists.Count + 1;
        var list = CreateListPanel(index, applyPersistedState: false);
        Lists.Add(list);
        RefreshReferenceNumbersFromLists();
        RaiseListStateProperties();
        ScheduleAutoSave();
    }

    private void DeleteAllLists()
    {
        ClearListPanels();
        _pendingAslaasUpdates.Clear();
        _persistedStates.Clear();
        SavePersistedState();

        AddNewList();
        ScheduleAutoSave();
    }

    public async Task DeleteListAsync(ListPanelViewModel? list)
    {
        if (list == null)
        {
            return;
        }

        if (IsProcessing)
        {
            ProcessStatus = "Cannot delete lists while processing is running.";
            return;
        }

        if (!CanEditLists)
        {
            ProcessStatus = "Update the database before modifying lists, or use Proceed Anyway.";
            _notificationService?.Warning("Database Update Needed", "Update the database before modifying lists.");
            return;
        }

        var needsConfirmation = list.Count > 0 || list.IsSuccessState || list.IsFailedState;
        if (needsConfirmation)
        {
            var message = list.Count > 0
                ? $"{list.Name} has {list.Count} account(s). Delete this entire list?"
                : $"Delete {list.Name}?";

            var confirmed = false;
            try
            {
                confirmed = await ConfirmPrompt.Handle(new ConfirmDialogRequest(
                    "Delete list?",
                    message,
                    "Delete",
                    "Keep")).ToTask();
            }
            catch (UnhandledInteractionException<ConfirmDialogRequest, bool>)
            {
                confirmed = false;
            }

            if (!confirmed)
            {
                return;
            }
        }

        list.PropertyChanged -= OnListPropertyChanged;
        PersistNeutralListState(list);
        Lists.Remove(list);
        RenumberLists();
        RefreshReferenceNumbersFromLists();
        RaiseListStateProperties();

        if (Lists.Count == 0)
        {
            AddNewList();
        }

        ProcessStatus = $"{list.Name} deleted.";
        _notificationService?.Success("List Deleted", $"{list.Name} removed.");
        ScheduleAutoSave();
    }

    private async Task SaveLotAsync()
    {
        try
        {
            var snapshot = BuildLotSnapshot();

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_lotSnapshotPath, json);
            Directory.CreateDirectory(_lotCheckpointDirectory);

            var checkpointName = $"lot_{snapshot.GeneratedAtUtc:yyyyMMdd_HHmmss_fff}.json";
            var checkpointPath = Path.Combine(_lotCheckpointDirectory, checkpointName);
            await File.WriteAllTextAsync(checkpointPath, json);

            LastSavedLotAt = snapshot.GeneratedAtUtc.ToLocalTime();
            ProcessStatus = $"Checkpoint saved at {LastSavedLotAt:dd-MMM hh:mm tt}.";
            _notificationService?.Success("Checkpoint Saved", $"{snapshot.Lists.Count} list(s) saved.");
            _ = CleanupOldLotCheckpointsAsync();
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Checkpoint save failed: {ex.Message}";
            _notificationService?.Error("Save Checkpoint Failed", ex.Message);
        }
    }

    private async Task ReloadLotAsync()
    {
        if (IsProcessing)
        {
            ProcessStatus = "Cannot reload while processing is running.";
            return;
        }

        var latestSnapshotPath = GetLatestSavedLotSnapshotPath();
        if (latestSnapshotPath == null)
        {
            ProcessStatus = "No saved checkpoint found. Use Save Lot first.";
            _notificationService?.Warning("No Saved Checkpoint", "Save a checkpoint before restoring.");
            return;
        }

        var confirmed = false;
        try
        {
            confirmed = await ConfirmPrompt.Handle(new ConfirmDialogRequest(
                "Restore checkpoint?",
                "This will replace the open lists with the saved checkpoint. Autosave stays separate.",
                "Restore",
                "Cancel")).ToTask();
        }
        catch (UnhandledInteractionException<ConfirmDialogRequest, bool>)
        {
            confirmed = false;
        }

        if (!confirmed)
        {
            return;
        }

        var snapshotPath = latestSnapshotPath;
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(snapshotPath);
            var snapshot = JsonSerializer.Deserialize<LotSnapshot>(json);
            var generatedAtUtc = snapshot?.GeneratedAtUtc;
            if (generatedAtUtc.HasValue && generatedAtUtc.Value != default)
            {
                LastSavedLotAt = generatedAtUtc.Value.ToLocalTime();
            }
            var savedLists = snapshot?.Lists?
                .OrderBy(list => list.ListNumber)
                .ToList() ?? new List<LotListSnapshot>();

            ClearListPanels();
            _pendingAslaasUpdates.Clear();
            if (snapshot?.PendingAslaasUpdates != null)
            {
                foreach (var pending in snapshot.PendingAslaasUpdates)
                {
                    if (string.IsNullOrWhiteSpace(pending.AccountNo))
                    {
                        continue;
                    }

                    _pendingAslaasUpdates[pending.AccountNo.Trim()] = NormalizeAslaasValue(pending.AslaasNo);
                }
            }
            ScheduleAutoSave();

            var skippedAccounts = new List<string>();
            foreach (var saved in savedLists)
            {
                var listNumber = saved.ListNumber > 0 ? saved.ListNumber : Lists.Count + 1;
                var list = CreateListPanel(listNumber, applyPersistedState: false);
                list.SelectedPaymentMode = saved.PaymentMode;
                Lists.Add(list);

                foreach (var savedItem in saved.Items)
                {
                    if (string.IsNullOrWhiteSpace(savedItem.AccountNo))
                    {
                        continue;
                    }

                    var wasAdded = await AddSingleAccountToListAsync(
                        list,
                        savedItem.AccountNo.Trim(),
                        Math.Max(1, savedItem.Installment),
                        skipAdvanceConfirmation: true,
                        skipDatabaseGuard: true);

                    if (!wasAdded)
                    {
                        skippedAccounts.Add(savedItem.AccountNo.Trim());
                    }
                }

                list.PendingAccountNo = saved.PendingAccountNo;
                list.PendingInstallmentText = string.IsNullOrWhiteSpace(saved.PendingInstallmentText)
                    ? "1"
                    : saved.PendingInstallmentText;
                ApplySnapshotStatus(list, saved);
                PersistListState(list);
            }

            if (Lists.Count == 0)
            {
                AddNewList();
            }

            RefreshReferenceNumbersFromLists();
            RaiseListStateProperties();

            ProcessStatus = skippedAccounts.Count == 0
                ? $"Reloaded {Lists.Count} list(s) from saved lot."
                : $"Reloaded {Lists.Count} list(s); skipped {skippedAccounts.Count} invalid/duplicate account(s).";
            _notificationService?.Info("Lot Reloaded", ProcessStatus);
            ScheduleAutoSave();
            _ = CleanupOldLotCheckpointsAsync();
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Reload lot failed: {ex.Message}";
            _notificationService?.Error("Reload Lot Failed", ex.Message);
        }
    }

    public List<string> GetAllExistingAccountNumbers()
    {
        return Lists
            .SelectMany(l => l.Items.Select(i => i.AccountNo))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();
    }

    public async Task AddAccountsToListAsync(ListPanelViewModel list, List<string> accountNumbers)
    {
        if (list == null || accountNumbers == null || accountNumbers.Count == 0)
        {
            return;
        }

        foreach (var accountNo in accountNumbers)
        {
            if (list.IsFull)
            {
                break;
            }

            await AddSingleAccountToListAsync(list, accountNo, 1);
        }
    }

    public async Task<bool> AddOverdueAccountToBestListAsync(string accountNo, int balanceMonths)
    {
        if (!CanEditLists)
        {
            ProcessStatus = "Update the database before adding overdue accounts, or use Proceed Anyway.";
            _notificationService?.Warning("Database Update Needed", "Update the database before adding overdue accounts.");
            return false;
        }

        var installment = Math.Max(1, balanceMonths);
        var targetLists = Lists.Where(list => !list.IsFull).ToList();
        if (targetLists.Count == 0)
        {
            AddNewList();
            targetLists = Lists.Where(list => !list.IsFull).ToList();
        }

        foreach (var list in targetLists)
        {
            if (await AddSingleAccountToListAsync(list, accountNo, installment))
            {
                ProcessStatus = $"Added {accountNo} to {list.Name} with {installment} installment(s).";
                ScheduleAutoSave();
                return true;
            }
        }

        ProcessStatus = $"Could not add {accountNo}. Review duplicate, ASLAAS, or amount-limit warnings in the list.";
        return false;
    }

    private async Task RefreshDatabaseStatusAsync()
    {
        try
        {
            DatabaseLastUpdated = await _databaseService.GetLastUpdateTimeAsync();
            if (IsDatabaseFresh)
            {
                HasStaleDatabaseOverride = false;
            }
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Could not read database update status: {ex.Message}";
        }
    }

    private async Task UpdateDatabaseAsync()
    {
        if (IsUpdatingDatabase || IsProcessing)
        {
            return;
        }

        IsUpdatingDatabase = true;
        ProcessStatus = "Checking Python installation...";

        try
        {
            var (isInstalled, version) = await _pythonService.CheckPythonInstalledAsync();
            if (!isInstalled)
            {
                ProcessStatus = "Python not found! Please install Python 3.x";
                _notificationService?.Error("Update Failed", "Python 3.x was not found.");
                return;
            }

            var (fetchExists, _) = _pythonService.CheckScriptsExist();
            if (!fetchExists)
            {
                ProcessStatus = "Fetch_RDAccounts.py not found in DOPAgent folder.";
                _notificationService?.Error("Update Failed", "Fetch_RDAccounts.py was not found.");
                return;
            }

            ProcessStatus = "Checking required Python packages...";
            var hasPackages = await _pythonService.CheckRequiredPackagesAsync();
            if (!hasPackages)
            {
                ProcessStatus = "Installing missing Python packages...";
                _notificationService?.Info("Python Setup", "Installing missing packages...");

                var (installed, installOutput) = await _pythonService.InstallRequiredPackagesAsync();
                if (!installed)
                {
                    ProcessStatus = "Package installation failed. Check internet and pip.";
                    var firstLine = string.IsNullOrWhiteSpace(installOutput)
                        ? "Could not install required Python packages."
                        : installOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Could not install required Python packages.";
                    _notificationService?.Error("Package Install Failed", firstLine);
                    return;
                }

                _notificationService?.Success("Python Setup", "Required packages installed.");
            }

            ProcessStatus = $"Python {version} ready. Starting update...";
            var (success, output) = await _pythonService.UpdateDatabaseAsync(progress =>
            {
                var trimmed = (progress ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    ProcessStatus = trimmed;
                }
            });

            if (!success)
            {
                ProcessStatus = $"Update failed: {output}";
                _notificationService?.Error("Update Failed", "Fetch_RDAccounts.py did not complete successfully.");
                return;
            }

            _databaseService.NotifyDatabaseChanged();
            await RefreshDatabaseStatusAsync();
            ProcessStatus = "Database updated successfully. You can continue creating lists.";
            _notificationService?.Success("Database Updated", "Active account data was refreshed successfully.");
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Update failed: {ex.Message}";
            _notificationService?.Error("Update Error", ex.Message);
        }
        finally
        {
            IsUpdatingDatabase = false;
        }
    }

    private void EnableStaleDatabaseOverride()
    {
        HasStaleDatabaseOverride = true;
        ProcessStatus = "Proceeding with a stale database. Review accounts carefully.";
        _notificationService?.Warning("Stale Database Override", "Proceeding without today's database update.");
    }

    private async Task<bool> AddSingleAccountToListAsync(
        ListPanelViewModel list,
        string accountNo,
        int installment,
        bool skipAdvanceConfirmation = false,
        bool skipDatabaseGuard = false)
    {
        if (list == null)
        {
            return false;
        }

        if (!skipDatabaseGuard && !CanEditLists)
        {
            list.SetEntryErrorMessage("Update the database first, or use Proceed Anyway.");
            _notificationService?.Warning("Database Update Needed", "Update the database before creating lists.");
            return false;
        }

        var normalizedAccountNo = (accountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedAccountNo))
        {
            list.SetEntryErrorMessage("Enter an account number.");
            return false;
        }

        var duplicateMatch = FindOpenListDuplicate(normalizedAccountNo);
        if (duplicateMatch != null)
        {
            await HighlightDuplicateAsync(duplicateMatch.Value.list, duplicateMatch.Value.item);
            var listLabel = $"List {duplicateMatch.Value.list.ListNumber}";
            list.SetEntryErrorMessage($"{normalizedAccountNo} already exists in {listLabel}.");
            _notificationService?.Warning("Duplicate In Open Lists", $"{normalizedAccountNo} already exists in {listLabel}.");
            return false;
        }

        var existingAccounts = GetAllExistingAccountNumbers();
        var (validationStatus, validatedAccount) = await _validationService.ValidateAccountAsync(normalizedAccountNo, existingAccounts);
        var isProcessable = validationStatus == AccountValidationStatus.Valid ||
                            validationStatus == AccountValidationStatus.DueSoon;

        if (isProcessable)
        {
            var account = validatedAccount;
            if (account != null && IsAslaasMissing(account))
            {
                if (!_pendingAslaasUpdates.TryGetValue(normalizedAccountNo, out var queuedAslaas))
                {
                    queuedAslaas = await RequestAslaasValueAsync(account);
                    if (string.IsNullOrWhiteSpace(queuedAslaas))
                    {
                        list.SetEntryErrorMessage($"{normalizedAccountNo} requires ASLAAS before adding.");
                        return false;
                    }

                    _pendingAslaasUpdates[normalizedAccountNo] = NormalizeAslaasValue(queuedAslaas);
                    ScheduleAutoSave();
                }
            }

            if (account != null)
            {
                var paymentAnalysis = account.AnalyzePayment(installment);
                if (!skipAdvanceConfirmation &&
                    ShouldConfirmAdvancePayment(paymentAnalysis) &&
                    !await ConfirmAdvancePaymentAsync(account, paymentAnalysis))
                {
                    list.SetEntryMessage("Review the installment count before adding this account.");
                    return false;
                }
            }
        }

        return await list.AddAccountWithInstallmentAsync(normalizedAccountNo, installment, existingAccounts);
    }

    private async Task<bool> ConfirmAdvancePaymentAsync(RDAccount account, PaymentAnalysis analysis)
    {
        if (!ShouldConfirmAdvancePayment(analysis))
        {
            return true;
        }

        var lines = new List<string>
        {
            $"{account.AccountNo}",
            $"Already paid till {analysis.DueMonthDisplay}",
            $"Entered {analysis.EnteredInstallments} more installment(s)",
            $"Next due after pay: {analysis.NextDueMonthAfterPaymentDisplay}"
        };

        return await ConfirmPrompt.Handle(new ConfirmDialogRequest(
            "Pay more advance?",
            string.Join(Environment.NewLine, lines),
            "Continue",
            "Review Entry")).ToTask();
    }

    private static bool ShouldConfirmAdvancePayment(PaymentAnalysis analysis)
    {
        return analysis.DueMonth.HasValue &&
               analysis.DueMonth.Value > analysis.CurrentMonth;
    }

    private async Task<string?> RequestAslaasValueAsync(RDAccount account)
    {
        var response = await AslaasPrompt.Handle(new AslaasPromptRequest
        {
            AccountNo = account.AccountNo,
            AccountName = account.AccountName,
            SuggestedAslaasNo = "APPLIED"
        }).ToTask();

        if (response == null)
        {
            return null;
        }

        var trimmed = response.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return NormalizeAslaasValue(trimmed);
    }

    private static bool IsAslaasMissing(RDAccount account)
    {
        if (account == null)
        {
            return true;
        }

        var aslaas = (account.AslaasNo ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(aslaas);
    }

    private static string NormalizeAslaasValue(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "APPLIED" : trimmed.ToUpperInvariant();
    }

    private List<AslaasUpdateItem> CollectPendingAslaasUpdates(IEnumerable<ListPanelViewModel> processableLists)
    {
        var relevantAccounts = new HashSet<string>(
            processableLists
                .SelectMany(list => list.Items)
                .Where(item => item.AccountDetails != null && !string.IsNullOrWhiteSpace(item.AccountNo))
                .Select(item => item.AccountNo.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return _pendingAslaasUpdates
            .Where(entry => relevantAccounts.Contains(entry.Key))
            .Select(entry => new AslaasUpdateItem
            {
                AccountNo = entry.Key,
                AslaasNo = NormalizeAslaasValue(entry.Value)
            })
            .OrderBy(entry => entry.AccountNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Task ProcessAllListsAsync()
    {
        return ProcessListsAsync(retryFailedOnly: false);
    }

    private Task RetryFailedListsAsync()
    {
        return ProcessListsAsync(retryFailedOnly: true);
    }

    private async Task ProcessListsAsync(bool retryFailedOnly)
    {
        if (IsProcessing)
        {
            return;
        }

        if (!retryFailedOnly && HasFailedLists)
        {
            ProcessStatus = "Failed lists exist. Use Retry Failed Only to avoid report conflicts.";
            _notificationService?.Warning("Retry Failed Lists Only", ProcessStatus);
            return;
        }

        if (!CanEditLists)
        {
            ProcessStatus = "Update the database before processing lists, or use Proceed Anyway.";
            _notificationService?.Warning("Database Update Needed", ProcessStatus);
            return;
        }

        var processableLists = Lists
            .Where(list =>
                list.HasProcessableItems &&
                (retryFailedOnly ? list.IsFailedState : !list.IsSuccessState))
            .ToList();

        if (processableLists.Count == 0)
        {
            ProcessStatus = retryFailedOnly
                ? "No failed lists available to retry."
                : "No pending lists to process.";
            _notificationService?.Warning("Nothing To Process", ProcessStatus);
            return;
        }

        IsProcessing = true;
        ClearProcessingLogs();
        AppendProcessingLog(
            retryFailedOnly
                ? $"Retry started for {processableLists.Count} failed list(s)."
                : $"Run started for {processableLists.Count} pending list(s).");

        try
        {
            ProcessStatus = "Checking Python installation...";
            AppendProcessingLog(ProcessStatus);
            var (isInstalled, _) = await _pythonService.CheckPythonInstalledAsync();
            if (!isInstalled)
            {
                ProcessStatus = "Python not found. Install Python 3.x and try again.";
                AppendProcessingLog(ProcessStatus);
                _notificationService?.Error("Python Missing", "Install Python 3.x to process lists.");
                return;
            }

            ProcessStatus = "Checking Python packages...";
            AppendProcessingLog(ProcessStatus);
            var hasRequiredPackages = await _pythonService.CheckRequiredPackagesAsync();
            if (!hasRequiredPackages)
            {
                ProcessStatus = "Python packages missing or broken. Reinstall requirements and try again.";
                AppendProcessingLog(ProcessStatus);
                _notificationService?.Error(
                    "Python Packages Missing",
                    "Required Python packages are missing or broken. Open Settings and reinstall requirements.");
                return;
            }

            foreach (var list in processableLists)
            {
                list.MarkPending();
            }

            var bulkListsString = string.Join(
                ", ",
                processableLists
                    .Where(list => list.HasProcessableItems)
                    .Select(list => list.ToScriptFormatWithMode()));

            if (string.IsNullOrWhiteSpace(bulkListsString))
            {
                ProcessStatus = "No valid list payload to send.";
                AppendProcessingLog(ProcessStatus);
                _notificationService?.Warning("Nothing To Send", "All selected lists are empty.");
                return;
            }

            var queuedAslaasUpdates = CollectPendingAslaasUpdates(processableLists);
            AppendProcessingLog($"Queued ASLAAS updates: {queuedAslaasUpdates.Count}");
            var queuedDopChequeInputs = await CollectDopChequeInputsAsync(processableLists);
            if (queuedDopChequeInputs == null)
            {
                ProcessStatus = "Processing cancelled.";
                AppendProcessingLog(ProcessStatus);
                return;
            }
            AppendProcessingLog($"Queued cheque/account prompts: {queuedDopChequeInputs.Count}");

            if (queuedDopChequeInputs.Count > 0)
            {
                await _databaseService.SaveDopChequeInputsAsync(queuedDopChequeInputs);
            }

            ProcessStatus = retryFailedOnly
                ? $"Retrying {processableLists.Count} failed list(s)..."
                : $"Running {processableLists.Count} pending list(s)...";
            AppendProcessingLog(ProcessStatus);

            var indexedProcessableLists = processableLists
                .Select((list, index) => new { list, index = index + 1 })
                .ToDictionary(entry => entry.index, entry => entry.list);

            var defaultPaymentMode = processableLists
                .Select(list => list.PaymentModeToken)
                .FirstOrDefault(token => !string.IsNullOrWhiteSpace(token))
                ?? "cash";

            var stateLock = new object();
            var currentProcessingIndex = 0;

            var result = await _pythonService.ProcessListsAsync(
                bulkListsString,
                queuedAslaasUpdates,
                paymentMode: defaultPaymentMode,
                dopChequeInputs: queuedDopChequeInputs,
                progress =>
                {
                    if (string.IsNullOrWhiteSpace(progress))
                    {
                        return;
                    }

                    var line = progress.Trim();
                    Dispatcher.UIThread.Post(() => ProcessStatus = line);
                    AppendProcessingLog(line);

                    var processingMatch = ProcessingListRegex.Match(line);
                    if (processingMatch.Success && int.TryParse(processingMatch.Groups[1].Value, out var processingIndex))
                    {
                        lock (stateLock)
                        {
                            currentProcessingIndex = processingIndex;
                        }

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (indexedProcessableLists.TryGetValue(processingIndex, out var list))
                            {
                                list.MarkProcessing();
                                PersistListState(list);
                            }
                        });
                        return;
                    }

                    var accountProcessedMatch = AccountProcessedRegex.Match(line);
                    if (accountProcessedMatch.Success)
                    {
                        var accountNo = accountProcessedMatch.Groups[1].Value.Trim();
                        var mappedIndex = 0;
                        lock (stateLock)
                        {
                            mappedIndex = currentProcessingIndex;
                        }

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (mappedIndex > 0 && indexedProcessableLists.TryGetValue(mappedIndex, out var list))
                            {
                                list.MarkAccountProcessed(accountNo);
                            }
                        });
                    }

                    var referenceMatch = ReferenceRegex.Match(line);
                    if (referenceMatch.Success)
                    {
                        var reference = referenceMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrWhiteSpace(reference))
                        {
                            return;
                        }

                        var mappedIndex = 0;
                        lock (stateLock)
                        {
                            mappedIndex = currentProcessingIndex;
                        }

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (mappedIndex > 0 && indexedProcessableLists.TryGetValue(mappedIndex, out var list))
                            {
                                list.MarkSuccess(reference);
                                PersistListState(list);
                                RefreshReferenceNumbersFromLists();
                            }
                        });
                        return;
                    }

                    var failedMatch = ErrorProcessingRegex.Match(line);
                    if (failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var failedIndex))
                    {
                        var reason = failedMatch.Groups[2].Value.Trim();
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (indexedProcessableLists.TryGetValue(failedIndex, out var list))
                            {
                                list.MarkFailed(reason);
                                PersistListState(list);
                            }
                        });
                        return;
                    }

                    var failedSummaryMatch = FailedListRegex.Match(line);
                    if (!failedSummaryMatch.Success || !int.TryParse(failedSummaryMatch.Groups[1].Value, out var summaryIndex))
                    {
                        return;
                    }

                    var summaryReason = failedSummaryMatch.Groups[2].Value.Trim();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (indexedProcessableLists.TryGetValue(summaryIndex, out var list))
                        {
                            list.MarkFailed(summaryReason);
                            PersistListState(list);
                        }
                    });
                });

            if (!result.Success)
            {
                var errorMessage = FirstMeaningfulLine(result.ErrorMessage);
                foreach (var list in processableLists.Where(list => !list.IsSuccessState))
                {
                    list.MarkFailed(errorMessage);
                    PersistListState(list);
                }

                ProcessStatus = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "List processing failed."
                    : result.ErrorMessage;
                AppendProcessingLog("List processing failed.");
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    AppendProcessingLog($"Failure reason: {errorMessage}");
                }
                _notificationService?.Error("List Processing Failed", "ScheduleArguments.py returned an error.");
                RefreshReferenceNumbersFromLists();
                RaiseListStateProperties();
                return;
            }

            if (queuedAslaasUpdates.Count > 0)
            {
                await _databaseService.SaveAslaasUpdatesAsync(queuedAslaasUpdates);
            }

            foreach (var update in queuedAslaasUpdates)
            {
                _pendingAslaasUpdates.Remove(update.AccountNo);
            }
            ScheduleAutoSave();

            foreach (var list in processableLists.Where(list => !list.IsSuccessState))
            {
                list.MarkFailed("No reference number returned.");
                PersistListState(list);
            }

            RefreshReferenceNumbersFromLists();
            RaiseListStateProperties();

            var completed = processableLists.Count(list => list.IsSuccessState);
            var failed = processableLists.Count(list => list.IsFailedState);

            ProcessStatus = failed == 0
                ? $"Completed. {completed}/{processableLists.Count} list(s) processed successfully."
                : $"Completed with issues. Success: {completed}, Failed: {failed}.";
            AppendProcessingLog(ProcessStatus);

            if (failed == 0)
            {
                _notificationService?.Success("Lists Processed", $"{completed} list(s) completed.");
            }
            else
            {
                _notificationService?.Warning("Lists Partially Processed", $"Success: {completed}, Failed: {failed}.");
            }

            if (failed == 0 && completed > 0)
            {
                await EnsureMissingReportsAsync();

                var runReferences = GetAllSuccessReferences();

                if (runReferences.Count > 0)
                {
                    await HandlePostProcessPromptsAsync(runReferences);
                }
            }
            else if (failed > 0 && completed > 0)
            {
                var message = "Some lists failed. Retry failed lists to enable printing and payslips.";
                ProcessStatus = message;
                AppendProcessingLog(message);
                _notificationService?.Info("Printing On Hold", message);
            }
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Error: {ex.Message}";
            AppendProcessingLog(ProcessStatus);
            _notificationService?.Error("Processing Error", ex.Message);
        }
        finally
        {
            IsProcessing = false;
            RaiseListStateProperties();
        }
    }

    private async Task HandlePostProcessPromptsAsync(IReadOnlyList<string> runReferences)
    {
        try
        {
            var runReports = await _reportsService.GetReportsByReferencesAsync(runReferences);
            if (runReports.Count == 0)
            {
                return;
            }

            var reportsWithPdf = runReports.Where(report => report.HasPdf).ToList();
            var shouldPrintReports = false;

            if (reportsWithPdf.Count > 0)
            {
                var printList = BuildConfirmListItems(runReferences, runReports);

                shouldPrintReports = await ConfirmListPrompt.Handle(new ConfirmListDialogRequest(
                    "Print Reports?",
                    $"Print all {reportsWithPdf.Count} report PDF(s) for the lists below? This will print 2 copies of each report.",
                    printList,
                    "Print 2 Copies",
                    "Skip")).ToTask();
            }

            if (shouldPrintReports)
            {
                var printedCount = 0;
                foreach (var report in reportsWithPdf)
                {
                    if (string.IsNullOrWhiteSpace(report.PdfPath))
                    {
                        continue;
                    }

                    var (printed, _) = await _reportsService.PrintPdfAsync(report.PdfPath, 2);
                    if (printed)
                    {
                        printedCount++;
                    }
                }

                if (printedCount > 0)
                {
                    _notificationService?.Success("Printing Started", $"Queued {printedCount} report PDF(s) for printing (2 copies).");
                }
                else
                {
                    _notificationService?.Warning("Print Skipped", "No printable PDFs were found for today.");
                }
            }

            var payslipList = BuildConfirmListItems(runReferences, runReports);

            var shouldGeneratePayslips = await ConfirmListPrompt.Handle(new ConfirmListDialogRequest(
                "Generate Payslips?",
                "Generate payslips for the lists below and print 1 copy?",
                payslipList,
                "Generate & Print",
                "Not Now")).ToTask();

            if (!shouldGeneratePayslips)
            {
                return;
            }

            var (generated, message, outputPdfPath) = await _reportsService.GeneratePayslipsAsync(runReports);
            if (!generated)
            {
                _notificationService?.Error("Payslip Failed", message);
                return;
            }

            if (!string.IsNullOrWhiteSpace(outputPdfPath))
            {
                var (printed, printMessage) = await _reportsService.PrintPdfAsync(outputPdfPath, 1);
                if (printed)
                {
                    _notificationService?.Success("Payslip Printed", "Payslip generated and sent to printer (1 copy).");
                }
                else
                {
                    _notificationService?.Warning("Payslip Generated", printMessage);
                }
            }
        }
        catch (UnhandledInteractionException<ConfirmDialogRequest, bool>)
        {
            // Ignore prompts if the view is not available.
        }
        catch (UnhandledInteractionException<ConfirmListDialogRequest, bool>)
        {
            // Ignore prompts if the view is not available.
        }
    }

    private async Task EnsureMissingReportsAsync()
    {
        var allReferences = GetAllSuccessReferences();

        if (allReferences.Count == 0)
        {
            return;
        }

        var missingRefs = allReferences
            .Where(reference =>
            {
                var pdfPath = Path.Combine(_reportsService.PdfDirectoryPath, $"{reference}.pdf");
                return !File.Exists(pdfPath);
            })
            .ToList();

        if (missingRefs.Count == 0)
        {
            return;
        }

        var startMessage = $"Downloading {missingRefs.Count} missing report(s)...";
        ProcessStatus = startMessage;
        AppendProcessingLog(startMessage);
        _notificationService?.Info("Downloading Reports", startMessage);

        var result = await _pythonService.GenerateReportsFromReferencesAsync(
            missingRefs,
            progress =>
            {
                if (string.IsNullOrWhiteSpace(progress))
                {
                    return;
                }
                var line = progress.Trim();
                ProcessStatus = line;
                AppendProcessingLog(line);
            });

        if (!result.Success)
        {
            var error = FirstMeaningfulLine(result.ErrorMessage);
            var message = string.IsNullOrWhiteSpace(error)
                ? "Failed to download missing reports."
                : error;
            ProcessStatus = message;
            AppendProcessingLog(message);
            _notificationService?.Error("Report Download Failed", message);
            return;
        }

        var successMessage = $"Generated {missingRefs.Count} missing report(s).";
        ProcessStatus = successMessage;
        AppendProcessingLog(successMessage);
        _notificationService?.Success("Reports Ready", successMessage);
    }

    private List<string> GetAllSuccessReferences()
    {
        return Lists
            .Where(list => list.IsSuccessState && !string.IsNullOrWhiteSpace(list.ReferenceNumber))
            .Select(list => list.ReferenceNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ConfirmListItem> BuildConfirmListItems(
        IReadOnlyList<string> references,
        IReadOnlyList<DailyListReport> reports)
    {
        var normalized = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byReference = reports
            .Where(report => !string.IsNullOrWhiteSpace(report.ReferenceNumber))
            .GroupBy(report => report.ReferenceNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Timestamp).First())
            .ToDictionary(item => item.ReferenceNumber.Trim(), item => item, StringComparer.OrdinalIgnoreCase);

        var items = new List<ConfirmListItem>();
        foreach (var reference in normalized)
        {
            if (byReference.TryGetValue(reference, out var report))
            {
                var listLabel = report.ListIndex > 0 ? $"List {report.ListIndex}" : "List -";
                var timestampLabel = report.Timestamp == default
                    ? "-"
                    : report.Timestamp.ToString("dd-MMM HH:mm");
                items.Add(new ConfirmListItem(reference, listLabel, timestampLabel, report.AccountCount));
            }
            else
            {
                items.Add(new ConfirmListItem(reference, "List -", "-", 0));
            }
        }

        return items
            .OrderBy(item => item.TimestampLabel == "-" ? 1 : 0)
            .ThenBy(item => item.TimestampLabel)
            .ThenBy(item => item.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ListPanelViewModel list)
        {
            return;
        }

        if (e.PropertyName is nameof(ListPanelViewModel.RunState)
            or nameof(ListPanelViewModel.ReferenceNumber)
            or nameof(ListPanelViewModel.FailureReason))
        {
            PersistListState(list);
            RefreshReferenceNumbersFromLists();
            RaiseListStateProperties();
        }

        if (e.PropertyName is nameof(ListPanelViewModel.RunState)
            or nameof(ListPanelViewModel.ReferenceNumber)
            or nameof(ListPanelViewModel.FailureReason)
            or nameof(ListPanelViewModel.PendingAccountNo)
            or nameof(ListPanelViewModel.PendingInstallmentText)
            or nameof(ListPanelViewModel.SelectedPaymentMode))
        {
            ScheduleAutoSave();
        }
    }

    private void OnListStateChanged()
    {
        ScheduleAutoSave();
    }

    private void OnDatabaseChanged(object? sender, EventArgs e)
    {
        _ = RefreshDatabaseStatusAsync();
    }

    private ListPanelViewModel CreateListPanel(int listNumber, bool applyPersistedState)
    {
        var list = new ListPanelViewModel(
            Math.Max(1, listNumber),
            _databaseService,
            _validationService,
            (panel, accountNo, installment) => AddSingleAccountToListAsync(panel, accountNo, installment));

        list.PropertyChanged += OnListPropertyChanged;
        list.StateChanged += OnListStateChanged;

        if (applyPersistedState)
        {
            ApplyPersistedState(list);
        }

        return list;
    }

    private void ClearListPanels()
    {
        foreach (var list in Lists)
        {
            list.PropertyChanged -= OnListPropertyChanged;
            list.StateChanged -= OnListStateChanged;
        }

        Lists.Clear();
        ReferenceNumbers.Clear();
        RaiseListStateProperties();
    }

    private void RenumberLists()
    {
        for (var index = 0; index < Lists.Count; index++)
        {
            Lists[index].UpdateListNumber(index + 1);
        }
    }

    private static void ApplySnapshotStatus(ListPanelViewModel list, LotListSnapshot snapshot)
    {
        var status = (snapshot.Status ?? string.Empty).Trim();
        if (status.Equals(nameof(ListRunState.Success), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(snapshot.ReferenceNumber))
        {
            list.MarkSuccess(snapshot.ReferenceNumber.Trim());
            return;
        }

        if (status.Equals(nameof(ListRunState.Failed), StringComparison.OrdinalIgnoreCase))
        {
            list.MarkFailed(snapshot.FailureReason);
            return;
        }

        list.MarkPending();
    }

    private void RaiseListStateProperties()
    {
        this.RaisePropertyChanged(nameof(HasFailedLists));
        this.RaisePropertyChanged(nameof(CanProcessLists));
        this.RaisePropertyChanged(nameof(CanRetryFailedLists));
        this.RaisePropertyChanged(nameof(ShowRetryFailedWarning));
    }

    private void RaiseDatabaseGuardProperties()
    {
        this.RaisePropertyChanged(nameof(IsDatabaseFresh));
        this.RaisePropertyChanged(nameof(CanEditLists));
        this.RaisePropertyChanged(nameof(CanProcessLists));
        this.RaisePropertyChanged(nameof(CanRetryFailedLists));
        this.RaisePropertyChanged(nameof(ShowDatabaseGuard));
        this.RaisePropertyChanged(nameof(ShowOverrideButton));
        this.RaisePropertyChanged(nameof(ShowOverrideWarning));
        this.RaisePropertyChanged(nameof(DatabaseStatusTitle));
        this.RaisePropertyChanged(nameof(DatabaseStatusMessage));
    }

    private (ListPanelViewModel list, ListItem item)? FindOpenListDuplicate(string accountNo)
    {
        var normalized = (accountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var list in Lists)
        {
            var item = list.Items.FirstOrDefault(existing =>
                string.Equals(existing.AccountNo?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                return (list, item);
            }
        }

        return null;
    }

    private async Task HighlightDuplicateAsync(ListPanelViewModel targetList, ListItem targetItem)
    {
        var version = ++_duplicateHighlightVersion;

        foreach (var list in Lists)
        {
            list.ClearDuplicateFocus();
        }

        targetList.IsDuplicateFocus = true;
        targetItem.IsDuplicateFocus = true;

        try
        {
            await DuplicateFocusPrompt.Handle(new DuplicateFocusRequest(targetList.ListNumber, targetItem.AccountNo)).ToTask();
        }
        catch (UnhandledInteractionException<DuplicateFocusRequest, Unit>)
        {
            // Ignore if the view is not available.
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (version != _duplicateHighlightVersion)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                targetItem.IsDuplicateFocus = false;
                targetList.IsDuplicateFocus = false;
            });
        });
    }

    private void ClearProcessingLogs()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProcessingLogs.Clear();
            this.RaisePropertyChanged(nameof(HasProcessingLogs));
        });
    }

    private void AppendProcessingLog(string message)
    {
        var line = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            ProcessingLogs.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            while (ProcessingLogs.Count > MaxProcessLogEntries)
            {
                ProcessingLogs.RemoveAt(0);
            }

            this.RaisePropertyChanged(nameof(HasProcessingLogs));
        });
    }

    private void RefreshReferenceNumbersFromLists()
    {
        var refs = Lists
            .Where(list => list.IsSuccessState && !string.IsNullOrWhiteSpace(list.ReferenceNumber))
            .Select(list => list.ReferenceNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReferenceNumbers.Clear();
        foreach (var reference in refs)
        {
            ReferenceNumbers.Add(reference);
        }
    }

    private void ApplyPersistedStatesToLists()
    {
        foreach (var list in Lists)
        {
            ApplyPersistedState(list);
        }
    }

    private void ApplyPersistedState(ListPanelViewModel list)
    {
        var signature = list.GetPayloadSignature();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return;
        }

        if (!_persistedStates.TryGetValue(signature, out var state))
        {
            return;
        }

        switch (state.Status)
        {
            case nameof(ListRunState.Success):
                if (!string.IsNullOrWhiteSpace(state.ReferenceNumber))
                {
                    list.MarkSuccess(state.ReferenceNumber);
                }
                break;

            case nameof(ListRunState.Failed):
                list.MarkFailed(state.FailureReason);
                break;
        }
    }

    private void PersistListState(ListPanelViewModel list)
    {
        var signature = list.GetPayloadSignature();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return;
        }

        if (list.IsSuccessState)
        {
            _persistedStates[signature] = new PersistedListRunState
            {
                Signature = signature,
                Status = nameof(ListRunState.Success),
                ReferenceNumber = list.ReferenceNumber,
                FailureReason = string.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            };
            SavePersistedState();
            return;
        }

        if (list.IsFailedState)
        {
            _persistedStates[signature] = new PersistedListRunState
            {
                Signature = signature,
                Status = nameof(ListRunState.Failed),
                ReferenceNumber = string.Empty,
                FailureReason = list.FailureReason,
                UpdatedAtUtc = DateTime.UtcNow
            };
            SavePersistedState();
            return;
        }

        if (_persistedStates.Remove(signature))
        {
            SavePersistedState();
        }
    }

    private void PersistNeutralListState(ListPanelViewModel list)
    {
        var signature = list.GetPayloadSignature();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return;
        }

        if (_persistedStates.Remove(signature))
        {
            SavePersistedState();
        }
    }

    private void LoadPersistedState()
    {
        _persistedStates.Clear();

        if (!File.Exists(_processingStatePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_processingStatePath);
            var state = JsonSerializer.Deserialize<ListProcessingStateStore>(json);
            if (state?.States == null)
            {
                return;
            }

            foreach (var item in state.States)
            {
                if (string.IsNullOrWhiteSpace(item.Signature))
                {
                    continue;
                }

                _persistedStates[item.Signature] = item;
            }
        }
        catch
        {
            // Ignore invalid state files.
        }
    }

    private void SavePersistedState()
    {
        try
        {
            var state = new ListProcessingStateStore
            {
                States = _persistedStates.Values
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_processingStatePath, json);
        }
        catch
        {
            // Ignore persistence failures.
        }
    }

    private LotSnapshot BuildLotSnapshot()
    {
        return new LotSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            PendingAslaasUpdates = _pendingAslaasUpdates
                .Select(entry => new LotAslaasSnapshot
                {
                    AccountNo = entry.Key,
                    AslaasNo = entry.Value
                })
                .OrderBy(entry => entry.AccountNo, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Lists = Lists.Select(list => new LotListSnapshot
            {
                ListNumber = list.ListNumber,
                PaymentMode = list.SelectedPaymentMode,
                Status = list.RunState.ToString(),
                ReferenceNumber = list.ReferenceNumber,
                FailureReason = list.FailureReason,
                PendingAccountNo = list.PendingAccountNo,
                PendingInstallmentText = list.PendingInstallmentText,
                Items = list.Items.Select(item => new LotItemSnapshot
                {
                    AccountNo = item.AccountNo,
                    Installment = item.EffectiveInstallment
                }).ToList()
            }).ToList()
        };
    }

    private static bool HasMeaningfulSnapshotContent(LotSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        if (snapshot.PendingAslaasUpdates.Any())
        {
            return true;
        }

        foreach (var list in snapshot.Lists)
        {
            if (list.Items.Any())
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(list.PendingAccountNo))
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleAutoSave()
    {
        if (_isRestoringAutoSave)
        {
            return;
        }

        _hasPendingAutoSave = true;
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        await PersistAutoSaveAsync();
    }

    private async Task PersistAutoSaveAsync()
    {
        if (_isRestoringAutoSave || _isAutoSaving || !_hasPendingAutoSave)
        {
            return;
        }

        _isAutoSaving = true;
        try
        {
            var snapshot = BuildLotSnapshot();
            if (!HasMeaningfulSnapshotContent(snapshot))
            {
                _hasPendingAutoSave = false;
                DeleteAutoSaveSnapshot();
                return;
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempPath = $"{_autoSaveSnapshotPath}.tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _autoSaveSnapshotPath, true);
            _hasPendingAutoSave = false;
            LastAutoSaveAt = DateTime.Now;
        }
        catch
        {
            // Ignore autosave failures; user workflow should continue.
        }
        finally
        {
            _isAutoSaving = false;
        }
    }

    private void DeleteAutoSaveSnapshot()
    {
        try
        {
            if (File.Exists(_autoSaveSnapshotPath))
            {
                File.Delete(_autoSaveSnapshotPath);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private async Task RestoreAutoSaveAsync()
    {
        if (!File.Exists(_autoSaveSnapshotPath) || IsProcessing)
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_autoSaveSnapshotPath);
            var snapshot = JsonSerializer.Deserialize<LotSnapshot>(json);
            if (!HasMeaningfulSnapshotContent(snapshot))
            {
                DeleteAutoSaveSnapshot();
                return;
            }

            _isRestoringAutoSave = true;
            var savedLists = snapshot?.Lists?
                .OrderBy(list => list.ListNumber)
                .ToList() ?? new List<LotListSnapshot>();

            ClearListPanels();
            _pendingAslaasUpdates.Clear();
            if (snapshot?.PendingAslaasUpdates != null)
            {
                foreach (var pending in snapshot.PendingAslaasUpdates)
                {
                    if (string.IsNullOrWhiteSpace(pending.AccountNo))
                    {
                        continue;
                    }

                    _pendingAslaasUpdates[pending.AccountNo.Trim()] = NormalizeAslaasValue(pending.AslaasNo);
                }
            }

            var skippedAccounts = new List<string>();
            foreach (var saved in savedLists)
            {
                var listNumber = saved.ListNumber > 0 ? saved.ListNumber : Lists.Count + 1;
                var list = CreateListPanel(listNumber, applyPersistedState: false);
                list.SelectedPaymentMode = saved.PaymentMode;
                Lists.Add(list);

                foreach (var savedItem in saved.Items)
                {
                    if (string.IsNullOrWhiteSpace(savedItem.AccountNo))
                    {
                        continue;
                    }

                    var wasAdded = await AddSingleAccountToListAsync(
                        list,
                        savedItem.AccountNo.Trim(),
                        Math.Max(1, savedItem.Installment),
                        skipAdvanceConfirmation: true,
                        skipDatabaseGuard: true);

                    if (!wasAdded)
                    {
                        skippedAccounts.Add(savedItem.AccountNo.Trim());
                    }
                }

                list.PendingAccountNo = saved.PendingAccountNo;
                list.PendingInstallmentText = string.IsNullOrWhiteSpace(saved.PendingInstallmentText)
                    ? "1"
                    : saved.PendingInstallmentText;
                ApplySnapshotStatus(list, saved);
                PersistListState(list);
            }

            if (Lists.Count == 0)
            {
                AddNewList();
            }

            RefreshReferenceNumbersFromLists();
            RaiseListStateProperties();

            ProcessStatus = skippedAccounts.Count == 0
                ? $"Restored {Lists.Count} checkpoint list(s)."
                : $"Restored {Lists.Count} checkpoint list(s); skipped {skippedAccounts.Count} invalid/duplicate account(s).";
            _notificationService?.Info("Checkpoint Restored", ProcessStatus);
            _hasPendingAutoSave = false;
        }
        catch
        {
            // Ignore invalid autosave files.
        }
        finally
        {
            _isRestoringAutoSave = false;
        }
    }

    private async Task CleanupSavedLotSnapshotsAsync()
    {
        try
        {
            var retentionDays = await GetSavedLotRetentionDaysAsync();
            if (retentionDays <= 0)
            {
                retentionDays = 1;
            }

            await CleanupSnapshotIfExpiredAsync(_lotSnapshotPath, retentionDays);
            await CleanupSnapshotIfExpiredAsync(_autoSaveSnapshotPath, retentionDays);
            await CleanupOldLotCheckpointsAsync(retentionDays);
        }
        catch
        {
            // Ignore retention cleanup failures.
        }
    }

    private async Task<int> GetSavedLotRetentionDaysAsync()
    {
        var raw = await _databaseService.GetAppSettingAsync(SavedLotRetentionDaysKey);
        return ParsePositiveRetentionDays(raw, 1);
    }

    private static int ParsePositiveRetentionDays(string? raw, int defaultValue)
    {
        if (int.TryParse((raw ?? string.Empty).Trim(), out var days) && days > 0)
        {
            return Math.Min(days, 3650);
        }

        return defaultValue;
    }

    private async Task CleanupSnapshotIfExpiredAsync(string snapshotPath, int retentionDays)
    {
        if (!File.Exists(snapshotPath))
        {
            return;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<LotSnapshot>(await File.ReadAllTextAsync(snapshotPath));
            var generatedAt = snapshot?.GeneratedAtUtc;
            var generatedDate = generatedAt == null || generatedAt == default
                ? File.GetLastWriteTimeUtc(snapshotPath)
                : DateTime.SpecifyKind(generatedAt.Value, DateTimeKind.Utc);

            var cutoff = DateTime.UtcNow.Date.AddDays(1 - retentionDays);
            if (generatedDate.Date >= cutoff)
            {
                return;
            }

            File.Delete(snapshotPath);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private void LoadLatestSavedLotStatus()
    {
        var latestSnapshotPath = GetLatestSavedLotSnapshotPath();
        if (string.IsNullOrWhiteSpace(latestSnapshotPath))
        {
            return;
        }

        var snapshotPath = latestSnapshotPath;
        try
        {
            var snapshot = JsonSerializer.Deserialize<LotSnapshot>(File.ReadAllText(snapshotPath));
            var generatedAtUtc = snapshot?.GeneratedAtUtc;
            if (generatedAtUtc.HasValue && generatedAtUtc.Value != default)
            {
                LastSavedLotAt = generatedAtUtc.Value.ToLocalTime();
            }
            else
            {
                LastSavedLotAt = File.GetLastWriteTime(snapshotPath);
            }
        }
        catch
        {
            LastSavedLotAt = File.Exists(snapshotPath) ? File.GetLastWriteTime(snapshotPath) : null;
        }
    }

    private string? GetLatestSavedLotSnapshotPath()
    {
        try
        {
            var candidates = new List<string>();
            if (File.Exists(_lotSnapshotPath))
            {
                candidates.Add(_lotSnapshotPath);
            }

            if (Directory.Exists(_lotCheckpointDirectory))
            {
                candidates.AddRange(Directory.EnumerateFiles(_lotCheckpointDirectory, "lot_*.json"));
            }

            return candidates
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return File.Exists(_lotSnapshotPath) ? _lotSnapshotPath : null;
        }
    }

    private async Task CleanupOldLotCheckpointsAsync()
    {
        var retentionDays = await GetSavedLotRetentionDaysAsync();
        await CleanupOldLotCheckpointsAsync(retentionDays);
    }

    private Task CleanupOldLotCheckpointsAsync(int retentionDays)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_lotCheckpointDirectory))
                {
                    return;
                }

                var cutoff = DateTime.UtcNow.Date.AddDays(1 - Math.Max(1, retentionDays));
                foreach (var file in Directory.EnumerateFiles(_lotCheckpointDirectory, "lot_*.json"))
                {
                    try
                    {
                        var writeTime = File.GetLastWriteTimeUtc(file);
                        if (writeTime.Date < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // Ignore checkpoint cleanup failures.
                    }
                }
            }
            catch
            {
                // Ignore checkpoint cleanup failures.
            }
        });
    }

    private void ReconcileFromReferenceLog(IEnumerable<ListPanelViewModel> lists)
    {
        var latestBySignature = LoadLatestReferencesBySignature();
        if (latestBySignature.Count == 0)
        {
            return;
        }

        foreach (var list in lists)
        {
            if (!list.HasProcessableItems || list.IsSuccessState)
            {
                continue;
            }

            var signature = list.GetPayloadSignature();
            if (string.IsNullOrWhiteSpace(signature))
            {
                continue;
            }

            if (!latestBySignature.TryGetValue(signature, out var referenceInfo))
            {
                continue;
            }

            var pdfPath = Path.Combine(_reportsService.PdfDirectoryPath, $"{referenceInfo.ReferenceNumber}.pdf");
            if (File.Exists(pdfPath))
            {
                list.MarkSuccess(referenceInfo.ReferenceNumber);
                PersistListState(list);
            }
        }
    }

    private Dictionary<string, ReferenceBySignature> LoadLatestReferencesBySignature()
    {
        var result = new Dictionary<string, ReferenceBySignature>(StringComparer.Ordinal);
        if (!File.Exists(_referencesFilePath))
        {
            return result;
        }

        string content;
        try
        {
            content = File.ReadAllText(_referencesFilePath);
        }
        catch
        {
            return result;
        }

        foreach (Match match in ReferenceEntryRegex.Matches(content))
        {
            var reference = match.Groups["reference"].Value.Trim();
            var signature = NormalizeAccountSignature(match.Groups["accounts"].Value);
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(signature))
            {
                continue;
            }

            var timestampRaw = match.Groups["timestamp"].Value.Trim();
            var timestamp = DateTime.MinValue;
            DateTime.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp);

            if (!result.TryGetValue(signature, out var existing) || timestamp >= existing.Timestamp)
            {
                result[signature] = new ReferenceBySignature
                {
                    Signature = signature,
                    ReferenceNumber = reference,
                    Timestamp = timestamp
                };
            }
        }

        return result;
    }

    private static string NormalizeAccountSignature(string rawAccounts)
    {
        if (string.IsNullOrWhiteSpace(rawAccounts))
        {
            return string.Empty;
        }

        var tokens = AccountTokenRegex
            .Matches(rawAccounts)
            .Select(match => match.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(",", tokens);
    }

    private static string FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Processing failed.";
        }

        var lines = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return "Processing failed.";
        }

        var hasTraceback = lines.Any(line =>
            line.StartsWith("Traceback (most recent call last):", StringComparison.OrdinalIgnoreCase));

        if (hasTraceback)
        {
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (line.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("File \"", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("During handling of the above exception", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line;
            }
        }

        return lines[0];
    }

    private async Task<IReadOnlyCollection<DopChequeInputItem>?> CollectDopChequeInputsAsync(
        IReadOnlyList<ListPanelViewModel> processableLists)
    {
        var result = new List<DopChequeInputItem>();
        var (lastBankName, lastChequeNo, lastPaymentAccountNo) = await GetDopChequeDefaultsAsync();

        for (var index = 0; index < processableLists.Count; index++)
        {
            var list = processableLists[index];
            var paymentModeToken = list.PaymentModeToken;
            var isDopChequeMode = string.Equals(paymentModeToken, "dop_cheque", StringComparison.Ordinal);
            var isNonDopChequeMode = string.Equals(paymentModeToken, "non_dop_cheque", StringComparison.Ordinal);
            if (!isDopChequeMode && !isNonDopChequeMode)
            {
                continue;
            }

            var dopChequeItems = list.Items
                .Where(item => item.AccountDetails != null && !string.IsNullOrWhiteSpace(item.AccountNo))
                .ToList();

            foreach (var item in dopChequeItems)
            {
                var response = await DopChequePrompt.Handle(new DopChequePromptRequest
                {
                    ListNumber = list.ListNumber,
                    ListName = list.Name,
                    AccountNo = item.AccountNo,
                    AccountName = item.AccountNameDisplay,
                    Installment = item.EffectiveInstallment,
                    PaymentModeToken = paymentModeToken,
                    RequireChequeNo = true,
                    SuggestedBankName = lastBankName,
                    SuggestedChequeNo = lastChequeNo,
                    SuggestedPaymentAccountNo = lastPaymentAccountNo
                }).ToTask();

                if (response == null)
                {
                    return null;
                }

                var chequeNo = (response.ChequeNo ?? string.Empty).Trim();
                var paymentAccountNo = (response.PaymentAccountNo ?? string.Empty).Trim();
                var bankName = (response.BankName ?? string.Empty).Trim();
                var accountNo = (response.AccountNo ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(accountNo) ||
                    string.IsNullOrWhiteSpace(chequeNo) ||
                    string.IsNullOrWhiteSpace(paymentAccountNo))
                {
                    return null;
                }

                if (isNonDopChequeMode && string.IsNullOrWhiteSpace(bankName))
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(bankName))
                {
                    lastBankName = bankName;
                }
                if (!string.IsNullOrWhiteSpace(chequeNo))
                {
                    lastChequeNo = chequeNo;
                }
                lastPaymentAccountNo = paymentAccountNo;

                result.Add(new DopChequeInputItem
                {
                    ListIndex = index + 1,
                    AccountNo = accountNo,
                    BankName = bankName,
                    ChequeNo = chequeNo,
                    PaymentAccountNo = paymentAccountNo,
                    PaymentModeToken = paymentModeToken
                });
            }
        }

        if (result.Count > 0)
        {
            await SaveDopChequeDefaultsAsync(lastBankName, lastChequeNo, lastPaymentAccountNo);
        }

        return result;
    }

    private async Task<(string bankName, string chequeNo, string paymentAccountNo)> GetDopChequeDefaultsAsync()
    {
        try
        {
            var bankName = (await _databaseService.GetAppSettingAsync(DopChequeDefaultBankNameKey) ?? string.Empty).Trim();
            var chequeNo = (await _databaseService.GetAppSettingAsync(DopChequeDefaultChequeNoKey) ?? string.Empty).Trim();
            var paymentAccountNo = (await _databaseService.GetAppSettingAsync(DopChequeDefaultPaymentAccountNoKey) ?? string.Empty).Trim();
            return (bankName, chequeNo, paymentAccountNo);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private async Task SaveDopChequeDefaultsAsync(string bankName, string chequeNo, string paymentAccountNo)
    {
        var normalizedBankName = (bankName ?? string.Empty).Trim();
        var normalizedChequeNo = (chequeNo ?? string.Empty).Trim();
        var normalizedPaymentAccountNo = (paymentAccountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBankName) &&
            string.IsNullOrWhiteSpace(normalizedChequeNo) &&
            string.IsNullOrWhiteSpace(normalizedPaymentAccountNo))
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(normalizedBankName))
            {
                await _databaseService.SaveAppSettingAsync(DopChequeDefaultBankNameKey, normalizedBankName);
            }

            if (!string.IsNullOrWhiteSpace(normalizedChequeNo))
            {
                await _databaseService.SaveAppSettingAsync(DopChequeDefaultChequeNoKey, normalizedChequeNo);
            }

            if (!string.IsNullOrWhiteSpace(normalizedPaymentAccountNo))
            {
                await _databaseService.SaveAppSettingAsync(DopChequeDefaultPaymentAccountNoKey, normalizedPaymentAccountNo);
            }
        }
        catch
        {
            // Ignore persistence failures; prompt flow can continue.
        }
    }

    private sealed class ListProcessingStateStore
    {
        public List<PersistedListRunState> States { get; set; } = new();
    }

    private sealed class PersistedListRunState
    {
        public string Signature { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class ReferenceBySignature
    {
        public string Signature { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    private sealed class LotSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public List<LotAslaasSnapshot> PendingAslaasUpdates { get; set; } = new();
        public List<LotListSnapshot> Lists { get; set; } = new();
    }

    private sealed class LotListSnapshot
    {
        public int ListNumber { get; set; }
        public string PaymentMode { get; set; } = "Cash";
        public string Status { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public string PendingAccountNo { get; set; } = string.Empty;
        public string PendingInstallmentText { get; set; } = "1";
        public List<LotItemSnapshot> Items { get; set; } = new();
    }

    private sealed class LotItemSnapshot
    {
        public string AccountNo { get; set; } = string.Empty;
        public int Installment { get; set; } = 1;
    }

    private sealed class LotAslaasSnapshot
    {
        public string AccountNo { get; set; } = string.Empty;
        public string AslaasNo { get; set; } = "APPLIED";
    }
}
