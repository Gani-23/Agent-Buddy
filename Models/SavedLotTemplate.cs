using System;
using System.Collections.Generic;

namespace AgentBuddy.Models;

public sealed class SavedLotTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string PaymentMode { get; set; } = "Cash";
    public List<SavedLotItemTemplate> Items { get; set; } = new();

    public int AccountCount => Items?.Count ?? 0;
    public string DisplaySummary => $"{AccountCount} account(s) • {PaymentMode} • {CreatedAtUtc.ToLocalTime():dd-MMM-yyyy}";
}

public sealed class SavedLotItemTemplate
{
    public string AccountNo { get; set; } = string.Empty;
    public int Installment { get; set; } = 1;
    public string? Nickname { get; set; }
}
