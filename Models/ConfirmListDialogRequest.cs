using System.Collections.Generic;

namespace AgentBuddy.Models;

public sealed record ConfirmListDialogRequest(
    string Title,
    string Message,
    IReadOnlyList<string> Items,
    string YesText = "Yes",
    string NoText = "No"
);
