using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentBuddy.Models;
using SkiaSharp;

namespace AgentBuddy.Services;

public static class DueListPdfService
{
    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;
    private const float Margin = 36f;
    private const float ContentWidth = PageWidth - (Margin * 2);

    /// <summary>
    /// Generates a professional multi-page India Post collection due list PDF.
    /// </summary>
    public static string GenerateDueListPdf(string title, string agentName, IReadOnlyList<RDAccount> accounts)
    {
        var outputDir = Path.Combine(AppPaths.BaseDirectory, "Reports", "reminders");
        try
        {
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }
        catch { }

        var safeTitle = Regex.Replace(title ?? "Due_List", @"[^\w\-]", "_").Trim('_');
        var fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var fullPath = Path.Combine(outputDir, fileName);

        var totalAccounts = accounts.Count;
        decimal totalAmount = 0;
        foreach (var a in accounts)
        {
            totalAmount += a.GetAmount();
        }

        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var doc = SKDocument.CreatePdf(stream);

        using var boldTypeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var normalTypeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        using var fontTitle = new SKFont(boldTypeface, 15);
        using var fontSub = new SKFont(normalTypeface, 9);
        using var fontMeta = new SKFont(normalTypeface, 9.5f);
        using var fontMetaBold = new SKFont(boldTypeface, 9.5f);
        using var fontHeader = new SKFont(boldTypeface, 9);
        using var fontRow = new SKFont(normalTypeface, 8.5f);
        using var fontRowBold = new SKFont(boldTypeface, 8.5f);

        using var paintDark = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true };
        using var paintSlate = new SKPaint { Color = new SKColor(100, 116, 139), IsAntialias = true };
        using var paintBody = new SKPaint { Color = new SKColor(30, 41, 59), IsAntialias = true };
        using var paintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };

        using var headerBgPaint = new SKPaint { Color = new SKColor(30, 58, 138), Style = SKPaintStyle.Fill };
        using var rowBgAltPaint = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { Color = new SKColor(226, 232, 240), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };

        // Columns: [# (26)] [Account No (110)] [Name (195)] [Amount (72)] [Due Date (56)] [Paid Upto (56)] = 515
        float colX0 = Margin;
        float colX1 = colX0 + 26;
        float colX2 = colX1 + 110;
        float colX3 = colX2 + 195;
        float colX4 = colX3 + 72;
        float colX5 = colX4 + 56;
        float colX6 = Margin + ContentWidth;

        const float rowHeight = 20f;
        const int rowsPerPageFirst = 27;
        const int rowsPerPageOther = 33;

        int totalPages = 1;
        int remaining = totalAccounts - rowsPerPageFirst;
        if (remaining > 0)
        {
            totalPages += (int)Math.Ceiling((double)remaining / rowsPerPageOther);
        }

        int currIndex = 0;
        for (int p = 1; p <= totalPages; p++)
        {
            using var canvas = doc.BeginPage(PageWidth, PageHeight);
            float y = Margin;

            if (p == 1)
            {
                // Top Brand Bar
                var brandRect = new SKRect(Margin, y, Margin + ContentWidth, y + 4);
                using (var bp = new SKPaint { Color = new SKColor(220, 38, 38) })
                {
                    canvas.DrawRect(brandRect, bp);
                }
                y += 14;

                // Title & Subtitle
                canvas.DrawText("DEPARTMENT OF POSTS (INDIA) • RD COLLECTION DUE LIST", Margin, y + 12, fontTitle, paintDark);
                canvas.DrawText($"Generated: {DateTime.Now:dd-MMM-yyyy hh:mm tt}  |  Category: {title}", Margin, y + 26, fontSub, paintSlate);
                y += 34;

                // Summary Card
                var summaryRect = new SKRect(Margin, y, Margin + ContentWidth, y + 36);
                using (var sbg = new SKPaint { Color = new SKColor(241, 245, 249) })
                {
                    canvas.DrawRoundRect(summaryRect, 6, 6, sbg);
                }
                canvas.DrawRoundRect(summaryRect, 6, 6, borderPaint);

                float boxY = y + 22;
                canvas.DrawText("Agent: ", Margin + 12, boxY, fontMeta, paintBody);
                var displayAgent = string.IsNullOrWhiteSpace(agentName) ? "DOP Agent" : agentName;
                canvas.DrawText(displayAgent, Margin + 46, boxY, fontMetaBold, paintDark);

                canvas.DrawText("Total Dues: ", Margin + 220, boxY, fontMeta, paintBody);
                canvas.DrawText($"{totalAccounts} Accounts", Margin + 276, boxY, fontMetaBold, paintDark);

                canvas.DrawText("Total Amount: ", Margin + 370, boxY, fontMeta, paintBody);
                canvas.DrawText($"Rs. {totalAmount:N0}", Margin + 440, boxY, fontMetaBold, paintDark);
                y += 46;
            }
            else
            {
                // Continuation Header on Subsequent Pages
                canvas.DrawText($"INDIA POST RD COLLECTION DUE LIST — {title} (Cont.)", Margin, y + 10, fontMetaBold, paintDark);
                canvas.DrawText($"Page {p} of {totalPages}", Margin + ContentWidth - 65, y + 10, fontSub, paintSlate);
                y += 20;
            }

            // Table Column Headers
            var thRect = new SKRect(Margin, y, Margin + ContentWidth, y + rowHeight);
            canvas.DrawRect(thRect, headerBgPaint);

            float headerTextY = y + 14;
            canvas.DrawText("#", colX0 + 8, headerTextY, fontHeader, paintWhite);
            canvas.DrawText("Account Number", colX1 + 6, headerTextY, fontHeader, paintWhite);
            canvas.DrawText("Customer Name", colX2 + 6, headerTextY, fontHeader, paintWhite);
            canvas.DrawText("Amount (Rs)", colX3 + 6, headerTextY, fontHeader, paintWhite);
            canvas.DrawText("Due Date", colX4 + 6, headerTextY, fontHeader, paintWhite);
            canvas.DrawText("Paid Upto", colX5 + 6, headerTextY, fontHeader, paintWhite);
            y += rowHeight;

            int rowsOnThisPage = (p == 1) ? rowsPerPageFirst : rowsPerPageOther;
            int pageStart = currIndex;
            int pageEnd = Math.Min(currIndex + rowsOnThisPage, totalAccounts);

            for (int r = pageStart; r < pageEnd; r++)
            {
                var item = accounts[r];
                var rowRect = new SKRect(Margin, y, Margin + ContentWidth, y + rowHeight);
                if (r % 2 == 1)
                {
                    canvas.DrawRect(rowRect, rowBgAltPaint);
                }
                canvas.DrawLine(Margin, y + rowHeight, Margin + ContentWidth, y + rowHeight, borderPaint);

                float rowTextY = y + 14;
                canvas.DrawText($"{r + 1}", colX0 + 6, rowTextY, fontSub, paintSlate);
                canvas.DrawText(item.AccountNo ?? string.Empty, colX1 + 6, rowTextY, fontRowBold, paintDark);

                var name = item.AccountName ?? string.Empty;
                if (name.Length > 32)
                {
                    name = string.Concat(name.AsSpan(0, 30), "..");
                }
                canvas.DrawText(name, colX2 + 6, rowTextY, fontRow, paintBody);

                var amt = item.GetAmount();
                canvas.DrawText($"{amt:N0}", colX3 + 6, rowTextY, fontRowBold, paintDark);

                var dueDate = item.GetNextInstallmentDate()?.ToString("dd-MMM") ?? item.NextInstallmentDate ?? string.Empty;
                canvas.DrawText(dueDate, colX4 + 6, rowTextY, fontRow, paintBody);

                var paidUpto = item.MonthPaidUpto ?? string.Empty;
                canvas.DrawText(paidUpto, colX5 + 6, rowTextY, fontRow, paintBody);

                y += rowHeight;
                currIndex++;
            }

            // Outer Borders
            canvas.DrawLine(colX0, Margin, colX0, y, borderPaint);
            canvas.DrawLine(colX6, Margin, colX6, y, borderPaint);

            // Grand Total Row on Final Page
            if (p == totalPages)
            {
                var totalRowRect = new SKRect(Margin, y, Margin + ContentWidth, y + rowHeight);
                using (var tbg = new SKPaint { Color = new SKColor(241, 245, 249) })
                {
                    canvas.DrawRect(totalRowRect, tbg);
                }
                canvas.DrawRect(totalRowRect, borderPaint);

                float totTextY = y + 14;
                canvas.DrawText("GRAND TOTAL", colX1 + 6, totTextY, fontMetaBold, paintDark);
                canvas.DrawText($"{totalAccounts} Accounts", colX2 + 6, totTextY, fontMetaBold, paintDark);
                canvas.DrawText($"Rs. {totalAmount:N0}", colX3 + 6, totTextY, fontMetaBold, paintDark);
                y += rowHeight + 8;
            }

            // Footer
            float footerY = PageHeight - Margin + 14;
            canvas.DrawLine(Margin, PageHeight - Margin, Margin + ContentWidth, PageHeight - Margin, borderPaint);
            canvas.DrawText("Generated by AgentBuddy • Confidential DOP Agent Record", Margin, footerY, fontSub, paintSlate);
            canvas.DrawText($"Page {p} of {totalPages}", Margin + ContentWidth - 65, footerY, fontSub, paintSlate);

            doc.EndPage();
        }

        doc.Close();
        return fullPath;
    }

    /// <summary>
    /// Reveals the given file in Finder (macOS) or File Explorer (Windows).
    /// </summary>
    public static void RevealInFinder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(filePath);
                Process.Start(psi);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add($"/select,{filePath}");
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(Path.GetDirectoryName(filePath) ?? ".");
                Process.Start(psi);
            }
        }
        catch { }
    }

    /// <summary>
    /// Generates due list PDF, reveals it in Finder, and automatically attaches & sends the PDF via WhatsApp Desktop.
    /// </summary>
    public static (bool Success, string PdfPath, string WhatsAppUrl) DispatchToWhatsApp(
        string title,
        string agentName,
        IReadOnlyList<RDAccount> accounts,
        string? agentPhoneNumber)
    {
        var totalCount = accounts.Count;
        decimal totalAmount = 0;
        foreach (var a in accounts)
        {
            totalAmount += a.GetAmount();
        }

        // 1. Generate multi-page PDF document
        var pdfPath = GenerateDueListPdf(title, agentName, accounts);

        // 2. Reveal PDF in Finder for user reference
        RevealInFinder(pdfPath);

        var phoneQuery = string.IsNullOrWhiteSpace(agentPhoneNumber) ? string.Empty : $"phone={agentPhoneNumber}&";
        var nativeUrl = $"whatsapp://send?{phoneQuery}";

        // 3. Automated dispatch on macOS via Accessibility script
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Task.Run(() =>
            {
                try
                {
                    var scriptPath = EnsureAutomationScript();
                    var phoneArg = string.IsNullOrWhiteSpace(agentPhoneNumber) ? "" : $"--phone \"{agentPhoneNumber}\"";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"\"{scriptPath}\" --pdf \"{pdfPath}\" {phoneArg}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(20000);
                }
                catch { }
            });
        }
        else
        {
            // Windows fallback: open whatsapp:// URL
            try
            {
                Process.Start(new ProcessStartInfo(nativeUrl) { UseShellExecute = true });
            }
            catch
            {
                var webUrl = $"https://api.whatsapp.com/send?{phoneQuery}";
                Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
            }
        }

        return (true, pdfPath, nativeUrl);
    }

    private static string EnsureAutomationScript()
    {
        var scriptsDir = Path.Combine(AppPaths.BaseDirectory, "scripts");
        if (!Directory.Exists(scriptsDir))
        {
            Directory.CreateDirectory(scriptsDir);
        }
        var scriptPath = Path.Combine(scriptsDir, "send_whatsapp_pdf.py");
        if (!File.Exists(scriptPath))
        {
            File.WriteAllText(scriptPath, PythonScriptSource);
        }
        return scriptPath;
    }

    private const string PythonScriptSource = @"#!/usr/bin/env python3
