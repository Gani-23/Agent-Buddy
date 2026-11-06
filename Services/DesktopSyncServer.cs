using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Lightweight local Wi-Fi / Hotspot HTTP server to sync Account data & IDs to the Android app.
/// </summary>
public sealed class DesktopSyncServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly DatabaseService _databaseService;
    public const int DefaultPort = 5055;

    public DesktopSyncServer(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public bool IsRunning => _listener?.IsListening == true;

    public string? ServerUrl { get; private set; }
    public string? AccountsUrl { get; private set; }
    public string? LocalIp { get; private set; }

    /// <summary>
    /// Re-evaluates active network adapters and updates the current IP and URLs.
    /// Can be called at any time (even while the server is running) to adapt to dynamic DHCP changes.
    /// </summary>
    public void RefreshEndpoints(int port = DefaultPort)
    {
        LocalIp = GetLocalIpAddress() ?? "127.0.0.1";
        ServerUrl = $"http://{LocalIp}:{port}/";
        AccountsUrl = $"http://{LocalIp}:{port}/accounts";
    }

    public void Start(int port = DefaultPort)
    {
        RefreshEndpoints(port);

        if (IsRunning) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception)
        {
            // If wildcard fails due to OS permissions, bind specifically to local IP
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://{LocalIp}:{port}/");
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();

                _cts = new CancellationTokenSource();
                Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Stop();
                System.Diagnostics.Debug.WriteLine($"[DesktopSyncServer] Failed to start: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        finally
        {
            _listener = null;
            _cts = null;
            ServerUrl = null;
            AccountsUrl = null;
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // listener closed or error
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            // Add CORS headers for web/mobile cross-origin requests
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept");

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
            if (path == "/accounts" || path == "/sync")
            {
                var accounts = await _databaseService.GetAllAccountsAsync();
                var dtoList = accounts.Select(a => new
                {
                    account_no = a.AccountNo,
                    account_name = a.AccountName,
                    aslaas_no = a.AslaasNo,
                    denomination = a.Denomination,
                    month_paid_upto = a.MonthPaidUpto,
                    next_installment_date = a.NextInstallmentDate,
                    amount = (long)a.Amount,
                    month_paid_upto_num = a.MonthPaidUptoNumber,
                    next_due_date_iso = a.NextDueDateIso,
                    total_deposit = (long)a.TotalDeposit,
                    status = a.Status,
                    is_active = a.IsActive ? 1 : 0,
                    short_code = a.ShortCode
                }).ToList();

                var json = JsonSerializer.Serialize(dtoList);
                var bytes = Encoding.UTF8.GetBytes(json);

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else if (path == "/ids")
            {
                var mappings = await _databaseService.GetAccountShortCodeMappingsAsync();
                var json = JsonSerializer.Serialize(mappings);
                var bytes = Encoding.UTF8.GetBytes(json);

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else if (path == "" || path == "/status")
            {
                var count = (await _databaseService.GetAllAccountsAsync()).Count;
                var info = new
                {
                    service = "AgentBuddy Desktop Sync",
                    status = "online",
                    accounts_count = count,
                    endpoints = new[] { "/accounts", "/ids", "/status" }
                };
                var json = JsonSerializer.Serialize(info);
                var bytes = Encoding.UTF8.GetBytes(json);

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = 500;
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = errBytes.Length;
                await context.Response.OutputStream.WriteAsync(errBytes, 0, errBytes.Length);
            }
            catch { }
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }

    public static string? GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        // Fallback: enumerate interfaces prioritizing physical Wi-Fi / Ethernet
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            // First try non-virtual interfaces
            foreach (var ni in interfaces)
            {
                if (IsVirtualOrBridge(ni)) continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }

            // Fallback to any active non-loopback
            foreach (var ni in interfaces)
            {
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    private static bool IsVirtualOrBridge(NetworkInterface ni)
    {
        var name = ni.Name.ToLowerInvariant();
        var desc = ni.Description.ToLowerInvariant();
        return name.StartsWith("vmenet") ||
               name.StartsWith("bridge") ||
               name.StartsWith("utun") ||
               name.StartsWith("awdl") ||
               name.StartsWith("llw") ||
               desc.Contains("virtual") ||
               desc.Contains("vmware") ||
               desc.Contains("parallels");
    }

    /// <summary>
    /// Discovers all available endpoints (Live Wi-Fi IP, Mac Bonjour Hostname, and other interfaces)
    /// for seamless dynamic Wi-Fi pairing with the mobile app.
    /// </summary>
    public static List<NetworkEndpointOption> GetAvailableEndpoints(int port = DefaultPort)
    {
        var list = new List<NetworkEndpointOption>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary active Dynamic IP (via active default gateway)
        var primaryIp = GetLocalIpAddress();
        if (!string.IsNullOrEmpty(primaryIp) && primaryIp != "127.0.0.1")
        {
            list.Add(new NetworkEndpointOption(
                $"Wi-Fi (Dynamic: {primaryIp})",
                primaryIp,
                $"http://{primaryIp}:{port}/accounts",
                true,
                "Recommended: Fast direct local Wi-Fi pairing"
            ));
            seenHosts.Add(primaryIp);
        }

        // 2. Bonjour / mDNS Hostname (.local) - persistent across DHCP reassignments
        try
        {
            var hostName = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                var mdnsHost = hostName.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                    ? hostName
                    : $"{hostName}.local";

                list.Add(new NetworkEndpointOption(
                    $"Mac Hostname ({mdnsHost})",
                    mdnsHost,
                    $"http://{mdnsHost}:{port}/accounts",
                    false,
                    "Zero-Config Bonjour: Remains constant even when DHCP assigns a new IP"
                ));
                seenHosts.Add(mdnsHost);
            }
        }
        catch { }

        // 3. Other active physical network interfaces (e.g. Hotspot, Ethernet)
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    IsVirtualOrBridge(ni))
                {
                    continue;
                }

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        var ipStr = ip.Address.ToString();
                        if (seenHosts.Add(ipStr))
                        {
                            var nicName = !string.IsNullOrWhiteSpace(ni.Name) ? ni.Name : "Network";
                            list.Add(new NetworkEndpointOption(
                                $"{nicName} ({ipStr})",
                                ipStr,
                                $"http://{ipStr}:{port}/accounts",
                                true,
                                $"Active physical interface: {ni.Description}"
                            ));
                        }
                    }
                }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add(new NetworkEndpointOption(
                "Localhost (127.0.0.1)",
                "127.0.0.1",
                $"http://127.0.0.1:{port}/accounts",
                false,
                "Loopback only"
            ));
        }

        return list;
    }

    public void Dispose()
    {
        Stop();
    }
}
