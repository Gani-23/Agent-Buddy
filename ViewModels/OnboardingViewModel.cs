using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using AgentBuddy.Models;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

/// <summary>
/// First-run setup wizard. Every step saves through the existing
/// <see cref="SettingsViewModel"/> logic so onboarding and Settings never drift apart.
/// </summary>
public sealed class OnboardingViewModel : ViewModelBase
{
    public enum Step
    {
        Welcome = 0,
        Language,
        Profile,
        DopLogin,
        License,
        Theme,
        RdRenewal,
        Tour,
        Finish
    }

    private readonly MainWindowViewModel _main;
    private int _stepIndex;
    private string _stepError = string.Empty;
    private bool _isBusy;
    private string _ownerMobile;
    private DateTime? _rdRenewalDate;
    private bool _hasSavedPassword;
    private bool _syncAfterSetup = true;
    private string _initialLanguageCode = string.Empty;

    public OnboardingViewModel(MainWindowViewModel main, bool isRerun)
    {
        _main = main;
        IsRerun = isRerun;
        _ownerMobile = main.DashboardViewModel.AgentPhoneNumber ?? string.Empty;
        _rdRenewalDate = TryParseDate(Settings.RdCertificateRenewalDateText);
        _initialLanguageCode = Settings.SelectedLanguage?.Code ?? string.Empty;

        StepItems = new ObservableCollection<OnboardingStepItem>
        {
            new("Welcome", "Let's get you set up"),
            new("Language", "Pick how the app talks"),
            new("Your profile", "Name & owner mobile"),
            new("DOP login", "India Post portal ID"),
            new("License", "Activate AgentBuddy"),
            new("Theme", "Choose your look"),
            new("RD renewal", "Certificate due date"),
            new("Quick tour", "What each page does"),
            new("All set", "First data sync"),
        };

        TourItems = new ObservableCollection<TourItem>
        {
            new("Icon_Dashboard", "Dashboard", "Your daily cockpit: portfolio totals, half-month pending and deposited accounts, risk insights and one-click WhatsApp reminders."),
            new("Icon_Lists", "Create Lists", "Build deposit lots from pending accounts, review totals and cash, then generate reference numbers and payslips."),
            new("Icon_Clock", "Fixed Deposits (TD)", "Track time deposits, maturity dates and interest so nothing renews unnoticed."),
            new("Icon_Reports", "Reports", "Every generated report and PDF in one place: search, reopen, print or share."),
            new("Icon_Support", "Support", "Help, FAQs and a direct line to the team when something looks off."),
            new("Icon_Settings", "Settings", "Credentials, license, language, printers, backups, updates and this setup wizard."),
            new("Icon_Bolt", "Balance button", "The floating button bottom-right jumps straight to overdue balance accounts from any page."),
            new("Icon_QrCode", "Sync Mobile (QR)", "On the Dashboard: scan the QR from the mobile app to sync accounts over Wi-Fi."),
        };

        var canGoBack = this.WhenAnyValue(x => x.StepIndex, x => x.IsBusy, (i, busy) => i > 0 && !busy);
        var notBusy = this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy);

        NextCommand = ReactiveCommand.CreateFromTask(NextAsync, notBusy);
        BackCommand = ReactiveCommand.Create(() => { StepError = string.Empty; StepIndex--; }, canGoBack);
        SkipCommand = ReactiveCommand.Create(() => { StepError = string.Empty; StepIndex++; }, notBusy);
        CheckPythonCommand = ReactiveCommand.CreateFromTask(async () => await Settings.CheckPythonCommand.Execute().ToTask());

