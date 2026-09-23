using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

/// <summary>
/// Cross-platform abstraction for DOP portal automation (desktop Python vs on-device Android/iOS WebView).
/// </summary>
public interface IPortalAutomationService
{
    /// <summary>
    /// Checks if this service can run on the current device platform.
    /// </summary>
    bool CanRunOnDevice { get; }

    /// <summary>
    /// Executes account fetch from the DOP portal and returns success status and count.
    /// </summary>
    Task<(bool success, int fetchedCount, string message)> FetchAccountsAsync(
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Static provider to allow platform heads (e.g. AgentBuddy.Android) to supply platform-specific automation services.
/// </summary>
public static class PortalAutomationProvider
{
    public static Func<DatabaseService, IPortalAutomationService>? Factory { get; set; }
}
