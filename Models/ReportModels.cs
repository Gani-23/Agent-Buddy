using System;

namespace AgentBuddy.Models;

public class DailyListReport
{
    public DateTime Timestamp { get; set; }
    public int ListIndex { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string AccountsRaw { get; set; } = string.Empty;
    public int AccountCount { get; set; }
    public string PdfPath { get; set; } = string.Empty;
    public bool HasPdf { get; set; }
    public bool IsSelectedForPayslip { get; set; }

    public string TimestampDisplay => Timestamp.ToString("hh:mm tt");
    public string DateDisplay => Timestamp.ToString("dd-MMM-yyyy");
    public string ListDisplay => $"List {ListIndex}";
}