import sys
import os
import time
import ctypes
import subprocess
import argparse

app_services = ctypes.cdll.LoadLibrary('/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices')
core_foundation = ctypes.cdll.LoadLibrary('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')

kCFStringEncodingUTF8 = 0x08000100

CFStringCreateWithCString = core_foundation.CFStringCreateWithCString
CFStringCreateWithCString.restype = ctypes.c_void_p
CFStringCreateWithCString.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32]

CFStringGetCString = core_foundation.CFStringGetCString
CFStringGetCString.restype = ctypes.c_bool
CFStringGetCString.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_long, ctypes.c_uint32]

CFArrayGetCount = core_foundation.CFArrayGetCount
CFArrayGetCount.restype = ctypes.c_long
CFArrayGetCount.argtypes = [ctypes.c_void_p]

CFArrayGetValueAtIndex = core_foundation.CFArrayGetValueAtIndex
CFArrayGetValueAtIndex.restype = ctypes.c_void_p
CFArrayGetValueAtIndex.argtypes = [ctypes.c_void_p, ctypes.c_long]

def cf_to_str(cf):
    if not cf:
        return """"
    buf = ctypes.create_string_buffer(512)
    CFStringGetCString(cf, buf, 512, kCFStringEncodingUTF8)
    return buf.value.decode('utf-8', errors='ignore')

def str_to_cf(s):
    return CFStringCreateWithCString(None, s.encode('utf-8'), kCFStringEncodingUTF8)

AXUIElementCreateApplication = app_services.AXUIElementCreateApplication
AXUIElementCreateApplication.restype = ctypes.c_void_p
AXUIElementCreateApplication.argtypes = [ctypes.c_int]

AXUIElementCopyAttributeValue = app_services.AXUIElementCopyAttributeValue
AXUIElementCopyAttributeValue.restype = ctypes.c_int
AXUIElementCopyAttributeValue.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]

AXUIElementPerformAction = app_services.AXUIElementPerformAction
AXUIElementPerformAction.restype = ctypes.c_int
AXUIElementPerformAction.argtypes = [ctypes.c_void_p, ctypes.c_void_p]

def get_attr(elem, attr_name):
    cf_attr = str_to_cf(attr_name)
    val = ctypes.c_void_p()
    res = AXUIElementCopyAttributeValue(elem, cf_attr, ctypes.byref(val))
    if res == 0 and val.value:
        return val.value
    return None

def find_by_desc(elem, keyword, max_depth=12):
    if max_depth <= 0 or not elem:
        return None
    desc = cf_to_str(get_attr(elem, ""AXDescription""))
    if keyword.lower() in desc.lower():
        return elem
    children_cf = get_attr(elem, ""AXChildren"")
    if children_cf:
        count = CFArrayGetCount(children_cf)
        for i in range(count):
            child = CFArrayGetValueAtIndex(children_cf, i)
            found = find_by_desc(child, keyword, max_depth - 1)
            if found:
                return found
    return None

def send_pdf(pdf_path, phone=None):
    if not os.path.isfile(pdf_path):
        return False

    pdf_path = os.path.abspath(pdf_path)

    phone_clean = ''.join(c for c in (phone or '') if c.isdigit())
    if phone_clean:
        uri = f""whatsapp://send?phone={phone_clean}""
    else:
        uri = ""whatsapp://send""
    
    subprocess.call(['open', uri])
    time.sleep(1.0)

    out = subprocess.check_output(['pgrep', '-x', 'WhatsApp']).decode().strip().split()
    if not out:
        return False
    pid = int(out[0])
    app = AXUIElementCreateApplication(pid)

    def get_main_window():
        wins = get_attr(app, ""AXWindows"")
        if wins and CFArrayGetCount(wins) > 0:
            return CFArrayGetValueAtIndex(wins, 0)
        return None

    win = get_main_window()
    share_btn = find_by_desc(win, ""share media"")
    if not share_btn:
        return False

    AXUIElementPerformAction(share_btn, str_to_cf(""AXPress""))
    time.sleep(0.4)

    win = get_main_window()
    file_btn = find_by_desc(win, ""file"")
    if not file_btn:
        return False

    AXUIElementPerformAction(file_btn, str_to_cf(""AXPress""))
    time.sleep(1.0)

    script = f''''
    tell application ""System Events""
        tell process ""WhatsApp""
            keystroke ""g"" using {{command down, shift down}}
            delay 0.6
            keystroke ""{pdf_path}""
            delay 0.4
            key code 36
            delay 0.8
            key code 36
        end tell
    end tell
    ''''
    subprocess.call(['osascript', '-e', script])
    time.sleep(1.2)

    win = get_main_window()
    send_btn = find_by_desc(win, ""send"")
    if send_btn:
        AXUIElementPerformAction(send_btn, str_to_cf(""AXPress""))
        return True
    else:
        subprocess.call(['osascript', '-e', 'tell application ""System Events"" to tell process ""WhatsApp"" to key code 36'])
        return True

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pdf', required=True, help=""Path to PDF"")
    parser.add_argument('--phone', default=None, help=""Phone number"")
    args = parser.parse_args()
    success = send_pdf(args.pdf, args.phone)
    sys.exit(0 if success else 1)
";
}
