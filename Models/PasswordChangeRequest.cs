namespace AgentBuddy.Models;

public sealed record PasswordChangeRequest(
    string AgentId,
    string CurrentPassword,
    string NewPassword
);