        _ = LoadAsync();
    }

    public event EventHandler<bool>? Completed;

    public bool IsRerun { get; }
    public SettingsViewModel Settings => _main.SettingsViewModel;
    public ObservableCollection<OnboardingStepItem> StepItems { get; }
    public ObservableCollection<TourItem> TourItems { get; }

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }
    public ReactiveCommand<Unit, Unit> CheckPythonCommand { get; }

    public int StepIndex
    {
        get => _stepIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, StepItems.Count - 1);
            this.RaiseAndSetIfChanged(ref _stepIndex, clamped);
            for (var i = 0; i < StepItems.Count; i++)
            {
                StepItems[i].State = i < clamped ? StepState.Done : i == clamped ? StepState.Current : StepState.Upcoming;
            }

            this.RaisePropertyChanged(nameof(CurrentStep));
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(StepCounterText));
            this.RaisePropertyChanged(nameof(NextButtonText));
            this.RaisePropertyChanged(nameof(CanSkip));
            this.RaisePropertyChanged(nameof(IsFirstStep));
        }
    }

    public Step CurrentStep => (Step)StepIndex;
    public bool IsFirstStep => StepIndex == 0;
    public double ProgressPercent => StepIndex * 100.0 / (StepItems.Count - 1);
    public string StepCounterText => $"Step {StepIndex + 1} of {StepItems.Count}";

    public string NextButtonText => CurrentStep switch
    {
        Step.Welcome => "Get started",
        Step.Finish => SyncAfterSetup && CanSync ? "Finish & sync" : "Finish",
        _ => "Continue"
    };

    /// <summary>Optional steps: user can move on without saving.</summary>
    public bool CanSkip => CurrentStep is Step.License or Step.RdRenewal or Step.Tour
        || (CurrentStep == Step.DopLogin && _hasSavedPassword);

    public string StepError
    {
        get => _stepError;
        set
        {
            this.RaiseAndSetIfChanged(ref _stepError, value);
            this.RaisePropertyChanged(nameof(HasStepError));
        }
    }

    public bool HasStepError => !string.IsNullOrWhiteSpace(StepError);

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string OwnerMobile
    {
        get => _ownerMobile;
        set => this.RaiseAndSetIfChanged(ref _ownerMobile, value);
    }

    public DateTime? RdRenewalDate
    {
        get => _rdRenewalDate;
        set => this.RaiseAndSetIfChanged(ref _rdRenewalDate, value);
    }

    public bool HasSavedPassword
    {
        get => _hasSavedPassword;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasSavedPassword, value);
            this.RaisePropertyChanged(nameof(PasswordHint));
            this.RaisePropertyChanged(nameof(CanSkip));
        }
    }

    public string PasswordHint => HasSavedPassword
        ? "A password is already saved. Leave blank to keep it."
        : "Stored encrypted on this computer only. Used to sign in to the DOP portal for you.";

    public bool IsLicenseActive => _main.IsLicenseActive;

    public bool SyncAfterSetup
    {
        get => _syncAfterSetup;
        set
        {
            this.RaiseAndSetIfChanged(ref _syncAfterSetup, value);
            this.RaisePropertyChanged(nameof(NextButtonText));
        }
    }

    /// <summary>A first sync needs saved portal credentials.</summary>
    public bool CanSync => !string.IsNullOrWhiteSpace(Settings.AgentId) && (HasSavedPassword || !string.IsNullOrEmpty(Settings.EditablePassword));

    public string GreetingName => string.IsNullOrWhiteSpace(Settings.EditableAgentName)
        ? "Welcome aboard!"
        : $"You're all set, {Settings.EditableAgentName!.Trim().Split(' ')[0]}!";

    private async Task LoadAsync()
    {
        try
        {
            HasSavedPassword = await Settings.HasSavedPasswordAsync();
        }
        catch
        {
            HasSavedPassword = false;
        }

        _main.WhenAnyValue(x => x.IsLicenseActive).Subscribe(_ => this.RaisePropertyChanged(nameof(IsLicenseActive)));
    }

    private async Task NextAsync()
    {
        StepError = string.Empty;
        IsBusy = true;
        try
        {
            var ok = CurrentStep switch
            {
                Step.Language => await SaveLanguageAsync(),
                Step.Profile => await SaveProfileAsync(),
                Step.DopLogin => await SaveDopLoginAsync(),
                Step.License => await ActivateLicenseAsync(),
                Step.RdRenewal => await SaveRdRenewalAsync(),
                _ => true
            };

            if (!ok)
            {
                return;
            }

            if (CurrentStep == Step.Finish)
            {
                Completed?.Invoke(this, SyncAfterSetup && CanSync);
                return;
            }

            StepIndex++;
            if (CurrentStep == Step.Finish)
            {
                this.RaisePropertyChanged(nameof(CanSync));
                this.RaisePropertyChanged(nameof(GreetingName));
                this.RaisePropertyChanged(nameof(NextButtonText));
                if (string.IsNullOrWhiteSpace(Settings.PythonVersion) || Settings.PythonVersion == "Not installed")
                {
                    _ = CheckPythonCommand.Execute().ToTask();
                }
            }
        }
        catch (Exception ex)
        {
            StepError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> SaveLanguageAsync()
    {
        if (Settings.SelectedLanguage is null
            || string.Equals(Settings.SelectedLanguage.Code, _initialLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!await Settings.TrySaveLanguageAsync())
        {
            StepError = Settings.LanguageStatus;
            return false;
        }

        _initialLanguageCode = Settings.SelectedLanguage.Code;
        return true;
    }

    private async Task<bool> SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.EditableAgentName))
        {
            StepError = "Please enter your name so reminders and greetings can address you.";
            return false;
        }

        var digits = new string((OwnerMobile ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            digits = "91" + digits;
        }

        if (digits.Length != 12 || !digits.StartsWith("91", StringComparison.Ordinal))
        {
            StepError = "Enter a 10-digit Indian mobile number (WhatsApp reminders are sent from it).";
            return false;
        }

        OwnerMobile = digits;
        Settings.OwnerPhoneNumber = digits;
        await Settings.SaveAgentNameAsync();
        return true;
    }

    private async Task<bool> SaveDopLoginAsync()
    {
        var hasNewPassword = !string.IsNullOrEmpty(Settings.EditablePassword);
        if (!hasNewPassword && HasSavedPassword && !string.IsNullOrWhiteSpace(Settings.EditableAgentId))
        {
            // Keep the existing password; only the ID or name may have changed.
            if (!string.Equals(Settings.EditableAgentId?.Trim(), Settings.AgentId, StringComparison.Ordinal))
            {
                StepError = "Changing the DOP ID needs its password too.";
                return false;
            }

            return true;
        }

        if (!await Settings.TrySaveCredentialsAsync())
        {
            StepError = Settings.CredentialsStatus;
            return false;
        }

        HasSavedPassword = true;
        return true;
    }

    private async Task<bool> ActivateLicenseAsync()
    {
        if (IsLicenseActive && string.IsNullOrWhiteSpace(Settings.LicenseToken))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Settings.LicenseToken))
        {
            StepError = "Paste your license key, or choose Skip to do it later from Settings.";
            return false;
        }

        if (!await Settings.TryActivateLicenseAsync())
        {
            StepError = Settings.LicenseStatus;
            return false;
        }

        return true;
    }

    private async Task<bool> SaveRdRenewalAsync()
    {
        Settings.RdCertificateRenewalDateText = RdRenewalDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        if (!await Settings.TrySaveRdCertificateRenewalAsync())
        {
            StepError = Settings.RdCertificateRenewalStatus;
            return false;
        }

        return true;
    }

    private static DateTime? TryParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParseExact(text.Trim(), new[] { "dd-MMM-yyyy", "yyyy-MM-dd", "dd-MM-yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}

public enum StepState
{
    Upcoming,
    Current,
    Done
}

public sealed class OnboardingStepItem : ReactiveObject
{
    private StepState _state;

    public OnboardingStepItem(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }
    public string Subtitle { get; }

    public StepState State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(IsDone));
            this.RaisePropertyChanged(nameof(IsCurrent));
            this.RaisePropertyChanged(nameof(IsUpcoming));
        }
    }

    public bool IsDone => State == StepState.Done;
    public bool IsCurrent => State == StepState.Current;
    public bool IsUpcoming => State == StepState.Upcoming;
}

public sealed record TourItem(string IconKey, string Title, string Description);
