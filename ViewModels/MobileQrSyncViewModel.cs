using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using AgentBuddy.Services;
using QRCoder;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class MobileQrSyncViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopSyncServer _syncServer;
    private readonly DatabaseService _databaseService;
    private readonly bool _ownsServer;

    private Bitmap? _qrBitmap;
    private string _syncUrl = string.Empty;
    private string _localIp = string.Empty;
    private int _port = DesktopSyncServer.DefaultPort;
    private int _accountsCount;
    private string _statusMessage = "Starting Wi-Fi server...";
    private bool _isCopied;

    public MobileQrSyncViewModel(DatabaseService databaseService, DesktopSyncServer? syncServer = null)
    {
        _databaseService = databaseService;
        _ownsServer = syncServer == null;
        _syncServer = syncServer ?? new DesktopSyncServer(_databaseService);

        _ = InitializeAsync();
    }

    public Bitmap? QrBitmap
    {
        get => _qrBitmap;
        private set => this.RaiseAndSetIfChanged(ref _qrBitmap, value);
    }

    public string SyncUrl
    {
        get => _syncUrl;
        private set => this.RaiseAndSetIfChanged(ref _syncUrl, value);
    }

    public string LocalIp
    {
        get => _localIp;
        private set => this.RaiseAndSetIfChanged(ref _localIp, value);
    }

    public int Port
    {
        get => _port;
        private set => this.RaiseAndSetIfChanged(ref _port, value);
    }

    public int AccountsCount
    {
        get => _accountsCount;
        private set => this.RaiseAndSetIfChanged(ref _accountsCount, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsCopied
    {
        get => _isCopied;
        set => this.RaiseAndSetIfChanged(ref _isCopied, value);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _syncServer.Start(Port);
            LocalIp = _syncServer.LocalIp ?? "127.0.0.1";
            SyncUrl = _syncServer.AccountsUrl ?? $"http://{LocalIp}:{Port}/accounts";

            var accounts = await _databaseService.GetAllAccountsAsync();
            AccountsCount = accounts.Count;

            GenerateQrBitmap(SyncUrl);
            StatusMessage = $"✓ Server ready. {AccountsCount} accounts available to sync.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Server initialization notice: {ex.Message}";
        }
    }

    private void GenerateQrBitmap(string url)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20);

            using var stream = new MemoryStream(qrCodeBytes);
            QrBitmap = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to render QR Code: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (_ownsServer)
        {
            _syncServer.Stop();
        }
        _qrBitmap?.Dispose();
    }
}
