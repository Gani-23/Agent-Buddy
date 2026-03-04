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

    public ListPanelViewModel(
        int listNumber,
        DatabaseService databaseService,
        ValidationService validationService,
        Func<ListPanelViewModel, string, int, Task<bool>>? addAccountHandler = null)
    {
        ListNumber = Math.Max(1, listNumber);
        _name = $"List {ListNumber}";
        _databaseService = databaseService;
        _validationService = validationService;
        _addAccountHandler = addAccountHandler;

        Items = new ObservableCollection<ListItem>();
        Items.CollectionChanged += (_, _) =>
        {
            RecalculateTotals();
            ResetRunStateIfPayloadChanged();
        };

        AddPendingAccountCommand = ReactiveCommand.CreateFromTask(async () => { await SubmitPendingAsync(); });
        RemoveAccountCommand = ReactiveCommand.Create<ListItem?>(RemoveAccount);
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    public int ListNumber { get; }

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

        Items.Add(new ListItem
        {
            AccountNo = accountNo,
            Installment = installment,
            Status = status,
            AccountDetails = account
        });

        SetEntrySuccessMessage(status switch
        {
            AccountValidationStatus.DueSoon => $"{accountNo} added (due soon).",
            _ => $"{accountNo} added."
        });

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
        Name = $"List {ListNumber}";
    }

    public void MarkProcessing()
    {
        RunState = ListRunState.Processing;
        FailureReason = string.Empty;
        Name = $"List {ListNumber}";
    }

    public void MarkSuccess(string referenceNumber)
    {
        ReferenceNumber = (referenceNumber ?? string.Empty).Trim();
        FailureReason = string.Empty;
        RunState = ListRunState.Success;
        MarkAllAccountsProcessed();
        _lastProcessedSignature = GetPayloadSignature();
        _lastProcessedPaymentMode = SelectedPaymentMode;
        Name = string.IsNullOrWhiteSpace(ReferenceNumber) ? $"List {ListNumber}" : ReferenceNumber;
    }

    public void MarkFailed(string reason)
    {
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Processing failed." : reason.Trim();
        RunState = ListRunState.Failed;
        _lastProcessedSignature = GetPayloadSignature();
        _lastProcessedPaymentMode = SelectedPaymentMode;
        Name = $"List {ListNumber}";
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
            $"Suggested pending installments: {suggestedInstallments}. You can edit this value.";
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
}

public class ListManagementViewModel : ViewModelBase
{
    private const string DopChequeDefaultChequeNoKey = "dop_cheque_default_cheque_no";
    private const string DopChequeDefaultPaymentAccountNoKey = "dop_cheque_default_payment_account_no";

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
    private readonly NotificationService? _notificationService;
    private readonly string _processingStatePath;
    private readonly string _lotSnapshotPath;
    private readonly string _referencesFilePath;
    private readonly Dictionary<string, PersistedListRunState> _persistedStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingAslaasUpdates = new(StringComparer.OrdinalIgnoreCase);

    private bool _isDarkTheme;
    private bool _isProcessing;
    private string _processStatus = string.Empty;

