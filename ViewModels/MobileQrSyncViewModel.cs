using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentBuddy.Models;
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
    private string _statusMessage = "Starting Wi-Fi sync server...";
    private bool _isCopied;
    private string _copyButtonText = "Copy";
    private bool _isRefreshing;

    private ObservableCollection<NetworkEndpointOption> _availableEndpoints = new();
    private NetworkEndpointOption? _selectedEndpoint;

    public MobileQrSyncViewModel(DatabaseService databaseService, DesktopSyncServer? syncServer = null)
    {
        _databaseService = databaseService;
        _ownsServer = syncServer == null;
        _syncServer = syncServer ?? new DesktopSyncServer(_databaseService);

        _ = InitializeAsync();
    }

    public ObservableCollection<NetworkEndpointOption> AvailableEndpoints
    {
        get => _availableEndpoints;
        set => this.RaiseAndSetIfChanged(ref _availableEndpoints, value);
    }

    public NetworkEndpointOption? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEndpoint, value);
            if (value != null)
            {
                LocalIp = value.Host;
                SyncUrl = value.Url;
                GenerateQrBitmap(SyncUrl);
                StatusMessage = $"✓ Server ready on {value.Name}. {AccountsCount} accounts available to sync.";
            }
        }
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

    public string CopyButtonText
    {
        get => _copyButtonText;
        set => this.RaiseAndSetIfChanged(ref _copyButtonText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => this.RaiseAndSetIfChanged(ref _isRefreshing, value);
    }

    public async Task RefreshNetworkAsync()
    {
        IsRefreshing = true;
        try
        {
            _syncServer.RefreshEndpoints(Port);
            var (cloudUrl, cloudKey) = await _databaseService.GetMobileSyncSettingsAsync();
            var endpoints = DesktopSyncServer.GetAvailableEndpoints(Port, cloudUrl, cloudKey, _syncServer.SyncToken);
            AvailableEndpoints = new ObservableCollection<NetworkEndpointOption>(endpoints);

            var best = AvailableEndpoints.FirstOrDefault();
            SelectedEndpoint = best;

            var accounts = await _databaseService.GetAllAccountsAsync();
            AccountsCount = accounts.Count;

            StatusMessage = $"✓ Endpoints refreshed. {AccountsCount} accounts ready.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Network refresh notice: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _syncServer.Start(Port);

            var accounts = await _databaseService.GetAllAccountsAsync();
            AccountsCount = accounts.Count;

            var (cloudUrl, cloudKey) = await _databaseService.GetMobileSyncSettingsAsync();
            var endpoints = DesktopSyncServer.GetAvailableEndpoints(Port, cloudUrl, cloudKey, _syncServer.SyncToken);
            AvailableEndpoints = new ObservableCollection<NetworkEndpointOption>(endpoints);

            var best = AvailableEndpoints.FirstOrDefault();
            if (best != null)
            {
                SelectedEndpoint = best;
            }
            else
            {
                LocalIp = _syncServer.LocalIp ?? "127.0.0.1";
                SyncUrl = _syncServer.AccountsUrl ?? $"http://{LocalIp}:{Port}/accounts";
                GenerateQrBitmap(SyncUrl);
                StatusMessage = $"✓ Server ready. {AccountsCount} accounts available to sync.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Server initialization notice: {ex.Message}";
        }
    }

    public void OnCopied()
    {
        IsCopied = true;
        CopyButtonText = "Copied!";
        Task.Delay(2000).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsCopied = false;
                CopyButtonText = "Copy";
            });
        });
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
