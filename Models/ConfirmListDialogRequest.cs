using System.Collections.Generic;

namespace AgentBuddy.Models;

public sealed record ConfirmListItem(
    string ReferenceNumber,
    string ListLabel,
    string TimestampLabel
);

public sealed record ConfirmListDialogRequest(
    string Title,
    string Message,
    IReadOnlyList<ConfirmListItem> Items,
    string YesText = "Yes",
    string NoText = "No"
);
