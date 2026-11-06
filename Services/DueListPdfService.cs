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
    /// Generates a high-resolution visual table image (PNG) of the due list.
    /// </summary>
    public static string GenerateDueListPng(string title, string agentName, IReadOnlyList<RDAccount> accounts)
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
        var fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var fullPath = Path.Combine(outputDir, fileName);

        const float width = 800f;
        const float margin = 30f;
        const float contentWidth = width - (margin * 2);
        const float rowHeight = 26f;
        const float headerHeight = 140f;
        const float footerHeight = 75f;

        // Draw up to 100 rows in PNG for sharp display
        int totalAccounts = accounts.Count;
        int maxRows = Math.Min(totalAccounts, 100);
        float height = headerHeight + (maxRows * rowHeight) + footerHeight;
        if (totalAccounts > maxRows)
        {
            height += rowHeight; // extra line for truncated notice
        }

        decimal totalAmount = 0;
        foreach (var a in accounts)
        {
            totalAmount += a.GetAmount();
        }

        using var surface = SKSurface.Create(new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var boldTypeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var normalTypeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        using var fontTitle = new SKFont(boldTypeface, 17);
        using var fontSub = new SKFont(normalTypeface, 11);
        using var fontMeta = new SKFont(normalTypeface, 11.5f);
        using var fontMetaBold = new SKFont(boldTypeface, 11.5f);
        using var fontHeader = new SKFont(boldTypeface, 11);
        using var fontRow = new SKFont(normalTypeface, 10.5f);
        using var fontRowBold = new SKFont(boldTypeface, 10.5f);

        using var paintDark = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true };
        using var paintSlate = new SKPaint { Color = new SKColor(100, 116, 139), IsAntialias = true };
        using var paintBody = new SKPaint { Color = new SKColor(30, 41, 59), IsAntialias = true };
        using var paintWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };

        using var headerBgPaint = new SKPaint { Color = new SKColor(30, 58, 138), Style = SKPaintStyle.Fill };
        using var rowBgAltPaint = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { Color = new SKColor(226, 232, 240), Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

        float y = margin;
        // Top Red Bar
        using (var bp = new SKPaint { Color = new SKColor(220, 38, 38) })
        {
            canvas.DrawRect(new SKRect(margin, y, margin + contentWidth, y + 4), bp);
        }
        y += 16;

        canvas.DrawText("DEPARTMENT OF POSTS (INDIA) • RD COLLECTION DUE LIST", margin, y + 16, fontTitle, paintDark);
        canvas.DrawText($"Generated: {DateTime.Now:dd-MMM-yyyy hh:mm tt}  |  Category: {title}", margin, y + 34, fontSub, paintSlate);
        y += 46;

        // Summary Card
        var summaryRect = new SKRect(margin, y, margin + contentWidth, y + 42);
        using (var sbg = new SKPaint { Color = new SKColor(241, 245, 249) })
        {
            canvas.DrawRoundRect(summaryRect, 8, 8, sbg);
        }
        canvas.DrawRoundRect(summaryRect, 8, 8, borderPaint);

        float boxY = y + 26;
        canvas.DrawText("Agent: ", margin + 16, boxY, fontMeta, paintBody);
        var displayAgent = string.IsNullOrWhiteSpace(agentName) ? "DOP Agent" : agentName;
        canvas.DrawText(displayAgent, margin + 58, boxY, fontMetaBold, paintDark);

        canvas.DrawText("Total Dues: ", margin + 280, boxY, fontMeta, paintBody);
        canvas.DrawText($"{totalAccounts} Accounts", margin + 355, boxY, fontMetaBold, paintDark);

        canvas.DrawText("Total Amount: ", margin + 500, boxY, fontMeta, paintBody);
        canvas.DrawText($"Rs. {totalAmount:N0}", margin + 590, boxY, fontMetaBold, paintDark);
        y += 56;

        // Columns: [# (36)] [Account No (150)] [Name (264)] [Amount (110)] [Due Date (90)] [Paid Upto (90)] = 740
        float c0 = margin;
        float c1 = c0 + 36;
        float c2 = c1 + 150;
        float c3 = c2 + 264;
        float c4 = c3 + 110;
        float c5 = c4 + 90;

        // Table Header
        canvas.DrawRect(new SKRect(margin, y, margin + contentWidth, y + rowHeight), headerBgPaint);
        float hY = y + 18;
        canvas.DrawText("#", c0 + 8, hY, fontHeader, paintWhite);
        canvas.DrawText("Account Number", c1 + 8, hY, fontHeader, paintWhite);
        canvas.DrawText("Customer Name", c2 + 8, hY, fontHeader, paintWhite);
        canvas.DrawText("Amount (Rs)", c3 + 8, hY, fontHeader, paintWhite);
        canvas.DrawText("Due Date", c4 + 8, hY, fontHeader, paintWhite);
        canvas.DrawText("Paid Upto", c5 + 8, hY, fontHeader, paintWhite);
        y += rowHeight;

        for (int i = 0; i < maxRows; i++)
        {
            var item = accounts[i];
            var rowRect = new SKRect(margin, y, margin + contentWidth, y + rowHeight);
            if (i % 2 == 1)
            {
                canvas.DrawRect(rowRect, rowBgAltPaint);
            }
            canvas.DrawLine(margin, y + rowHeight, margin + contentWidth, y + rowHeight, borderPaint);

            float rY = y + 18;
            canvas.DrawText($"{i + 1}", c0 + 8, rY, fontSub, paintSlate);
            canvas.DrawText(item.AccountNo ?? string.Empty, c1 + 8, rY, fontRowBold, paintDark);

            var name = item.AccountName ?? string.Empty;
            if (name.Length > 34)
            {
                name = string.Concat(name.AsSpan(0, 32), "..");
            }
            canvas.DrawText(name, c2 + 8, rY, fontRow, paintBody);

            var amt = item.GetAmount();
            canvas.DrawText($"{amt:N0}", c3 + 8, rY, fontRowBold, paintDark);

            var dueDate = item.GetNextInstallmentDate()?.ToString("dd-MMM") ?? item.NextInstallmentDate ?? string.Empty;
            canvas.DrawText(dueDate, c4 + 8, rY, fontRow, paintBody);

            var paidUpto = item.MonthPaidUpto ?? string.Empty;
            canvas.DrawText(paidUpto, c5 + 8, rY, fontRow, paintBody);

            y += rowHeight;
        }

        if (totalAccounts > maxRows)
        {
            var truncRect = new SKRect(margin, y, margin + contentWidth, y + rowHeight);
            using (var tbg = new SKPaint { Color = new SKColor(254, 243, 199) })
            {
                canvas.DrawRect(truncRect, tbg);
            }
            canvas.DrawRect(truncRect, borderPaint);
            float trY = y + 18;
            canvas.DrawText($"... and {totalAccounts - maxRows} more accounts in the attached PDF report", c1 + 8, trY, fontRowBold, paintDark);
            y += rowHeight;
        }

        // Grand Total Row
        var totRect = new SKRect(margin, y, margin + contentWidth, y + rowHeight);
        using (var tbg = new SKPaint { Color = new SKColor(241, 245, 249) })
        {
            canvas.DrawRect(totRect, tbg);
        }
        canvas.DrawRect(totRect, borderPaint);
        float tY = y + 18;
        canvas.DrawText("GRAND TOTAL", c1 + 8, tY, fontMetaBold, paintDark);
        canvas.DrawText($"{totalAccounts} Accounts", c2 + 8, tY, fontMetaBold, paintDark);
        canvas.DrawText($"Rs. {totalAmount:N0}", c3 + 8, tY, fontMetaBold, paintDark);
        y += rowHeight + 12;

        canvas.DrawText("Generated by AgentBuddy • Confidential DOP Agent Record", margin, y + 14, fontSub, paintSlate);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(fullPath);
        data.SaveTo(fs);

        return fullPath;
    }

    /// <summary>
    /// Copies an image file directly to the system clipboard as native image data (PNGf)
    /// so pasting (Cmd+V) in WhatsApp Desktop/Web instantly attaches the image.
    /// </summary>
    public static bool CopyImageToClipboard(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // AppleScript sets native PNG picture on macOS pasteboard (accepted by WhatsApp Cmd+V)
                var escapedPath = imagePath.Replace("\"", "\\\"");
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'set the clipboard to (read (POSIX file \"{escapedPath}\") as «class PNGf»)'",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return proc?.ExitCode == 0;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var escapedPath = imagePath.Replace("'", "''");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"Set-Clipboard -Path '{escapedPath}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return proc?.ExitCode == 0;
            }
        }
        catch { }

        return false;
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
                Process.Start("open", $"-R \"{filePath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                Process.Start("xdg-open", Path.GetDirectoryName(filePath) ?? ".");
            }
        }
        catch { }
    }

    /// <summary>
    /// Generates due list PDF and PNG, copies the image to clipboard, reveals the PDF in Finder,
    /// opens WhatsApp directly (bypassing Safari), and automatically triggers paste into WhatsApp.
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

        // 2. Generate crisp high-resolution graphic table image
        var pngPath = GenerateDueListPng(title, agentName, accounts);

        // 3. Put the graphic table image onto clipboard so Cmd+V in WhatsApp pastes the image preview
        CopyImageToClipboard(pngPath);

        // 4. Reveal PDF file in Finder with the file selected so the user can easily drag & drop it
        RevealInFinder(pdfPath);

        // 5. Open WhatsApp DIRECTLY (using whatsapp:// URI to avoid Safari)
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine($"*[India Post DOP] {title}*");
        messageBuilder.AppendLine($"Total Accounts: {totalCount} | Expected Collection: Rs. {totalAmount:N0}");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("📋 Visual table is ready: Press Cmd+V (Mac) or Ctrl+V (Windows) to send image.");
        messageBuilder.AppendLine("📄 Or drag & drop the highlighted PDF from Finder to send document.");

        var phoneQuery = string.IsNullOrWhiteSpace(agentPhoneNumber) ? string.Empty : $"phone={agentPhoneNumber}&";
        var encodedMessage = Uri.EscapeDataString(messageBuilder.ToString());
        
        // Primary: native whatsapp:// desktop protocol (bypasses Safari completely)
        var nativeUrl = $"whatsapp://send?{phoneQuery}text={encodedMessage}";
        var webUrl = $"https://api.whatsapp.com/send?{phoneQuery}text={encodedMessage}";

        bool launched = false;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, 'open whatsapp://...' opens WhatsApp Desktop without launching Safari
                var psi = new ProcessStartInfo("open", $"\"{nativeUrl}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
                launched = proc?.ExitCode == 0;
            }
            else
            {
                Process.Start(new ProcessStartInfo(nativeUrl) { UseShellExecute = true });
                launched = true;
            }
        }
        catch { }

        if (!launched)
        {
            try
            {
                Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
            }
            catch { }
        }

        // 6. On macOS, automatically activate WhatsApp and trigger Cmd+V after a brief delay
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1200);
                    var pasteScript = @"
tell application ""WhatsApp"" to activate
delay 0.3
tell application ""System Events""
    keystroke ""v"" using command down
end tell";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e '{pasteScript}'",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                catch { }
            });
        }

        return (true, pdfPath, nativeUrl);
    }
}
