using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

public sealed record DueReminderItem
{
    public string AccountNo { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string MonthPaidUpto { get; init; } = string.Empty;
    public string NextDueDate { get; init; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ReminderMessage { get; init; } = string.Empty;
    public string WhatsAppUrl { get; init; } = string.Empty;
}

public sealed class WhatsAppReminderService
{
    private readonly DatabaseService _databaseService;

    public WhatsAppReminderService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Gets all active accounts that are currently due or pending for the current cycle
    /// </summary>
    public async Task<List<DueReminderItem>> GetDueRemindersAsync(int dueWithinDays = 30)
    {
        var accounts = await _databaseService.GetAllActiveAccountsAsync();
        var dueList = accounts
            .Where(a => a.IsDueWithinDays(dueWithinDays) || a.MonthPaidUptoNumber < DateTime.Now.Month)
            .OrderBy(a => a.AccountName)
            .ToList();

        var agentName = await _databaseService.GetAgentNameAsync() ?? "Your Post Office Agent";

        var result = new List<DueReminderItem>();
        foreach (var acc in dueList)
        {
            var amount = acc.GetAmount();
            var message = $"Dear {acc.AccountName}, this is a gentle reminder that your Post Office RD Account (A/C: {acc.AccountNo}) monthly installment of ₹{amount:N0} is due for {acc.MonthPaidUpto}. Kindly arrange payment. Thank you! - {agentName}";

            var cleanPhone = SanitizePhoneNumber(acc.ShortCode);
            var encodedMsg = Uri.EscapeDataString(message);
            var waUrl = !string.IsNullOrWhiteSpace(cleanPhone)
                ? $"https://wa.me/{cleanPhone}?text={encodedMsg}"
                : $"https://wa.me/?text={encodedMsg}";

            result.Add(new DueReminderItem
            {
                AccountNo = acc.AccountNo,
                AccountName = acc.AccountName,
                Amount = amount,
                MonthPaidUpto = acc.MonthPaidUpto,
                NextDueDate = acc.NextInstallmentDate,
                PhoneNumber = cleanPhone,
                ReminderMessage = message,
                WhatsAppUrl = waUrl,
            });
        }

        return result;
    }

    /// <summary>
    /// Open the WhatsApp Web / App link cross-platform (macOS & Windows)
    /// </summary>
    public static void LaunchWhatsAppUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private static string SanitizePhoneNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return $"91{digits}";
        if (digits.Length > 10) return digits;
        return string.Empty;
    }
}
