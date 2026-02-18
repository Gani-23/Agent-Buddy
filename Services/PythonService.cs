using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Service for executing Python scripts
/// </summary>
public class PythonService
{
    private static readonly Regex ReferenceRegex = new(
        @"Reference:\s*([A-Z0-9]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _pythonCommand;
    private readonly string _scriptsPath;
    private readonly string _documentsPath;
    private readonly string _venvPath;
    private readonly bool _hasVenv;

    public PythonService()
    {
        _documentsPath = AppPaths.DocumentsDirectory;
        _scriptsPath = AppPaths.BaseDirectory;
        Directory.CreateDirectory(_scriptsPath);
        
        // Check for virtual environment
        _venvPath = Path.Combine(_scriptsPath, ".venv");
        _hasVenv = Directory.Exists(_venvPath);
        
        // Determine Python command based on OS and venv
        if (_hasVenv)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var primary = Path.Combine(_venvPath, "Scripts", "python.exe");
                var fallback = Path.Combine(_venvPath, "Scripts", "python");
                _pythonCommand = File.Exists(primary) ? primary : fallback;
            }
            else
            {
                var primary = Path.Combine(_venvPath, "bin", "python3");
                var fallback = Path.Combine(_venvPath, "bin", "python");
                _pythonCommand = File.Exists(primary) ? primary : fallback;
            }
        }
        else
        {
            _pythonCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
        }
    }

    /// <summary>
    /// Check if Python is installed
    /// </summary>
    public async Task<(bool isInstalled, string version)> CheckPythonInstalledAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonCommand,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                }
            };
            ApplyPythonRuntimeEnvironment(process.StartInfo);

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var version = (output + error).Trim();
                var venvInfo = _hasVenv ? " (virtual environment)" : "";
                return (true, version + venvInfo);
            }
        }
        catch
        {
            // Python not found
        }

        return (false, string.Empty);
    }

    /// <summary>
    /// Check if required packages are installed
    /// </summary>
    public async Task<bool> CheckRequiredPackagesAsync()
    {
        var requiredPackages = new[] { "selenium", "pandas", "pyperclip", "openpyxl", "xlrd", "reportlab" };
        
        foreach (var package in requiredPackages)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _pythonCommand,
                        Arguments = $"-m pip show {package}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        CreateNoWindow = true
                    }
                };
                ApplyPythonRuntimeEnvironment(process.StartInfo);

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    return false; // Package not installed
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Install required Python packages
    /// </summary>
    public async Task<(bool success, string output)> InstallRequiredPackagesAsync()
    {
        var output = new StringBuilder();

        try
        {
            var packages = "selenium pandas pyperclip openpyxl xlrd reportlab";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonCommand,
                    Arguments = $"-m pip install {packages}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                }
            };
            ApplyPythonRuntimeEnvironment(process.StartInfo);

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    output.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    output.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output.ToString());
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute Fetch_RDAccounts.py to update database
    /// </summary>
    public async Task<(bool success, string output)> UpdateDatabaseAsync(Action<string>? progressCallback = null)
    {
        var scriptPath = Path.Combine(_scriptsPath, "Fetch_RDAccounts.py");
        
        if (!File.Exists(scriptPath))
        {
            return (false, $"Script not found: {scriptPath}");
        }

        var output = new StringBuilder();

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonCommand,
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,   // IMPORTANT: Enable stdin
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    WorkingDirectory = _scriptsPath
                }
            };
            ApplyPythonRuntimeEnvironment(process.StartInfo);

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                    progressCallback?.Invoke(args.Data);
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                    progressCallback?.Invoke(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Send "1" to automatically select menu option 1 (active-only update)
            await process.StandardInput.WriteLineAsync("1");
            await process.StandardInput.FlushAsync();
            
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output.ToString());
        }
        catch (Exception ex)
        {
            return (false, $"Error executing script: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute ScheduleArguments.py with bulk lists
    /// </summary>
    public async Task<ProcessingResult> ProcessListsAsync(
        string bulkListsString,
        IReadOnlyCollection<AslaasUpdateItem>? aslaasUpdates = null,
        string paymentMode = "Cash",
        IReadOnlyCollection<DopChequeInputItem>? dopChequeInputs = null,
        Action<string>? progressCallback = null)
    {
        var scriptPath = Path.Combine(_scriptsPath, "ScheduleArguments.py");
        
        if (!File.Exists(scriptPath))
        {
            return new ProcessingResult
            {
                Success = false,
                ErrorMessage = $"Script not found: {scriptPath}"
            };
        }

        var output = new StringBuilder();
        var referenceNumbers = new List<string>();

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
            process.StartInfo.Environment["PYTHONUNBUFFERED"] = "1";
            process.StartInfo.ArgumentList.Add("-u");
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add("--bulk");
            process.StartInfo.ArgumentList.Add(bulkListsString);
            process.StartInfo.ArgumentList.Add("--pay-mode");
            process.StartInfo.ArgumentList.Add(NormalizePaymentModeArg(paymentMode));
            if (dopChequeInputs is { Count: > 0 })
            {
                var normalizedDopCheque = dopChequeInputs
                    .Where(item =>
                        item.ListIndex > 0 &&
                        !string.IsNullOrWhiteSpace(item.AccountNo) &&
                        !string.IsNullOrWhiteSpace(item.ChequeNo) &&
                        !string.IsNullOrWhiteSpace(item.PaymentAccountNo))
                    .Select(item => new
                    {
                        list_index = item.ListIndex,
                        account_no = item.AccountNo.Trim(),
                        cheque_no = item.ChequeNo.Trim(),
                        payment_account_no = item.PaymentAccountNo.Trim()
                    })
                    .ToList();

                if (normalizedDopCheque.Count > 0)
                {
                    process.StartInfo.ArgumentList.Add("--dop-cheque-data");
                    process.StartInfo.ArgumentList.Add(JsonSerializer.Serialize(normalizedDopCheque));
                }
            }
            if (aslaasUpdates is { Count: > 0 })
            {
                var normalized = aslaasUpdates
                    .Where(item => !string.IsNullOrWhiteSpace(item.AccountNo))
                    .Select(item => new
                    {
                        account_no = item.AccountNo.Trim(),
                        aslaas_no = string.IsNullOrWhiteSpace(item.AslaasNo) ? "APPLIED" : item.AslaasNo.Trim()
                    })
                    .ToList();

                if (normalized.Count > 0)
                {
                    process.StartInfo.ArgumentList.Add("--aslaas-updates");
                    process.StartInfo.ArgumentList.Add(JsonSerializer.Serialize(normalized));
                }
            }
            ApplyPythonRuntimeEnvironment(process.StartInfo);

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                    progressCallback?.Invoke(args.Data);

                    // Extract only true payment reference lines.
                    // Prevents adding unrelated lines like "...saved to: /path/...".
                    var match = ReferenceRegex.Match(args.Data);
                    if (match.Success)
                    {
                        var refNum = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(refNum))
                        {
                            referenceNumbers.Add(refNum);
                        }
                    }
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                    progressCallback?.Invoke(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return new ProcessingResult
            {
                Success = process.ExitCode == 0,
                ReferenceNumbers = referenceNumbers,
                ErrorMessage = process.ExitCode != 0 ? output.ToString() : string.Empty
            };
        }
        catch (Exception ex)
        {
            return new ProcessingResult
            {
                Success = false,
                ErrorMessage = $"Error executing script: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Execute Sync_Legacy_AccountDetail.py to sync old sqlite data.
    /// </summary>
    public async Task<(bool success, string output)> SyncLegacyAccountDetailAsync(
        string sourceDbPath,
        string targetDbPath,
        bool dryRun = false,
        bool preferSourceValues = false,
        bool forceAslaas = false,
        bool deactivateMissing = false,
        Action<string>? progressCallback = null)
    {
        var scriptPath = Path.Combine(_scriptsPath, "Sync_Legacy_AccountDetail.py");
        if (!File.Exists(scriptPath))
        {
            return (false, $"Script not found: {scriptPath}");
        }

        var output = new StringBuilder();

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
            ApplyPythonRuntimeEnvironment(process.StartInfo);

            process.StartInfo.ArgumentList.Add("-u");
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add("--source");
            process.StartInfo.ArgumentList.Add(sourceDbPath);
            process.StartInfo.ArgumentList.Add("--target");
            process.StartInfo.ArgumentList.Add(targetDbPath);
            if (dryRun)
            {
                process.StartInfo.ArgumentList.Add("--dry-run");
            }
            if (preferSourceValues)
            {
                process.StartInfo.ArgumentList.Add("--prefer-source-values");
            }
            if (forceAslaas)
            {
                process.StartInfo.ArgumentList.Add("--force-aslaas");
            }
            if (deactivateMissing)
            {
                process.StartInfo.ArgumentList.Add("--deactivate-missing");
            }

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data == null)
                {
                    return;
                }

                output.AppendLine(args.Data);
                progressCallback?.Invoke(args.Data);
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                {
                    return;
                }

                output.AppendLine(args.Data);
                progressCallback?.Invoke(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output.ToString());
        }
        catch (Exception ex)
        {
            return (false, $"Error executing sync script: {ex.Message}");
        }
    }

    /// <summary>
    /// Get Python scripts directory path
    /// </summary>
    public string GetScriptsPath() => _scriptsPath;

    public string GetDocumentsPath() => _documentsPath;

    /// <summary>
    /// Check if scripts exist
    /// </summary>
    public (bool fetchExists, bool scheduleExists) CheckScriptsExist()
    {
        var fetchPath = Path.Combine(_scriptsPath, "Fetch_RDAccounts.py");
        var schedulePath = Path.Combine(_scriptsPath, "ScheduleArguments.py");
        
        return (File.Exists(fetchPath), File.Exists(schedulePath));
    }

    /// <summary>
    /// Check if virtual environment exists
    /// </summary>
    public bool HasVirtualEnvironment() => _hasVenv;

    private static void ApplyPythonRuntimeEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
    }

    private static string NormalizePaymentModeArg(string? paymentMode)
    {
        var mode = (paymentMode ?? string.Empty).Trim().ToLowerInvariant();
        return mode switch
        {
            "dop cheque" => "dop_cheque",
            "dop_cheque" => "dop_cheque",
            "non dop cheque" => "non_dop_cheque",
            "non_dop_cheque" => "non_dop_cheque",
            _ => "cash"
        };
    }
}
