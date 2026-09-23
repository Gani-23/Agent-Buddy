using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentBuddy.Services;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace AgentBuddy.Android.Services;

/// <summary>
/// Autonomous on-device Android portal automation using Android WebView.
/// Replicates the full Fetch_RDAccounts.py workflow (login, accounts navigation, printpreview/table extraction, and pagination).
/// </summary>
public sealed class AndroidPortalAutomationService : IPortalAutomationService
{
    private const string PortalUrl = "https://dopagent.indiapost.gov.in";
    private readonly Context _context;
    private readonly DatabaseService _databaseService;
    private readonly OnDeviceDataIngestionService _ingestionService;

    public AndroidPortalAutomationService(Context context, DatabaseService databaseService)
    {
        _context = context;
        _databaseService = databaseService;
        _ingestionService = new OnDeviceDataIngestionService(databaseService);
    }

    public bool CanRunOnDevice => true;

    /// <summary>
    /// Executes the on-device portal automation workflow:
    /// 1. Initialize WebView dialog and load portal
    /// 2. Autofill Agent ID and Password
    /// 3. Wait for CAPTCHA & form submission
    /// 4. Navigate to Accounts -> Agent Enquire and Update Screen
    /// 5. Trigger print preview and extract all account pages
    /// 6. Ingest into local SQLite database
    /// </summary>
    public async Task<(bool success, int fetchedCount, string message)> FetchAccountsAsync(
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<(bool, int, string)>();

        var mainHandler = new Handler(Looper.MainLooper!);
        mainHandler.Post(async () =>
        {
            Dialog? dialog = null;
            try
            {
                statusCallback?.Invoke("Loading saved credentials...");
                var (savedAgentId, savedPassword) = await _databaseService.GetSavedCredentialsAsync();
                var agentId = !string.IsNullOrWhiteSpace(savedAgentId) ? savedAgentId : "DOPMI5158650200005";
                var password = savedPassword ?? "";

                statusCallback?.Invoke("Initializing DOP Portal engine...");

                // Create container layout for the in-app portal browser
                var layout = new LinearLayout(_context)
                {
                    Orientation = Orientation.Vertical
                };
                layout.SetPadding(16, 16, 16, 16);

                // Header status
                var titleText = new TextView(_context)
                {
                    Text = "India Post DOP Agent Portal Automation",
                    TextSize = 16,
                    Typeface = Typeface.DefaultBold
                };
                titleText.SetPadding(8, 8, 8, 8);
                layout.AddView(titleText);

                var statusText = new TextView(_context)
                {
                    Text = "Connecting to dopagent.indiapost.gov.in...",
                    TextSize = 12
                };
                statusText.SetPadding(8, 0, 8, 8);
                layout.AddView(statusText);

                // WebView setup
                var webView = new WebView(_context);
                webView.Settings.JavaScriptEnabled = true;
                webView.Settings.DomStorageEnabled = true;
                webView.Settings.LoadsImagesAutomatically = true;
                webView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
                webView.Settings.SetSupportMultipleWindows(true);
                webView.Settings.UserAgentString = "Mozilla/5.0 (Linux; Android 14; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36";

                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1200);
                webView.LayoutParameters = lp;
                layout.AddView(webView);

                // Action buttons (Cancel / Finish)
                var buttonLayout = new LinearLayout(_context)
                {
                    Orientation = Orientation.Horizontal
                };
                var cancelButton = new Button(_context) { Text = "Cancel" };
                var finishButton = new Button(_context) { Text = "Done Syncing" };
                buttonLayout.AddView(cancelButton);
                buttonLayout.AddView(finishButton);
                layout.AddView(buttonLayout);

                dialog = new Dialog(_context);
                dialog.SetContentView(layout);
                dialog.SetCancelable(true);

                var extractedRecords = new List<PortalRawAccountRecord>();
                var isDone = false;

                void FinishExtraction(List<PortalRawAccountRecord> records)
                {
                    if (isDone) return;
                    isDone = true;

                    mainHandler.Post(async () =>
                    {
                        try
                        {
                            dialog?.Dismiss();
                            statusCallback?.Invoke($"Ingesting {records.Count} records into local database...");
                            var (newCount, updatedCount, removedCount) = await _ingestionService.IngestPortalAccountsAsync(records);
                            var summary = $"Sync Complete! Fetched: {records.Count}, New: {newCount}, Updated: {updatedCount}, Matured/Closed: {removedCount}.";
                            tcs.TrySetResult((true, records.Count, summary));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetResult((false, 0, $"Ingestion error: {ex.Message}"));
                        }
                    });
                }

                void FailExtraction(string error)
                {
                    if (isDone) return;
                    isDone = true;
                    mainHandler.Post(() =>
                    {
                        dialog?.Dismiss();
                        tcs.TrySetResult((false, 0, error));
                    });
                }

                cancelButton.Click += (s, e) =>
                {
                    dialog?.Dismiss();
                    if (!isDone)
                    {
                        isDone = true;
                        tcs.TrySetResult((false, 0, "Sync cancelled by user."));
                    }
                };

                finishButton.Click += (s, e) =>
                {
                    if (extractedRecords.Count > 0)
                    {
                        FinishExtraction(extractedRecords);
                    }
                    else
                    {
                        // Trigger final extraction attempt
                        webView.EvaluateJavascript(GetTableExtractionScript(), new JavaValueCallback(jsonResult =>
                        {
                            var list = ParseRecordsFromJson(jsonResult);
                            if (list.Count > 0)
                            {
                                FinishExtraction(list);
                            }
                            else
                            {
                                FailExtraction("No account records extracted yet.");
                            }
                        }));
                    }
                };

                webView.SetWebViewClient(new PortalWebViewClient(
                    agentId,
                    password,
                    msg =>
                    {
                        mainHandler.Post(() =>
                        {
                            statusText.Text = msg;
                            statusCallback?.Invoke(msg);
                        });
                    },
                    records =>
                    {
                        FinishExtraction(records);
                    },
                    error =>
                    {
                        FailExtraction(error);
                    }
                ));

                dialog.Show();
                webView.LoadUrl(PortalUrl);
            }
            catch (Exception ex)
            {
                dialog?.Dismiss();
                tcs.TrySetResult((false, 0, $"Android Portal Error: {ex.Message}"));
            }
        });

