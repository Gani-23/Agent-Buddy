using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using AgentBuddy.Models;
using AgentBuddy.Services;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public class ReportsViewModel : ViewModelBase
{
    private readonly ReportsService _reportsService;
    private readonly NotificationService? _notificationService;

    private bool _isDarkTheme;
    private string _statusMessage = string.Empty;
    private int _printCopies = 2;
    private string _selectedPrinter = string.Empty;
    private bool _isSettingDefaultPrinter;

    public ReportsViewModel(ReportsService reportsService, NotificationService? notificationService = null)
    {
        _reportsService = reportsService;
        _notificationService = notificationService;

        TodayReports = new ObservableCollection<DailyListReport>();

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadTodayReportsAsync);
        ViewReportCommand = ReactiveCommand.CreateFromTask<DailyListReport?>(ViewReportAsync);
        PrintReportCommand = ReactiveCommand.CreateFromTask<DailyListReport?>(PrintReportAsync);
        PrintAllCommand = ReactiveCommand.CreateFromTask(PrintAllAsync);
        GeneratePayslipsCommand = ReactiveCommand.CreateFromTask(GeneratePayslipsAsync);
        RefreshPrintersCommand = ReactiveCommand.CreateFromTask(LoadPrintersAsync);
        SetDefaultPrinterCommand = ReactiveCommand.CreateFromTask(SetDefaultPrinterAsync);

        _ = LoadPrintersAsync();
        _ = LoadTodayReportsAsync();
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public int PrintCopies
    {
        get => _printCopies;
        set => this.RaiseAndSetIfChanged(ref _printCopies, Math.Max(1, value));
    }

    public IReadOnlyList<int> CopyOptions { get; } = Enumerable.Range(1, 10).ToList();

    public ObservableCollection<DailyListReport> TodayReports { get; }
    public ObservableCollection<string> PrinterOptions { get; } = new();

    public string SelectedPrinter
    {
        get => _selectedPrinter;
        set => this.RaiseAndSetIfChanged(ref _selectedPrinter, value);
    }

    public bool IsSettingDefaultPrinter
    {
        get => _isSettingDefaultPrinter;
        set => this.RaiseAndSetIfChanged(ref _isSettingDefaultPrinter, value);
    }

    public bool HasPrinterOptions => PrinterOptions.Count > 0;

    public bool HasReports => TodayReports.Count > 0;
    public bool HasPrintableReports => TodayReports.Any(item => item.HasPdf);

    public int TotalAccounts => TodayReports.Sum(item => item.AccountCount);

    public string SummaryText =>
        $"{DateTime.Today:dd-MMM-yyyy}: {TodayReports.Count} list(s), {TotalAccounts} account(s)";

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<DailyListReport?, Unit> ViewReportCommand { get; }
    public ReactiveCommand<DailyListReport?, Unit> PrintReportCommand { get; }
    public ReactiveCommand<Unit, Unit> PrintAllCommand { get; }
    public ReactiveCommand<Unit, Unit> GeneratePayslipsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshPrintersCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDefaultPrinterCommand { get; }

    public async Task LoadTodayReportsAsync()
    {
        var reports = await _reportsService.GetReportsForDateAsync(DateTime.Today);

        TodayReports.Clear();
        foreach (var report in reports)
        {
            TodayReports.Add(report);
        }

        if (reports.Count == 0)
        {
            StatusMessage = $"No list reports found for {DateTime.Today:dd-MMM-yyyy}.";
        }
        else
        {
            StatusMessage = $"Loaded {reports.Count} list report(s) for today.";
        }

        RaiseSummaryProperties();
    }

    public async Task ViewReportAsync(DailyListReport? report)
    {
        if (report == null)
        {
            return;
        }

        var (success, message) = await _reportsService.OpenPdfAsync(report.PdfPath);
        StatusMessage = success
            ? $"Opened {report.ReferenceNumber}."
            : $"{report.ReferenceNumber}: {message}";
        if (success)
        {
            _notificationService?.Info("Report Opened", report.ReferenceNumber);
        }
        else
        {
            _notificationService?.Error("Open Failed", StatusMessage);
        }
    }

    public async Task PrintReportAsync(DailyListReport? report)
    {
        if (report == null)
        {
            return;
        }

        var (success, message) = await _reportsService.PrintPdfAsync(report.PdfPath, PrintCopies);
        StatusMessage = $"{report.ReferenceNumber}: {message}";
        if (success)
        {
            _notificationService?.Info("Print Ready", $"{report.ReferenceNumber}: {message}");
        }
        else
        {
            _notificationService?.Error("Print Failed", StatusMessage);
        }
    }

    public async Task DeleteReportAsync(DailyListReport? report)
    {
        if (report == null)
        {
            return;
        }

        var (success, message) = await _reportsService.DeleteReportAsync(report);
        StatusMessage = $"{report.ReferenceNumber}: {message}";

        if (!success)
        {
            _notificationService?.Error("Delete Failed", StatusMessage);
            return;
        }

        TodayReports.Remove(report);
        RaiseSummaryProperties();

        if (TodayReports.Count == 0)
        {
            StatusMessage = $"Deleted {report.ReferenceNumber}. No list reports found for {DateTime.Today:dd-MMM-yyyy}.";
        }

        _notificationService?.Success("Report Deleted", report.ReferenceNumber);
    }

    public async Task PrintAllAsync()
    {
        var printableReports = TodayReports.Where(item => item.HasPdf).ToList();
        if (printableReports.Count == 0)
        {
            StatusMessage = "No printable PDF files found for today.";
            _notificationService?.Warning("No Printable PDFs", "There are no generated PDFs for today.");
            return;
        }

        var successCount = 0;
        var failedCount = 0;
        foreach (var report in printableReports)
        {
            var (success, _) = await _reportsService.PrintPdfAsync(report.PdfPath, PrintCopies);
            if (success)
            {
                successCount++;
            }
            else
            {
                failedCount++;
            }
        }

        StatusMessage = failedCount == 0
            ? $"Sent print command for {successCount}/{printableReports.Count} report(s)."
            : $"Batch print completed with issues: printed {successCount}/{printableReports.Count}.";
        _notificationService?.Info("Batch Print", StatusMessage);
    }

    public async Task GeneratePayslipsAsync()
    {
        var selectedReports = TodayReports
            .Where(item => item.IsSelectedForPayslip)
            .ToList();

        if (selectedReports.Count == 0)
        {
            StatusMessage = "Select one or more reports to generate payslips.";
            _notificationService?.Warning("No Selection", "Select at least one report first.");
            return;
        }

        var (success, message, outputPdfPath) = await _reportsService.GeneratePayslipsAsync(selectedReports);
        if (!success)
        {
            StatusMessage = message;
            _notificationService?.Error("Payslip Failed", message);
            return;
        }

        StatusMessage = $"{message} ({selectedReports.Count} selected, max 3 per page)";
        _notificationService?.Success("Payslip Ready", $"{selectedReports.Count} report(s) converted to payslip.");
        if (!string.IsNullOrWhiteSpace(outputPdfPath))
        {
            await _reportsService.OpenPdfAsync(outputPdfPath);
        }
    }

    public async Task LoadPrintersAsync()
    {
        try
        {
            var currentSelection = (SelectedPrinter ?? string.Empty).Trim();
            var printerOptions = await _reportsService.GetAvailablePrintersAsync();
            var preferredPrinter = (await _reportsService.GetEffectiveDefaultPrinterAsync()).Trim();

            PrinterOptions.Clear();
            foreach (var printer in printerOptions)
            {
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    PrinterOptions.Add(printer);
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredPrinter) &&
                PrinterOptions.All(item => !string.Equals(item, preferredPrinter, StringComparison.OrdinalIgnoreCase)))
            {
                PrinterOptions.Insert(0, preferredPrinter);
            }

            var nextSelection = !string.IsNullOrWhiteSpace(preferredPrinter)
                ? preferredPrinter
                : currentSelection;

            if (!string.IsNullOrWhiteSpace(nextSelection))
            {
                var matched = PrinterOptions.FirstOrDefault(item =>
                    string.Equals(item, nextSelection, StringComparison.OrdinalIgnoreCase));
                SelectedPrinter = matched ?? nextSelection;
            }
            else if (PrinterOptions.Count > 0)
            {
                SelectedPrinter = PrinterOptions[0];
            }
            else
            {
                SelectedPrinter = string.Empty;
                StatusMessage = "No printers detected on this system.";
            }

            this.RaisePropertyChanged(nameof(HasPrinterOptions));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load printers: {ex.Message}";
            this.RaisePropertyChanged(nameof(HasPrinterOptions));
        }
    }

    public async Task SetDefaultPrinterAsync()
    {
        if (IsSettingDefaultPrinter)
        {
            return;
        }

        var selected = (SelectedPrinter ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selected))
        {
            StatusMessage = "Select a printer first.";
            return;
        }

        IsSettingDefaultPrinter = true;
        try
        {
            var (success, message) = await _reportsService.SetDefaultPrinterAsync(selected);
            StatusMessage = message;

            if (success)
            {
                _notificationService?.Success("Printer Updated", selected);
                await LoadPrintersAsync();
            }
            else
            {
                _notificationService?.Error("Printer Update Failed", message);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set default printer: {ex.Message}";
            _notificationService?.Error("Printer Update Failed", StatusMessage);
        }
        finally
        {
            IsSettingDefaultPrinter = false;
        }
    }

    private void RaiseSummaryProperties()
    {
        this.RaisePropertyChanged(nameof(HasReports));
        this.RaisePropertyChanged(nameof(HasPrintableReports));
        this.RaisePropertyChanged(nameof(TotalAccounts));
        this.RaisePropertyChanged(nameof(SummaryText));
    }
}
