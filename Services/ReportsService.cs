using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentBuddy.Models;
using Microsoft.Data.Sqlite;

namespace AgentBuddy.Services;

public class ReportsService
{
    private const string PreferredPrinterSettingKey = "default_printer";
    private const string LegacyPreferredPrinterSettingKey = "reports_default_printer";
    private static readonly Regex ReportEntryRegex = new(
        @"Timestamp:\s*(?<timestamp>[^\r\n]+)\s*[\r\n]+List #:\s*(?<list>\d+)\s*[\r\n]+Reference Number:\s*(?<reference>[^\r\n]+)\s*[\r\n]+Accounts:\s*(?<accounts>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _referencesFilePath;
    private readonly string _pdfDirectoryPath;
    private readonly string _scriptsPath;
    private readonly string _pythonCommand;
    private readonly string _connectionString;

    public ReportsService()
    {
        var dopAgentRoot = AppPaths.BaseDirectory;
        var reportsRoot = Path.Combine(dopAgentRoot, "Reports");
        var dbPath = Path.Combine(dopAgentRoot, "dop_agent.db");
        _referencesFilePath = Path.Combine(reportsRoot, "references", "payment_references.txt");
        _pdfDirectoryPath = Path.Combine(reportsRoot, "pdf");
        _scriptsPath = dopAgentRoot;
        _connectionString = $"Data Source={dbPath}";
        Directory.CreateDirectory(_scriptsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_referencesFilePath)!);
        Directory.CreateDirectory(_pdfDirectoryPath);

        var venvPath = Path.Combine(_scriptsPath, ".venv");
        var hasVenv = Directory.Exists(venvPath);
        if (hasVenv)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var primary = Path.Combine(venvPath, "Scripts", "python.exe");
                var fallback = Path.Combine(venvPath, "Scripts", "python");
                _pythonCommand = File.Exists(primary) ? primary : fallback;
            }
            else
            {
                var primary = Path.Combine(venvPath, "bin", "python3");
                var fallback = Path.Combine(venvPath, "bin", "python");
                _pythonCommand = File.Exists(primary) ? primary : fallback;
            }
        }
        else
        {
            _pythonCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
        }
    }

    public string ReferencesFilePath => _referencesFilePath;
    public string PdfDirectoryPath => _pdfDirectoryPath;

    public async Task<List<DailyListReport>> GetReportsForDateAsync(DateTime date)
    {
        if (!File.Exists(_referencesFilePath))
        {
            return new List<DailyListReport>();
        }

        var content = await File.ReadAllTextAsync(_referencesFilePath);
        var items = new List<DailyListReport>();

        foreach (Match match in ReportEntryRegex.Matches(content))
        {
            var timestampRaw = match.Groups["timestamp"].Value.Trim();
            var listRaw = match.Groups["list"].Value.Trim();
            var reference = match.Groups["reference"].Value.Trim();
            var accountsRaw = match.Groups["accounts"].Value.Trim();

            if (!DateTime.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
            {
                continue;
            }

            if (timestamp.Date != date.Date)
            {
                continue;
            }

            if (!int.TryParse(listRaw, out var listIndex))
            {
                listIndex = 0;
            }

            var pdfPath = Path.Combine(_pdfDirectoryPath, $"{reference}.pdf");

            items.Add(new DailyListReport
            {
                Timestamp = timestamp,
                ListIndex = listIndex,
                ReferenceNumber = reference,
                AccountsRaw = accountsRaw,
                AccountCount = CountAccounts(accountsRaw),
                PdfPath = pdfPath,
                HasPdf = File.Exists(pdfPath)
            });
        }

        return items
            .OrderByDescending(item => item.Timestamp)
            .ToList();
    }

    public async Task<List<DailyListReport>> GetReportsByReferencesAsync(IReadOnlyList<string> references)
    {
        if (references == null || references.Count == 0 || !File.Exists(_referencesFilePath))
        {
            return new List<DailyListReport>();
        }

        var referenceSet = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (referenceSet.Count == 0)
        {
            return new List<DailyListReport>();
        }

        var content = await File.ReadAllTextAsync(_referencesFilePath);
        var latestByReference = new Dictionary<string, DailyListReport>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ReportEntryRegex.Matches(content))
        {
            var reference = match.Groups["reference"].Value.Trim();
            if (string.IsNullOrWhiteSpace(reference) || !referenceSet.Contains(reference))
            {
                continue;
            }

            var timestampRaw = match.Groups["timestamp"].Value.Trim();
            if (!DateTime.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
            {
                continue;
            }

            var listRaw = match.Groups["list"].Value.Trim();
            var accountsRaw = match.Groups["accounts"].Value.Trim();
            if (!int.TryParse(listRaw, out var listIndex))
            {
                listIndex = 0;
            }

            var pdfPath = Path.Combine(_pdfDirectoryPath, $"{reference}.pdf");
            var report = new DailyListReport
            {
                Timestamp = timestamp,
                ListIndex = listIndex,
                ReferenceNumber = reference,
                AccountsRaw = accountsRaw,
                AccountCount = CountAccounts(accountsRaw),
                PdfPath = pdfPath,
                HasPdf = File.Exists(pdfPath)
            };

            if (!latestByReference.TryGetValue(reference, out var existing) || report.Timestamp > existing.Timestamp)
            {
                latestByReference[reference] = report;
            }
        }

        return latestByReference.Values
            .OrderByDescending(item => item.Timestamp)
            .ToList();
    }

    public Task<(bool Success, string Message)> OpenPdfAsync(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return Task.FromResult((false, "PDF file not found."));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });

            return Task.FromResult((true, "Opened PDF."));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Could not open PDF: {ex.Message}"));
        }
    }

    public async Task<(bool Success, string Message)> PrintPdfAsync(string pdfPath, int copies)
    {
        var normalizedPath = NormalizePath(pdfPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
        {
            return (false, "PDF file not found.");
        }

        var safeCopies = Math.Max(1, copies);
        var preferredPrinter = (await GetEffectiveDefaultPrinterAsync()).Trim();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var (printed, printError) = await TryWindowsDirectPdfPrintAsync(normalizedPath, preferredPrinter, safeCopies);
                if (printed)
                {
                    return (true, string.IsNullOrWhiteSpace(preferredPrinter)
                        ? $"Print command sent ({safeCopies} copy/copies)."
                        : $"Print command sent to {preferredPrinter} ({safeCopies} copy/copies).");
                }

                var reason = string.IsNullOrWhiteSpace(printError)
                    ? "Could not auto-print this PDF on Windows."
                    : printError;
                return (false, reason);
            }

            var lpInfo = new ProcessStartInfo
            {
                FileName = "lp",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            lpInfo.ArgumentList.Add("-n");
            lpInfo.ArgumentList.Add(safeCopies.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(preferredPrinter))
            {
                lpInfo.ArgumentList.Add("-d");
                lpInfo.ArgumentList.Add(preferredPrinter);
            }
            lpInfo.ArgumentList.Add(normalizedPath);

            using var lpProcess = Process.Start(lpInfo);
            if (lpProcess == null)
            {
                return (false, "Could not start print process.");
            }

            var stderr = await lpProcess.StandardError.ReadToEndAsync();
            await lpProcess.WaitForExitAsync();

            if (lpProcess.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stderr) ? "lp command failed." : stderr.Trim();
                return (false, error);
            }

            return (true, string.IsNullOrWhiteSpace(preferredPrinter)
                ? "Print command sent."
                : $"Print command sent to {preferredPrinter}.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not print PDF: {ex.Message}");
        }
    }

    private async Task<(bool success, string error)> TryWindowsDirectPdfPrintAsync(
        string pdfPath,
        string preferredPrinter,
        int copies)
    {
        var targetPrinter = string.IsNullOrWhiteSpace(preferredPrinter)
            ? (await GetSystemDefaultPrinterAsync()).Trim()
            : preferredPrinter.Trim();

        var sumatraPath = FindSumatraPdfPath();
        if (!string.IsNullOrWhiteSpace(sumatraPath))
        {
            var (success, error) = await TrySumatraPdfPrintAsync(sumatraPath, pdfPath, targetPrinter, copies);
            if (success)
            {
                return (true, string.Empty);
            }

            return (false, $"SumatraPDF print failed: {error}");
        }

        var adobePath = FindAdobeReaderPath();
        if (!string.IsNullOrWhiteSpace(adobePath))
        {
            var (success, error) = await TryAdobeReaderPrintAsync(adobePath, pdfPath, targetPrinter, copies);
            if (success)
            {
                return (true, string.Empty);
            }

            return (false, $"Adobe Reader print failed: {error}");
        }

        return (false, "Windows auto-print requires SumatraPDF or Adobe Reader. Install one of them, or open PDF and press Ctrl+P.");
    }

    private static async Task<(bool success, string error)> TrySumatraPdfPrintAsync(
        string sumatraPath,
        string pdfPath,
        string printerName,
        int copies)
    {
        try
        {
            var safeCopies = Math.Max(1, copies);
            for (var i = 0; i < safeCopies; i++)
            {
                var (exitCode, _, stderr) = await RunSumatraPrintAsync(sumatraPath, pdfPath, printerName);
                if (exitCode == 0)
                {
                    continue;
                }

                // Printer name mismatch is common; retry default printer once.
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    var (fallbackCode, _, fallbackError) = await RunSumatraPrintAsync(sumatraPath, pdfPath, string.Empty);
                    if (fallbackCode == 0)
                    {
                        continue;
                    }

                    var preferredError = string.IsNullOrWhiteSpace(stderr)
                        ? $"SumatraPDF exited with code {exitCode}."
                        : stderr.Trim();
                    var defaultError = string.IsNullOrWhiteSpace(fallbackError)
                        ? $"SumatraPDF exited with code {fallbackCode} on default printer."
                        : fallbackError.Trim();
                    return (false, $"{preferredError} Fallback default print failed: {defaultError}");
                }

                return (false, string.IsNullOrWhiteSpace(stderr)
                    ? $"SumatraPDF exited with code {exitCode}."
                    : stderr.Trim());
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunSumatraPrintAsync(
        string sumatraPath,
        string pdfPath,
        string printerName)
    {
        var info = new ProcessStartInfo
        {
            FileName = sumatraPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            info.ArgumentList.Add("-print-to");
            info.ArgumentList.Add(printerName);
        }
        else
        {
            info.ArgumentList.Add("-print-to-default");
        }

        info.ArgumentList.Add("-silent");
        info.ArgumentList.Add("-exit-when-done");
        info.ArgumentList.Add(pdfPath);

        using var process = Process.Start(info);
        if (process == null)
        {
            return (-1, string.Empty, "Could not start SumatraPDF.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<(bool success, string error)> TryAdobeReaderPrintAsync(
        string adobeReaderPath,
        string pdfPath,
        string printerName,
        int copies)
    {
        try
        {
            var safeCopies = Math.Max(1, copies);
            for (var i = 0; i < safeCopies; i++)
            {
                var info = new ProcessStartInfo
                {
                    FileName = adobeReaderPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add("/N");
                info.ArgumentList.Add("/T");
                info.ArgumentList.Add(pdfPath);
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    info.ArgumentList.Add(printerName);
                }

                var process = Process.Start(info);
                if (process == null)
                {
                    return (false, "Could not start Adobe Reader.");
                }

                // Adobe may continue running after queuing print. If it exits quickly with non-zero, treat as failure.
                await Task.Delay(1200);
                if (process.HasExited && process.ExitCode != 0)
                {
                    return (false, $"Adobe Reader exited with code {process.ExitCode}.");
                }
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string FindSumatraPdfPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SumatraPDF", "SumatraPDF.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SumatraPDF", "SumatraPDF.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SumatraPDF", "SumatraPDF.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SumatraPDF", "SumatraPDF.exe")
        };

        var fromKnownPaths = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(fromKnownPaths))
        {
            return fromKnownPaths;
        }

        return FindExecutableOnPath("SumatraPDF.exe");
    }

    private static string FindAdobeReaderPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(programFiles, "Adobe", "Acrobat Reader DC", "Reader", "AcroRd32.exe"),
            Path.Combine(programFilesX86, "Adobe", "Acrobat Reader DC", "Reader", "AcroRd32.exe"),
            Path.Combine(programFiles, "Adobe", "Acrobat Reader", "Reader", "AcroRd32.exe"),
            Path.Combine(programFilesX86, "Adobe", "Acrobat Reader", "Reader", "AcroRd32.exe"),
            Path.Combine(programFiles, "Adobe", "Acrobat", "Acrobat", "Acrobat.exe"),
            Path.Combine(programFilesX86, "Adobe", "Acrobat", "Acrobat", "Acrobat.exe")
        };

        var fromKnownPaths = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(fromKnownPaths))
        {
            return fromKnownPaths;
        }

        var fromPath = FindExecutableOnPath("AcroRd32.exe");
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        return FindExecutableOnPath("Acrobat.exe");
    }

    private static string FindExecutableOnPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return string.Empty;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return string.Empty;
        }

        foreach (var rawPart in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = rawPart.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return string.Empty;
    }

    public async Task<List<string>> GetAvailablePrintersAsync()
    {
        var printers = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (exitCode, stdout, _) = await RunProcessAsync(
                "powershell",
                "-NoProfile",
                "-Command",
                "Get-CimInstance Win32_Printer | Select-Object -ExpandProperty Name");

            if (exitCode == 0)
            {
                foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var printer = line.Trim();
                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        printers.Add(printer);
                    }
                }
            }
        }
        else
        {
            var (exitCode, stdout, _) = await RunProcessAsync("lpstat", "-a");
            if (exitCode == 0)
            {
                foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    var separatorIndex = trimmed.IndexOf(' ');
                    var printer = separatorIndex > 0
                        ? trimmed[..separatorIndex].Trim()
                        : trimmed;

                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        printers.Add(printer);
                    }
                }
            }
        }

        var deduped = printers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preferred = (await GetSavedPreferredPrinterAsync()).Trim();
        if (!string.IsNullOrWhiteSpace(preferred) &&
            deduped.All(item => !string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase)))
        {
            deduped.Insert(0, preferred);
        }

        return deduped;
    }

    public async Task<string> GetEffectiveDefaultPrinterAsync()
    {
        var preferred = (await GetSavedPreferredPrinterAsync()).Trim();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        var systemDefault = (await GetSystemDefaultPrinterAsync()).Trim();
        return systemDefault;
    }

    public async Task<(bool Success, string Message)> SetDefaultPrinterAsync(string printerName)
    {
        var normalizedPrinter = (printerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrinter))
        {
            return (false, "Select a printer first.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (exitCode, _, stderr) = await RunProcessAsync(
                "rundll32",
                "printui.dll,PrintUIEntry",
                "/y",
                "/n",
                normalizedPrinter);

            if (exitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stderr) ? "Failed to set default printer." : stderr.Trim();
                return (false, error);
            }
        }
        else
        {
            var (exitCode, _, stderr) = await RunProcessAsync("lpoptions", "-d", normalizedPrinter);
            if (exitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stderr) ? "Failed to set default printer." : stderr.Trim();
                return (false, error);
            }
        }

        await SavePreferredPrinterAsync(normalizedPrinter);
        return (true, $"Default printer set to {normalizedPrinter}.");
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Trim('"');
    }

    private async Task<string> GetSavedPreferredPrinterAsync()
    {
        EnsureAppSettingsTable();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT value
            FROM app_settings
            WHERE key = @key
            LIMIT 1";
        command.Parameters.AddWithValue("@key", PreferredPrinterSettingKey);

        var result = await command.ExecuteScalarAsync();
        var preferred = result?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        using var legacyCommand = connection.CreateCommand();
        legacyCommand.CommandText = @"
            SELECT value
            FROM app_settings
            WHERE key = @key
            LIMIT 1";
        legacyCommand.Parameters.AddWithValue("@key", LegacyPreferredPrinterSettingKey);
        var legacy = legacyCommand.ExecuteScalar()?.ToString() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            // Backfill old key to the new global key.
            await SavePreferredPrinterAsync(legacy);
        }

        return legacy;
    }

    private async Task SavePreferredPrinterAsync(string printerName)
    {
        EnsureAppSettingsTable();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@key, @value, CURRENT_TIMESTAMP)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = CURRENT_TIMESTAMP";
        command.Parameters.AddWithValue("@key", PreferredPrinterSettingKey);
        command.Parameters.AddWithValue("@value", printerName);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> GetSystemDefaultPrinterAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (exitCode, stdout, _) = await RunProcessAsync(
                "powershell",
                "-NoProfile",
                "-Command",
                "$p = Get-CimInstance Win32_Printer | Where-Object {$_.Default -eq $true} | Select-Object -First 1 -ExpandProperty Name; if ($p) { $p }");
            if (exitCode != 0)
            {
                return string.Empty;
            }

            return stdout
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? string.Empty;
        }

        var (code, output, _) = await RunProcessAsync("lpstat", "-d");
        if (code != 0)
        {
            return string.Empty;
        }

        var line = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var marker = "destination:";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        return line[(index + marker.Length)..].Trim();
    }

    private void EnsureAppSettingsTable()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )";
        command.ExecuteNonQuery();
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, params string[] args)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        if (process == null)
        {
            return (-1, string.Empty, "Failed to start process.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    public Task<(bool Success, string Message)> OpenLinkAsync(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return Task.FromResult((false, "Link is empty."));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = link,
                UseShellExecute = true
            });

            return Task.FromResult((true, "Opened link."));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Could not open link: {ex.Message}"));
        }
    }

    public async Task<(bool Success, string Message)> DeleteReportAsync(DailyListReport report)
    {
        if (report == null || string.IsNullOrWhiteSpace(report.ReferenceNumber))
        {
            return (false, "Invalid report selection.");
        }

        if (!File.Exists(_referencesFilePath))
        {
            return (false, "Reference log file not found.");
        }

        var referencesContent = await File.ReadAllTextAsync(_referencesFilePath);
        var updatedReferences = RemoveReferenceBlock(referencesContent, report.ReferenceNumber, out var removedFromReferences);
        if (!removedFromReferences)
        {
            return (false, "Report entry not found in payment_references.txt.");
        }

        await File.WriteAllTextAsync(_referencesFilePath, updatedReferences);

        if (string.IsNullOrWhiteSpace(report.PdfPath) || !File.Exists(report.PdfPath))
        {
            return (true, "Report removed from list.");
        }

        try
        {
            File.Delete(report.PdfPath);
            return (true, "Report and PDF deleted.");
        }
        catch (Exception ex)
        {
            return (true, $"Report removed from list, but PDF delete failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message, string OutputPdfPath)> GeneratePayslipsAsync(
        IReadOnlyList<DailyListReport> selectedReports)
    {
        if (selectedReports == null || selectedReports.Count == 0)
        {
            return (false, "Select at least one report to generate payslip.", string.Empty);
        }

        var references = selectedReports
            .Select(report => report.ReferenceNumber)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (references.Count == 0)
        {
            return (false, "Selected reports do not contain valid reference numbers.", string.Empty);
        }

        var scriptPath = Path.Combine(_scriptsPath, "Generate_Payslips.py");
        if (!File.Exists(scriptPath))
        {
            return (false, $"Payslip script not found: {scriptPath}", string.Empty);
        }

        var refsJson = System.Text.Json.JsonSerializer.Serialize(references);
        var output = new List<string>();

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonCommand,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    WorkingDirectory = _scriptsPath
                }
            };
            process.StartInfo.Environment["PYTHONUTF8"] = "1";
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add("--refs-json");
            process.StartInfo.ArgumentList.Add(refsJson);

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.Add(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.Add(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var outputLine = output
                .FirstOrDefault(line => line.StartsWith("OUTPUT_PDF:", StringComparison.OrdinalIgnoreCase));
            var outputPdfPath = outputLine?
                .Substring(outputLine.IndexOf(':') + 1)
                .Trim() ?? string.Empty;

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(outputPdfPath) && File.Exists(outputPdfPath))
            {
                return (true, "Payslip generated successfully.", outputPdfPath);
            }

            var errorText = output.Count > 0 ? string.Join(Environment.NewLine, output) : "Payslip generation failed.";
            return (false, errorText, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Could not generate payslip: {ex.Message}", string.Empty);
        }
    }

    private static int CountAccounts(string accountsRaw)
    {
        if (string.IsNullOrWhiteSpace(accountsRaw))
        {
            return 0;
        }

        var normalized = accountsRaw
            .Replace("[", string.Empty)
            .Replace("]", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        return normalized
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Count(part => !string.IsNullOrWhiteSpace(part));
    }

    private static string RemoveReferenceBlock(string content, string referenceNumber, out bool removed)
    {
        removed = false;
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(referenceNumber))
        {
            return content;
        }

        var escapedReference = Regex.Escape(referenceNumber.Trim());
        var fullBlockRegex = new Regex(
            $@"(?:\r?\n)?={80}\r?\nTimestamp:\s*[^\r\n]+\r?\nList #:\s*\d+\r?\nReference Number:\s*{escapedReference}\s*\r?\nAccounts:\s*[^\r\n]*\r?\n={80}\r?\n?",
            RegexOptions.IgnoreCase);

        var blockMatch = fullBlockRegex.Match(content);
        if (blockMatch.Success)
        {
            removed = true;
            return CleanupReferenceContent(content.Remove(blockMatch.Index, blockMatch.Length));
        }

        var fallbackMatch = ReportEntryRegex
            .Matches(content)
            .Cast<Match>()
            .FirstOrDefault(match =>
                string.Equals(
                    match.Groups["reference"].Value.Trim(),
                    referenceNumber.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (fallbackMatch == null)
        {
            return content;
        }

        var start = fallbackMatch.Index;
        var end = fallbackMatch.Index + fallbackMatch.Length;

        while (start > 0 && (content[start - 1] == '\r' || content[start - 1] == '\n'))
        {
            start--;
        }

        while (end < content.Length && (content[end] == '\r' || content[end] == '\n'))
        {
            end++;
        }

        removed = true;
        return CleanupReferenceContent(content.Remove(start, end - start));
    }

    private static string CleanupReferenceContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var compacted = Regex.Replace(content, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        return compacted.Trim() + Environment.NewLine;
    }
}
