using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentBuddy.Services;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
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
    private const string Tag = "AgentBuddyPortal";
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
    /// 3. Wait for CAPTCHA and form submission
    /// 4. Navigate to Accounts -> Agent Enquire and Update Screen
    /// 5. Trigger print preview and extract all account pages
    /// 6. Ingest into local SQLite database
    /// </summary>
    public async Task<(bool success, int fetchedCount, string message)> FetchAccountsAsync(
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        Log.Info(Tag, "FetchAccountsAsync initiated on Android device.");
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

                Log.Info(Tag, $"Loaded credentials for Agent ID: {agentId}");
                statusCallback?.Invoke("Opening DOP Agent Portal browser...");

                var actContext = MainActivity.Instance ?? _context;

                // Container layout
                var layout = new LinearLayout(actContext)
                {
                    Orientation = Orientation.Vertical
                };
                layout.SetPadding(24, 24, 24, 24);

                // Top Header Bar
                var headerBar = new LinearLayout(actContext)
                {
                    Orientation = Orientation.Horizontal
                };
                var titleText = new TextView(actContext)
                {
                    Text = "DOP Portal Sync",
                    TextSize = 18,
                    Typeface = Typeface.DefaultBold
                };
                titleText.SetTextColor(Color.Black);
                titleText.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                headerBar.AddView(titleText);

                var closeBtn = new Button(actContext) { Text = "✕ Close" };
                headerBar.AddView(closeBtn);
                layout.AddView(headerBar);

                var statusText = new TextView(actContext)
                {
                    Text = "Connecting to dopagent.indiapost.gov.in...",
                    TextSize = 12
                };
                statusText.SetTextColor(Color.DarkGray);
                statusText.SetPadding(0, 8, 0, 12);
                layout.AddView(statusText);

                // In-App WebView
                var webView = new WebView(actContext);
                webView.Settings.JavaScriptEnabled = true;
                webView.Settings.DomStorageEnabled = true;
                webView.Settings.LoadsImagesAutomatically = true;
                webView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
                webView.Settings.SetSupportMultipleWindows(true);
                webView.Settings.UserAgentString = "Mozilla/5.0 (Linux; Android 14; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36";

                var webLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f);
                webView.LayoutParameters = webLp;
                layout.AddView(webView);

                // Bottom Action Button
                var finishButton = new Button(actContext)
                {
                    Text = "Save & Complete Sync",
                    TextSize = 14,
                    Typeface = Typeface.DefaultBold
                };
                finishButton.SetPadding(16, 16, 16, 16);
                layout.AddView(finishButton);

                dialog = new Dialog(actContext, global::Android.Resource.Style.ThemeDeviceDefaultLightNoActionBarFullscreen);
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
                            Log.Info(Tag, $"Extraction complete with {records.Count} records. Ingesting into SQLite...");
                            statusCallback?.Invoke($"Ingesting {records.Count} records into local SQLite database...");
                            var (newCount, updatedCount, removedCount) = await _ingestionService.IngestPortalAccountsAsync(records);
                            var summary = $"Sync Complete! Fetched: {records.Count}, New: {newCount}, Updated: {updatedCount}, Matured/Closed: {removedCount}.";
                            tcs.TrySetResult((true, records.Count, summary));
                        }
                        catch (Exception ex)
                        {
                            Log.Error(Tag, $"Ingestion exception: {ex.Message}");
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
                        Log.Error(Tag, $"Automation failed: {error}");
                        tcs.TrySetResult((false, 0, error));
                    });
                }

                closeBtn.Click += (s, e) =>
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
                        webView.EvaluateJavascript(GetTableExtractionScript(), new JavaValueCallback(jsonResult =>
                        {
                            var list = ParseRecordsFromJson(jsonResult);
                            if (list.Count > 0)
                            {
                                FinishExtraction(list);
                            }
                            else
                            {
                                FailExtraction("No account records extracted yet. Make sure you are logged in and on the accounts screen.");
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
                            Log.Info(Tag, $"Status: {msg}");
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

                Log.Info(Tag, $"Loading URL: {PortalUrl}");
                dialog.Show();
                webView.LoadUrl(PortalUrl);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Exception in FetchAccountsAsync: {ex.Message}");
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
        catch (Exception ex)
        {
            Log.Error(Tag, $"JSON Parse error: {ex.Message}");
        }
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
            Log.Info(Tag, $"OnPageFinished URL: {url}");

            // Step 1: Login Page
            if (lowerUrl.Contains("dopagent.indiapost.gov.in") &&
                !lowerUrl.Contains("bankuser") &&
                !lowerUrl.Contains("agent") &&
                !lowerUrl.Contains("account"))
            {
                _statusCallback?.Invoke("Autofilling credentials... solve CAPTCHA and tap Sign In / Login.");

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
                _statusCallback?.Invoke("Navigating to Accounts -> Agent Enquire & Update Screen...");

                var navigateOrExtractJs = @"
                    (function() {
                        var printBtn = document.querySelector('#printpreview, img[src*=""btn-printscreen.gif""], input[name=""Action.FETCH_INPUT_ACCOUNT""]');
                        if (printBtn) {
                            return 'on_account_screen';
                        }

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
                    Log.Info(Tag, $"Navigation Step result: {cleanStep}");

                    if (cleanStep == "on_account_screen" || cleanStep == "unknown_state")
                    {
                        // Step 3: Trigger extract table rows
                        _statusCallback?.Invoke("Extracting account data rows...");

                        view.EvaluateJavascript(GetTableExtractionScript(), new JavaValueCallback(jsonResult =>
                        {
                            var list = ParseRecordsFromJson(jsonResult);
                            Log.Info(Tag, $"Parsed {list.Count} accounts from current page.");

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
                                    var cleanNext = (nextRes ?? "").Trim('"');
                                    Log.Info(Tag, $"Pagination Step: {cleanNext}");
                                    if (cleanNext == "done" && _accumulatedRecords.Count > 0)
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
