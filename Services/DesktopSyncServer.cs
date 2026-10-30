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

    public void Start(int port = DefaultPort)
    {
        if (IsRunning) return;

        LocalIp = GetLocalIpAddress() ?? "127.0.0.1";
        ServerUrl = $"http://{LocalIp}:{port}/";
        AccountsUrl = $"http://{LocalIp}:{port}/accounts";

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

        // Fallback: enumerate interfaces
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

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

    public void Dispose()
    {
        Stop();
    }
}
