using System;

namespace AgentBuddy.Services;

public record FixedDepositCalculationResult(
    string SchemeType,
    decimal Principal,
    decimal Rate,
    int TenureYears,
    decimal MaturityAmount,
    decimal TotalInterest,
    decimal PeriodicPayout,
    string PayoutFrequency,
    DateTime MaturityDate
);

public static class FixedDepositCalculator
{
    public static readonly (string Name, decimal Rate, int TenureYears)[] SupportedSchemes = new[]
    {
        ("1-Year TD", 6.9m, 1),
        ("2-Year TD", 7.0m, 2),
        ("3-Year TD", 7.1m, 3),
        ("5-Year TD", 7.5m, 5),
        ("MIS (Monthly Income)", 7.4m, 5),
        ("SCSS (Senior Citizen)", 8.2m, 5),
        ("NSC (5-Year)", 7.7m, 5),
        ("KVP (Doubles)", 7.5m, 10)
    };

    public static decimal GetDefaultRateForScheme(string schemeType)
    {
        return schemeType switch
        {
            "1-Year TD" => 6.9m,
            "2-Year TD" => 7.0m,
            "3-Year TD" => 7.1m,
            "5-Year TD" => 7.5m,
            "MIS (Monthly Income)" => 7.4m,
            "SCSS (Senior Citizen)" => 8.2m,
            "NSC (5-Year)" => 7.7m,
            "KVP (Doubles)" => 7.5m,
            _ => 7.5m
        };
    }

    public static int GetTenureYearsForScheme(string schemeType)
    {
        return schemeType switch
        {
            "1-Year TD" => 1,
            "2-Year TD" => 2,
            "3-Year TD" => 3,
            "5-Year TD" => 5,
            "MIS (Monthly Income)" => 5,
            "SCSS (Senior Citizen)" => 5,
            "NSC (5-Year)" => 5,
            "KVP (Doubles)" => 10,
            _ => 5
        };
    }

    public static FixedDepositCalculationResult Calculate(
        string schemeType,
        decimal principal,
        decimal? customRate = null,
        DateTime? startDate = null)
    {
        var rate = customRate ?? GetDefaultRateForScheme(schemeType);
        var tenure = GetTenureYearsForScheme(schemeType);
        var date = startDate ?? DateTime.Today;

        decimal maturityAmount;
        decimal totalInterest;
        decimal periodicPayout = 0;
        string payoutFrequency = "At Maturity";
        DateTime maturityDate = date.AddYears(tenure);

        if (schemeType.Contains("TD"))
        {
            // POTD: Compounded quarterly: A = P * (1 + R/400)^(4*T)
            var n = 4 * tenure;
            var rFactor = 1.0 + (double)rate / 400.0;
            var compoundFactor = Math.Pow(rFactor, n);
            maturityAmount = Math.Round(principal * (decimal)compoundFactor, 2);
            totalInterest = maturityAmount - principal;
            periodicPayout = Math.Round(principal * (rate / 100m), 2);
            payoutFrequency = "Annually (or Reinvested at Maturity)";
        }
        else if (schemeType.Contains("MIS"))
        {
            // Monthly Income Scheme: Payout is monthly, principal returned at maturity
            periodicPayout = Math.Round((principal * (rate / 100m)) / 12m, 2);
            totalInterest = periodicPayout * 12 * tenure;
            maturityAmount = principal;
            payoutFrequency = "Monthly";
        }
        else if (schemeType.Contains("SCSS"))
        {
            // Senior Citizen: Payout is quarterly, principal returned at maturity
            periodicPayout = Math.Round((principal * (rate / 100m)) / 4m, 2);
            totalInterest = periodicPayout * 4 * tenure;
            maturityAmount = principal;
            payoutFrequency = "Quarterly";
        }
        else if (schemeType.Contains("NSC"))
        {
            // NSC: Compounded annually
            var compoundFactor = Math.Pow(1.0 + (double)rate / 100.0, tenure);
            maturityAmount = Math.Round(principal * (decimal)compoundFactor, 2);
            totalInterest = maturityAmount - principal;
            payoutFrequency = "At Maturity";
        }
        else if (schemeType.Contains("KVP"))
        {
            // Doubles in 115 months (~9.58 years)
            maturityAmount = principal * 2m;
            totalInterest = principal;
            maturityDate = date.AddMonths(115);
            payoutFrequency = "At Maturity (Doubles in 115 months)";
        }
        else
        {
            // Fallback: simple/quarterly
            var n = 4 * tenure;
            var rFactor = 1.0 + (double)rate / 400.0;
            maturityAmount = Math.Round(principal * (decimal)Math.Pow(rFactor, n), 2);
            totalInterest = maturityAmount - principal;
        }

        return new FixedDepositCalculationResult(
            schemeType,
            principal,
            rate,
            tenure,
            maturityAmount,
            totalInterest,
            periodicPayout,
            payoutFrequency,
            maturityDate
        );
    }
}
