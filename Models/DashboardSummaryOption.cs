using System;

namespace AgentBuddy.Models;

public sealed record DashboardSummaryOption(string SegmentKey, string Title, string Hint)
{
    public override string ToString() => Title;
}

public sealed record DashboardSortOption(string Key, string Title)
{
    public override string ToString() => Title;
}
