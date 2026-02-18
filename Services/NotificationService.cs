using System;

namespace AgentBuddy.Services;

public enum AppNotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record AppNotification(string Title, string Message, AppNotificationType Type);

public sealed class NotificationService
{
    public event Action<AppNotification>? NotificationRaised;

    public void Info(string title, string message) => Raise(title, message, AppNotificationType.Info);

    public void Success(string title, string message) => Raise(title, message, AppNotificationType.Success);

    public void Warning(string title, string message) => Raise(title, message, AppNotificationType.Warning);

    public void Error(string title, string message) => Raise(title, message, AppNotificationType.Error);

    public void Raise(string title, string message, AppNotificationType type)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        NotificationRaised?.Invoke(new AppNotification(title.Trim(), message.Trim(), type));
    }
}
