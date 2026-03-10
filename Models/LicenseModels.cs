using System;

namespace AgentBuddy.Models;

public sealed record LicenseSettings
{
    public string ServerUrl { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? LastValidatedAtUtc { get; init; }
    public string Subject { get; init; } = string.Empty;
}

public sealed record LicenseStatus
{
    public bool IsActive { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ServerUrl { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? LastValidatedAtUtc { get; init; }
    public string Subject { get; init; } = string.Empty;
}

public sealed record LicenseActivationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public LicenseStatus Status { get; init; } = new();
}