        return await tcs.Task;
    }

    private static string GetTableExtractionScript()
    {
        return @"
        (function() {
            var rows = document.querySelectorAll('table#SummaryList tr, table tr');
            var data = [];
            for (var i = 0; i < rows.length; i++) {
                var cells = rows[i].querySelectorAll('td');
                if (cells.length >= 5) {
                    var acct = (cells[1] ? cells[1].innerText : '').trim();
                    if (/^\d{10,}$/.test(acct)) {
                        data.push({
                            AccountNo: acct,
                            AccountName: (cells[2] ? cells[2].innerText : '').trim(),
                            Denomination: (cells[3] ? cells[3].innerText : '').trim(),
                            MonthPaidUpto: (cells[4] ? cells[4].innerText : '').trim(),
                            NextInstallmentDate: (cells[5] ? cells[5].innerText : '').trim()
                        });
                    }
                }
            }
            return JSON.stringify(data);
        })();";
    }

    private static List<PortalRawAccountRecord> ParseRecordsFromJson(string? jsonResult)
    {
        var list = new List<PortalRawAccountRecord>();
        try
        {
            if (!string.IsNullOrWhiteSpace(jsonResult) && jsonResult != "null" && jsonResult != "\"[]\"")
            {
                var clean = jsonResult.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<PortalRawAccountRecord>>(clean);
                if (parsed != null && parsed.Count > 0)
                {
                    list.AddRange(parsed);
                }
            }
        }
        catch { }
        return list;
    }

    private sealed class PortalWebViewClient : WebViewClient
    {
        private readonly string _agentId;
        private readonly string _password;
        private readonly Action<string>? _statusCallback;
        private readonly Action<List<PortalRawAccountRecord>> _onSuccess;
        private readonly Action<string> _onError;
        private readonly List<PortalRawAccountRecord> _accumulatedRecords = new();

        public PortalWebViewClient(
            string agentId,
            string password,
            Action<string>? statusCallback,
            Action<List<PortalRawAccountRecord>> onSuccess,
            Action<string> onError)
        {
            _agentId = agentId;
            _password = password;
            _statusCallback = statusCallback;
            _onSuccess = onSuccess;
            _onError = onError;
        }

        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            if (view == null || string.IsNullOrWhiteSpace(url)) return;

            var lowerUrl = url.ToLowerInvariant();

            // Step 1: Login Page
            if (lowerUrl.Contains("dopagent.indiapost.gov.in") &&
                !lowerUrl.Contains("bankuser") &&
                !lowerUrl.Contains("agent") &&
                !lowerUrl.Contains("account"))
            {
                _statusCallback?.Invoke("Autofilling credentials... please solve CAPTCHA & tap Login if needed.");

                var loginJs = $@"
                    (function() {{
                        var u = document.querySelector('input[name=""AuthenticationFG.USER_PRINCIPAL""]');
                        var p = document.querySelector('input[name=""AuthenticationFG.ACCESS_CODE""]');
                        if (u && !u.value) {{
                            u.value = '{_agentId}';
                            u.dispatchEvent(new Event('input', {{bubbles:true}}));
                            u.dispatchEvent(new Event('change', {{bubbles:true}}));
                        }}
                        if (p && !p.value && '{_password}'.length > 0) {{
                            p.value = '{_password}';
                            p.dispatchEvent(new Event('input', {{bubbles:true}}));
                            p.dispatchEvent(new Event('change', {{bubbles:true}}));
                        }}
                    }})();";

                view.EvaluateJavascript(loginJs, null);
                return;
            }

            // Step 2: Main Menu -> Navigate to Agent Enquire & Update Screen
            if (lowerUrl.Contains("bankuser") || lowerUrl.Contains("agent") || lowerUrl.Contains("account"))
            {
                _statusCallback?.Invoke("Checking account list page...");

                var navigateOrExtractJs = @"
                    (function() {
                        // Check if we are on the accounts enquire screen
                        var printBtn = document.querySelector('#printpreview, img[src*=""btn-printscreen.gif""], input[name=""Action.FETCH_INPUT_ACCOUNT""]');
                        if (printBtn) {
                            return 'on_account_screen';
                        }

                        // Try navigating to Accounts -> Agent Enquire & Update Screen
                        var links = document.querySelectorAll('a');
                        for (var i = 0; i < links.length; i++) {
                            var text = links[i].innerText || '';
                            if (text.indexOf('Agent Enquire & Update Screen') !== -1 ||
                                text.indexOf('Agent Enquire') !== -1) {
                                links[i].click();
                                return 'navigating_enquire';
                            }
                        }
                        for (var j = 0; j < links.length; j++) {
                            var t = links[j].innerText || '';
                            if (t.trim() === 'Accounts') {
                                links[j].click();
                                return 'clicked_accounts_menu';
                            }
                        }
                        return 'unknown_state';
                    })();";

                view.EvaluateJavascript(navigateOrExtractJs, new JavaValueCallback(stepResult =>
                {
                    var cleanStep = (stepResult ?? string.Empty).Trim('"');
                    if (cleanStep == "on_account_screen" || cleanStep == "unknown_state")
                    {
                        // Step 3: Trigger extract table rows
                        _statusCallback?.Invoke("Extracting account data rows...");

                        view.EvaluateJavascript(GetTableExtractionScript(), new JavaValueCallback(jsonResult =>
                        {
                            var list = ParseRecordsFromJson(jsonResult);
                            if (list.Count > 0)
                            {
                                foreach (var item in list)
                                {
                                    if (!_accumulatedRecords.Exists(r => r.AccountNo == item.AccountNo))
                                    {
                                        _accumulatedRecords.Add(item);
                                    }
                                }

                                _statusCallback?.Invoke($"Extracted {_accumulatedRecords.Count} accounts so far...");

                                // Check if next page button exists
                                var checkNextJs = @"
                                    (function() {
                                        var nextBtn = document.querySelector('input[name=""Action.NEXT_ACCOUNTS""], input[name=""Action.AgentRDActSummaryAllListing.GOTO_NEXT__""], input[value="">""]');
                                        if (nextBtn && !nextBtn.disabled) {
                                            nextBtn.click();
                                            return 'clicked_next';
                                        }
                                        return 'done';
                                    })();";

                                view.EvaluateJavascript(checkNextJs, new JavaValueCallback(nextRes =>
                                {
                                    if ((nextRes ?? "").Trim('"') == "done" && _accumulatedRecords.Count > 0)
                                    {
                                        _onSuccess(_accumulatedRecords);
                                    }
                                }));
                            }
                        }));
                    }
                }));
            }
        }
    }

    private sealed class JavaValueCallback : Java.Lang.Object, IValueCallback
    {
        private readonly Action<string> _callback;

        public JavaValueCallback(Action<string> callback)
        {
            _callback = callback;
        }

        public void OnReceiveValue(Java.Lang.Object? value)
        {
            _callback(value?.ToString() ?? string.Empty);
        }
    }
}
