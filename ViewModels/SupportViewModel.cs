using System.Reactive;
using System.Threading.Tasks;
using AgentBuddy.Services;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public class SupportViewModel : ViewModelBase
{
    private readonly ReportsService _reportsService;
    private readonly NotificationService? _notificationService;

    private bool _isDarkTheme;
    private string _statusMessage = string.Empty;

    public SupportViewModel(ReportsService reportsService, NotificationService? notificationService = null)
    {
        _reportsService = reportsService;
        _notificationService = notificationService;

        OpenEmailCommand = ReactiveCommand.CreateFromTask(OpenEmailAsync);
        OpenWhatsAppCommand = ReactiveCommand.CreateFromTask(OpenWhatsAppAsync);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string Name => "Saiganesh Angadi";
    public string Email => "saiganesh7989@gmail.com";
    public string Address => "Fort Road, 7/1/12 SiriNilayam";
    public string WhatsAppUrl => "https://wa.me/919182345999";
    public string QrImagePath => "avares://AgentBuddy/Assets/WhatsApp.png";

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _statusMessage, value);
            this.RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ReactiveCommand<Unit, Unit> OpenEmailCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenWhatsAppCommand { get; }

    private async Task OpenEmailAsync()
    {
        var (success, message) = await _reportsService.OpenLinkAsync($"mailto:{Email}");
        StatusMessage = success ? "Email app opened." : message;
        if (success)
        {
            _notificationService?.Info("Email", "Email app opened.");
        }
        else
        {
            _notificationService?.Error("Email Error", message);
        }
    }

    private async Task OpenWhatsAppAsync()
    {
        var (success, message) = await _reportsService.OpenLinkAsync(WhatsAppUrl);
        StatusMessage = success ? "WhatsApp chat opened." : message;
        if (success)
        {
            _notificationService?.Info("WhatsApp", "WhatsApp chat opened.");
        }
        else
        {
            _notificationService?.Error("WhatsApp Error", message);
        }
    }
}
