using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

/// <summary>
/// Desktop automation service using PythonService and Fetch_RDAccounts.py
/// </summary>
public sealed class DesktopPythonPortalAutomationService : IPortalAutomationService
{
    private readonly PythonService _pythonService;

    public DesktopPythonPortalAutomationService(PythonService pythonService)
    {
        _pythonService = pythonService;
    }

    public bool CanRunOnDevice => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                                  RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                  RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public async Task<(bool success, int fetchedCount, string message)> FetchAccountsAsync(
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        var (success, output) = await _pythonService.UpdateDatabaseAsync(statusCallback);
        return (success, 0, output);
    }
}