    public ListManagementViewModel(
        DatabaseService databaseService,
        ValidationService validationService,
        PythonService pythonService,
        NotificationService? notificationService = null)
    {
        _databaseService = databaseService;
        _validationService = validationService;
        _pythonService = pythonService;
        _notificationService = notificationService;

        var stateDirectory = Path.Combine(AppPaths.BaseDirectory, "State");
        Directory.CreateDirectory(stateDirectory);
        _processingStatePath = Path.Combine(stateDirectory, "list_processing_state.json");
        _lotSnapshotPath = Path.Combine(stateDirectory, "list_lot_snapshot.json");
        _referencesFilePath = Path.Combine(AppPaths.BaseDirectory, "Reports", "references", "payment_references.txt");

        LoadPersistedState();

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

        AddNewList();
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

    public string ProcessStatus
    {
        get => _processStatus;
        set => this.RaiseAndSetIfChanged(ref _processStatus, value);
    }

    public bool HasFailedLists => Lists.Any(list => list.IsFailedState);
    public bool HasProcessingLogs => ProcessingLogs.Count > 0;

    public ObservableCollection<ListPanelViewModel> Lists { get; }
    public ObservableCollection<string> ReferenceNumbers { get; }
    public ObservableCollection<string> ProcessingLogs { get; }
    public Interaction<AslaasPromptRequest, string?> AslaasPrompt { get; } = new();
    public Interaction<DopChequePromptRequest, DopChequePromptResult?> DopChequePrompt { get; } = new();

    public ReactiveCommand<Unit, Unit> AddNewListCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveLotCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadLotCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteAllListsCommand { get; }
    public ReactiveCommand<Unit, Unit> ProcessAllListsCommand { get; }
    public ReactiveCommand<Unit, Unit> RetryFailedListsCommand { get; }

    private void AddNewList()
    {
        var index = Lists.Count + 1;
        var list = CreateListPanel(index, applyPersistedState: true);
        Lists.Add(list);
        RefreshReferenceNumbersFromLists();
        RaiseListStateProperties();
    }

    private void DeleteAllLists()
    {
        ClearListPanels();
        _pendingAslaasUpdates.Clear();
        _persistedStates.Clear();
        SavePersistedState();

        AddNewList();
    }

    private async Task SaveLotAsync()
    {
        try
        {
            var snapshot = new LotSnapshot
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
                    Items = list.Items.Select(item => new LotItemSnapshot
                    {
                        AccountNo = item.AccountNo,
                        Installment = item.EffectiveInstallment
                    }).ToList()
                }).ToList()
            };

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_lotSnapshotPath, json);
            ProcessStatus = $"Lot saved to: {_lotSnapshotPath}";
            _notificationService?.Success("Lot Saved", $"{snapshot.Lists.Count} list(s) saved.");
        }
        catch (Exception ex)
        {
            ProcessStatus = $"Save lot failed: {ex.Message}";
            _notificationService?.Error("Save Lot Failed", ex.Message);
        }
    }

    private async Task ReloadLotAsync()
    {
        if (IsProcessing)
        {
            ProcessStatus = "Cannot reload while processing is running.";
            return;
        }

        if (!File.Exists(_lotSnapshotPath))
        {
            ProcessStatus = "No saved lot found. Use Save Lot first.";
            _notificationService?.Warning("No Saved Lot", "Save a lot before reloading.");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_lotSnapshotPath);
            var snapshot = JsonSerializer.Deserialize<LotSnapshot>(json);
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
                        Math.Max(1, savedItem.Installment));

                    if (!wasAdded)
                    {
                        skippedAccounts.Add(savedItem.AccountNo.Trim());
                    }
                }

                ApplySnapshotStatus(list, saved);
                PersistListState(list);
            }

            if (Lists.Count == 0)
            {
                AddNewList();
            }

            ReconcileFromReferenceLog(Lists);
            RefreshReferenceNumbersFromLists();
            RaiseListStateProperties();

            ProcessStatus = skippedAccounts.Count == 0
                ? $"Reloaded {Lists.Count} list(s) from saved lot."
                : $"Reloaded {Lists.Count} list(s); skipped {skippedAccounts.Count} invalid/duplicate account(s).";
            _notificationService?.Info("Lot Reloaded", ProcessStatus);
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

    private async Task<bool> AddSingleAccountToListAsync(
        ListPanelViewModel list,
        string accountNo,
        int installment)
    {
        if (list == null)
        {
            return false;
        }

        var normalizedAccountNo = (accountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedAccountNo))
        {
            list.SetEntryErrorMessage("Enter an account number.");
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
                }
            }
        }

        return await list.AddAccountWithInstallmentAsync(normalizedAccountNo, installment, existingAccounts);
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

        ApplyPersistedStatesToLists();
        ReconcileFromReferenceLog(Lists);

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
    }

    private ListPanelViewModel CreateListPanel(int listNumber, bool applyPersistedState)
    {
        var list = new ListPanelViewModel(
            Math.Max(1, listNumber),
            _databaseService,
            _validationService,
            AddSingleAccountToListAsync);

        list.PropertyChanged += OnListPropertyChanged;

        if (applyPersistedState)
        {
            ApplyPersistedState(list);
            ReconcileFromReferenceLog(new[] { list });
        }

        return list;
    }

    private void ClearListPanels()
    {
        foreach (var list in Lists)
        {
            list.PropertyChanged -= OnListPropertyChanged;
        }

        Lists.Clear();
        ReferenceNumbers.Clear();
        RaiseListStateProperties();
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

            list.MarkSuccess(referenceInfo.ReferenceNumber);
            PersistListState(list);
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
        var (lastChequeNo, lastPaymentAccountNo) = await GetDopChequeDefaultsAsync();

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
                    RequireChequeNo = isDopChequeMode,
                    SuggestedChequeNo = lastChequeNo,
                    SuggestedPaymentAccountNo = lastPaymentAccountNo
                }).ToTask();

                if (response == null)
                {
                    return null;
                }

                var chequeNo = (response.ChequeNo ?? string.Empty).Trim();
                var paymentAccountNo = (response.PaymentAccountNo ?? string.Empty).Trim();
                var accountNo = (response.AccountNo ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(accountNo) ||
                    (isDopChequeMode && string.IsNullOrWhiteSpace(chequeNo)) ||
                    string.IsNullOrWhiteSpace(paymentAccountNo))
                {
                    return null;
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
                    ChequeNo = chequeNo,
                    PaymentAccountNo = paymentAccountNo,
                    PaymentModeToken = paymentModeToken
                });
            }
        }

        if (result.Count > 0)
        {
            await SaveDopChequeDefaultsAsync(lastChequeNo, lastPaymentAccountNo);
        }

        return result;
    }

    private async Task<(string chequeNo, string paymentAccountNo)> GetDopChequeDefaultsAsync()
    {
        try
        {
            var chequeNo = (await _databaseService.GetAppSettingAsync(DopChequeDefaultChequeNoKey) ?? string.Empty).Trim();
            var paymentAccountNo = (await _databaseService.GetAppSettingAsync(DopChequeDefaultPaymentAccountNoKey) ?? string.Empty).Trim();
            return (chequeNo, paymentAccountNo);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private async Task SaveDopChequeDefaultsAsync(string chequeNo, string paymentAccountNo)
    {
        var normalizedChequeNo = (chequeNo ?? string.Empty).Trim();
        var normalizedPaymentAccountNo = (paymentAccountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedChequeNo) && string.IsNullOrWhiteSpace(normalizedPaymentAccountNo))
        {
            return;
        }

        try
        {
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
