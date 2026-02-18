using System.Collections.Generic;

namespace AgentBuddy.Models;

/// <summary>
/// Aggregated metrics for dashboard display
/// </summary>
public class DashboardMetrics
{
    public int TotalAccounts { get; set; }
    public decimal TotalAmount { get; set; }
    
    // Account Categories
    public int DefaultAccounts { get; set; }
    public int MatureAccounts { get; set; }
    public int AboutToFreezeAccounts { get; set; }
    public int AdvancedAccounts { get; set; }
    public int NewlyOpenedAccounts { get; set; }
    
    // First Half Summary
    public int FirstHalfPendingCount { get; set; }
    public decimal FirstHalfPendingAmount { get; set; }
    public int FirstHalfDepositedCount { get; set; }
    public decimal FirstHalfDepositedAmount { get; set; }
    
    // Second Half Summary
    public int SecondHalfPendingCount { get; set; }
    public decimal SecondHalfPendingAmount { get; set; }
    public int SecondHalfDepositedCount { get; set; }
    public decimal SecondHalfDepositedAmount { get; set; }
    
    // Collection Status
    public int CollectionDoneButDepositPendingCount { get; set; }
    public decimal CollectionDoneButDepositPendingAmount { get; set; }
    public int CollectionPendingButDepositDoneCount { get; set; }
    public decimal CollectionPendingButDepositDoneAmount { get; set; }
    
    // Monthly Revenue
    public List<MonthlyRevenue> MonthlyRevenues { get; set; } = new();
    
    // Accounts due soon
    public List<RDAccount> AccountsDueSoon { get; set; } = new();
}

/// <summary>
/// Monthly revenue data for charts
/// </summary>
public class MonthlyRevenue
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal Commission { get; set; }
    public decimal Rebate { get; set; }
    public int DefaultCount { get; set; }
    
    public decimal Total => Commission + Rebate;
}

/// <summary>
/// Category data for pie chart
/// </summary>
public class CategoryData
{
    public string CategoryName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string Color { get; set; } = string.Empty;
}
