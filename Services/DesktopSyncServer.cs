using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

/// <summary>
/// Lightweight local Wi-Fi / Hotspot HTTP server to sync Account IDs & Short Codes to the Android app.
/// </summary>
public sealed class DesktopSyncServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly DatabaseService _databaseService;
    private const int DefaultPort = 5055;

    public DesktopSyncServer(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public bool IsRunning => _listener?.IsListening == true;

    public string? ServerUrl { get; private set; }

    public void Start(int port = DefaultPort)
    {
        if (IsRunning) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            var localIp = GetLocalIpAddress() ?? "127.0.0.1";
            ServerUrl = $"http://{localIp}:{port}/ids";

            Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception)
        {
            // If wildcard fails due to permissions, fallback to localhost/direct IP
            try
            {
                _listener = new HttpListener();
                var localIp = GetLocalIpAddress() ?? "127.0.0.1";
                _listener.Prefixes.Add($"http://{localIp}:{port}/");
                _listener.Start();
                _cts = new CancellationTokenSource();
                ServerUrl = $"http://{localIp}:{port}/ids";
                Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch
            {
                Stop();
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
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
            if (path == "/ids")
            {
                var mappings = await _databaseService.GetAccountShortCodeMappingsAsync();
                var json = JsonSerializer.Serialize(mappings);
                var bytes = Encoding.UTF8.GetBytes(json);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
        catch
        {
            context.Response.StatusCode = 500;
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }

    private static string? GetLocalIpAddress()
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
        return null;
    }

    public void Dispose()
    {
        Stop();
    }
}
