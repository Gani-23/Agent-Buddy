using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Service for calculating dashboard metrics from RD accounts
/// </summary>
public class MetricsCalculator
{
    private readonly DatabaseService _databaseService;

    public MetricsCalculator(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Calculate all dashboard metrics
    /// </summary>
    public async Task<DashboardMetrics> CalculateMetricsAsync()
    {
        var accounts = await _databaseService.GetAllActiveAccountsAsync();
        var metrics = new DashboardMetrics();

        metrics.TotalAccounts = accounts.Count;
        metrics.TotalAmount = accounts.Sum(a => a.GetAmount());

        CalculateCategories(accounts, metrics);
        CalculateHalfYearSummaries(accounts, metrics);

        // Prefer real revenue from imported reference history.
        var revenueFromReferences = await _databaseService.GetMonthlyRevenueDataAsync();
        metrics.MonthlyRevenues = revenueFromReferences.Count > 0
            ? revenueFromReferences
            : CalculateMonthlyAmountTrend(accounts);

        metrics.AccountsDueSoon = accounts.Where(a => a.IsDueWithinDays(30))
                                          .OrderBy(a => a.GetNextInstallmentDate())
                                          .ToList();

        return metrics;
    }

    /// <summary>
    /// Calculate account categories based on business rules
    /// </summary>
    private void CalculateCategories(List<RDAccount> accounts, DashboardMetrics metrics)
    {
        var currentDate = DateTime.Today;

        foreach (var account in accounts)
        {
            var nextDate = account.GetNextInstallmentDate();
            if (!nextDate.HasValue) continue;

            var monthsPaid = account.GetMonthPaidNumber();

            if (monthsPaid >= 40)
            {
                metrics.DefaultAccounts++;
            }
            else if (monthsPaid >= 22)
            {
                metrics.MatureAccounts++;
            }
            else if (nextDate.Value.Date < currentDate.AddMonths(-3))
            {
                metrics.AboutToFreezeAccounts++;
            }
            else if (nextDate.Value.Date > currentDate.AddMonths(1))
            {
                metrics.AdvancedAccounts++;
            }
            else if (monthsPaid == 1)
            {
                metrics.NewlyOpenedAccounts++;
            }
        }
    }

    /// <summary>
    /// Calculate first half and second half summaries
    /// </summary>
    private void CalculateHalfYearSummaries(List<RDAccount> accounts, DashboardMetrics metrics)
    {
        var today = DateTime.Today;

        foreach (var account in accounts)
        {
            var nextDate = account.GetNextInstallmentDate();
            if (!nextDate.HasValue) continue;

            var amount = account.GetAmount();
            var isPending = nextDate.Value.Date < today;
            var isFirstHalf = nextDate.Value.Month is >= 1 and <= 6;

            if (isFirstHalf)
            {
                if (isPending)
                {
                    metrics.FirstHalfPendingCount++;
                    metrics.FirstHalfPendingAmount += amount;
                }
                else
                {
                    metrics.FirstHalfDepositedCount++;
                    metrics.FirstHalfDepositedAmount += amount;
                }
            }
            else
            {
                if (isPending)
                {
                    metrics.SecondHalfPendingCount++;
                    metrics.SecondHalfPendingAmount += amount;
                }
                else
                {
                    metrics.SecondHalfDepositedCount++;
                    metrics.SecondHalfDepositedAmount += amount;
                }
            }
        }

        metrics.CollectionDoneButDepositPendingCount = 0;
        metrics.CollectionDoneButDepositPendingAmount = 0;
        metrics.CollectionPendingButDepositDoneCount = 0;
        metrics.CollectionPendingButDepositDoneAmount = 0;
    }

    /// <summary>
    /// Fallback monthly trend based on due amounts when reference revenue data is not available.
    /// </summary>
    private List<MonthlyRevenue> CalculateMonthlyAmountTrend(List<RDAccount> accounts)
    {
        var revenues = new List<MonthlyRevenue>();
        var currentDate = DateTime.Today;

        for (int i = 23; i >= 0; i--)
        {
            var targetDate = new DateTime(currentDate.Year, currentDate.Month, 1).AddMonths(-i);
            var year = targetDate.Year;
            var month = targetDate.Month;

            var accountsInMonth = accounts.Where(a =>
            {
                var nextDate = a.GetNextInstallmentDate();
                return nextDate.HasValue &&
                       nextDate.Value.Year == year &&
                       nextDate.Value.Month == month;
            }).ToList();

            var monthAmount = accountsInMonth.Sum(a => a.GetAmount());
            var overdueAmount = accountsInMonth.Sum(a =>
            {
                var dueDate = a.GetNextInstallmentDate();
                return dueDate.HasValue && dueDate.Value.Date < currentDate ? a.GetAmount() : 0;
            });

            revenues.Add(new MonthlyRevenue
            {
                Year = year,
                Month = month,
                MonthName = targetDate.ToString("MMM"),
                Commission = monthAmount,
                Rebate = overdueAmount,
                DefaultCount = accountsInMonth.Count(a => a.GetMonthPaidNumber() >= 40)
            });
        }

        return revenues;
    }

    /// <summary>
    /// Get category data for pie chart
    /// </summary>
    public async Task<List<CategoryData>> GetCategoryDataAsync()
    {
        var accounts = await _databaseService.GetAllActiveAccountsAsync();
        var currentDate = DateTime.Today;

        var categoryData = new List<CategoryData>();

        int defaultCount = 0, matureCount = 0, aboutToFreezeCount = 0;
        int advancedCount = 0, newlyOpenedCount = 0;
        decimal defaultAmount = 0, matureAmount = 0, aboutToFreezeAmount = 0;
        decimal advancedAmount = 0, newlyOpenedAmount = 0;

        foreach (var account in accounts)
        {
            var nextDate = account.GetNextInstallmentDate();
            if (!nextDate.HasValue) continue;

            var monthsPaid = account.GetMonthPaidNumber();
            var amount = account.GetAmount();

            if (monthsPaid >= 40)
            {
                defaultCount++;
                defaultAmount += amount;
            }
            else if (monthsPaid >= 22)
            {
                matureCount++;
                matureAmount += amount;
            }
            else if (nextDate.Value.Date < currentDate.AddMonths(-3))
            {
                aboutToFreezeCount++;
                aboutToFreezeAmount += amount;
            }
            else if (nextDate.Value.Date > currentDate.AddMonths(1))
            {
                advancedCount++;
                advancedAmount += amount;
            }
            else if (monthsPaid == 1)
            {
                newlyOpenedCount++;
                newlyOpenedAmount += amount;
            }
        }

        if (defaultCount > 0)
        {
            categoryData.Add(new CategoryData
            {
                CategoryName = "Default",
                Count = defaultCount,
                Amount = defaultAmount,
                Color = "#FFA500"
            });
        }

        if (matureCount > 0)
        {
            categoryData.Add(new CategoryData
            {
                CategoryName = "Mature",
                Count = matureCount,
                Amount = matureAmount,
                Color = "#E03E3E"
            });
        }

        if (aboutToFreezeCount > 0)
        {
            categoryData.Add(new CategoryData
            {
                CategoryName = "About To Freeze",
                Count = aboutToFreezeCount,
                Amount = aboutToFreezeAmount,
                Color = "#5DADE2"
            });
        }

        if (advancedCount > 0)
        {
            categoryData.Add(new CategoryData
            {
                CategoryName = "Advanced",
                Count = advancedCount,
                Amount = advancedAmount,
                Color = "#85C1E2"
            });
        }

        if (newlyOpenedCount > 0)
        {
            categoryData.Add(new CategoryData
            {
                CategoryName = "Newly Opened",
                Count = newlyOpenedCount,
                Amount = newlyOpenedAmount,
                Color = "#9B59B6"
            });
        }

        return categoryData;
    }
}
