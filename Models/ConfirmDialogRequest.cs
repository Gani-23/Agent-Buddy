namespace AgentBuddy.Models;

public sealed record ConfirmDialogRequest(
    string Title,
    string Message,
    string YesText = "Yes",
    string NoText = "No"
);
