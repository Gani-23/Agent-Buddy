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

namespace AgentBuddy.Services;

public class ReportsService
{
    private static readonly Regex ReportEntryRegex = new(
        @"Timestamp:\s*(?<timestamp>[^\r\n]+)\s*[\r\n]+List #:\s*(?<list>\d+)\s*[\r\n]+Reference Number:\s*(?<reference>[^\r\n]+)\s*[\r\n]+Accounts:\s*(?<accounts>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _referencesFilePath;
    private readonly string _pdfDirectoryPath;
    private readonly string _scriptsPath;
    private readonly string _pythonCommand;

    public ReportsService()
    {
        var dopAgentRoot = AppPaths.BaseDirectory;
        var reportsRoot = Path.Combine(dopAgentRoot, "Reports");
        _referencesFilePath = Path.Combine(reportsRoot, "references", "payment_references.txt");
        _pdfDirectoryPath = Path.Combine(reportsRoot, "pdf");
        _scriptsPath = dopAgentRoot;
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

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var opened = await OpenPdfAsync(normalizedPath);
                if (!opened.Success)
                {
                    return (false, opened.Message);
                }

                return (true, $"Opened PDF. Press Ctrl+P and set Copies={safeCopies}, Color=Grayscale.");
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

            return (true, "Print command sent.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not print PDF: {ex.Message}");
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Trim('"');
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
