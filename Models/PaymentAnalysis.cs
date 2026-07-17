using System;

namespace AgentBuddy.Models;

public enum PaymentClassification
{
    CurrentMonth,
    AdvancePayment,
    CatchUpPayment,
    MixedCatchUpAndAdvance,
    LongOverdueResolved,
    PartialCatchUp,
    LongOverduePartialCatchUp,
    MissingDueDate
}

public sealed record PaymentAnalysis(
    PaymentClassification Classification,
    DateTime CurrentMonth,
    DateTime? DueMonth,
    int EnteredInstallments,
    int OverdueMonths,
    int CatchUpInstallments,
    int AdvanceInstallments,
    int RemainingOverdueInstallments)
{
    public bool HasAdvancePortion => AdvanceInstallments > 0;

    public bool RemainsOverdueAfterPayment => RemainingOverdueInstallments > 0;

    public DateTime? NextDueMonthAfterPayment => DueMonth?.AddMonths(EnteredInstallments);

    public string DueMonthDisplay => DueMonth?.ToString("MMM yyyy") ?? "Unavailable";

    public string NextDueMonthAfterPaymentDisplay => NextDueMonthAfterPayment?.ToString("MMM yyyy") ?? "Unavailable";

    public string CategoryLabel => Classification switch
    {
        PaymentClassification.CurrentMonth => "Current month",
        PaymentClassification.AdvancePayment => "Advance payment",
        PaymentClassification.CatchUpPayment => "Catch-up payment",
        PaymentClassification.MixedCatchUpAndAdvance => "Catch-up + advance",
        PaymentClassification.LongOverdueResolved => "Long overdue account",
        PaymentClassification.PartialCatchUp => "Still overdue after payment",
        PaymentClassification.LongOverduePartialCatchUp => "Long overdue and still pending",
        PaymentClassification.MissingDueDate => "Due date unavailable",
        _ => "Review needed"
    };

    public string Summary => Classification switch
    {
        PaymentClassification.CurrentMonth => "Covers the current due month only.",
        PaymentClassification.AdvancePayment => BuildAdvanceSummary(),
        PaymentClassification.CatchUpPayment => $"Clears {FormatInstallments(CatchUpInstallments)} up to the current month.",
        PaymentClassification.MixedCatchUpAndAdvance =>
            $"Clears {FormatInstallments(CatchUpInstallments)} and adds {FormatInstallments(AdvanceInstallments)} in advance.",
        PaymentClassification.LongOverdueResolved => BuildLongOverdueSummary(),
        PaymentClassification.PartialCatchUp =>
            $"Pays {FormatInstallments(EnteredInstallments)}, but {FormatInstallments(RemainingOverdueInstallments)} remain overdue.",
        PaymentClassification.LongOverduePartialCatchUp =>
            $"Long-overdue account: {FormatInstallments(RemainingOverdueInstallments)} will remain overdue after this payment.",
        PaymentClassification.MissingDueDate => "Next due date is unavailable. Review the account before processing.",
        _ => "Review this payment before processing."
    };

    public static PaymentAnalysis CreateMissingDueDate(int enteredInstallments, DateTime currentMonth)
    {
        return new PaymentAnalysis(
            PaymentClassification.MissingDueDate,
            currentMonth,
            null,
            Math.Max(1, enteredInstallments),
            0,
            0,
            0,
            0);
    }

    private string BuildAdvanceSummary()
    {
        if (DueMonth.HasValue && DueMonth.Value > CurrentMonth)
        {
            return $"Account is already ahead. All {FormatInstallments(AdvanceInstallments)} are advance payments.";
        }

        return $"Includes {FormatInstallments(AdvanceInstallments)} beyond the current month.";
    }

    private string BuildLongOverdueSummary()
    {
        if (HasAdvancePortion)
        {
            return $"Long-overdue account: clears {FormatInstallments(CatchUpInstallments)} and adds {FormatInstallments(AdvanceInstallments)} in advance.";
        }

        return $"Long-overdue account: clears {FormatInstallments(CatchUpInstallments)} up to the current month.";
    }

    private static string FormatInstallments(int count)
    {
        var safeCount = Math.Max(0, count);
        return safeCount == 1 ? "1 installment" : $"{safeCount} installments";
    }
}
