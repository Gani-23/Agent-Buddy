package com.gani.agentbuddy.sync.portal

import android.annotation.SuppressLint
import android.content.Context
import android.net.http.SslError
import android.webkit.*
import com.gani.agentbuddy.data.local.AgentBuddyDatabase
import com.gani.agentbuddy.data.model.RDAccount
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import java.text.SimpleDateFormat
import java.util.Locale

sealed class SyncState {
    object Idle : SyncState()
    object LoadingPage : SyncState()
    object SolvingCaptcha : SyncState()
    object Authenticating : SyncState()
    object NavigatingToAccounts : SyncState()
    data class ScrapingAccounts(val progressText: String) : SyncState()
    data class Success(val count: Int) : SyncState()
    data class Error(val message: String) : SyncState()
}

class PortalSyncManager(
    private val context: Context,
    private val scope: CoroutineScope
) {
    private val database = AgentBuddyDatabase.getDatabase(context)
    private val _syncState = MutableStateFlow<SyncState>(SyncState.Idle)
    val syncState: StateFlow<SyncState> = _syncState

    private var targetWebView: WebView? = null

    @SuppressLint("SetJavaScriptEnabled")
    fun attachWebView(webView: WebView, agentId: String, accessCode: String) {
        targetWebView = webView
        webView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            databaseEnabled = true
            useWideViewPort = true
            loadWithOverviewMode = true
            mixedContentMode = WebSettings.MIXED_CONTENT_ALWAYS_ALLOW
            userAgentString = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        }

        val jsBridge = PortalJsBridge(agentId, accessCode)
        webView.addJavascriptInterface(jsBridge, "AgentBuddyMobile")

        webView.webChromeClient = object : WebChromeClient() {
            override fun onProgressChanged(view: WebView?, newProgress: Int) {
                if (newProgress < 100 && _syncState.value is SyncState.Idle) {
                    _syncState.value = SyncState.LoadingPage
                }
            }
        }

        webView.webViewClient = object : WebViewClient() {
            @SuppressLint("WebViewClientOnReceivedSslError")
            override fun onReceivedSslError(view: WebView?, handler: SslErrorHandler?, error: SslError?) {
                // Allow India Post government SSL certificate chains
                handler?.proceed()
            }

            override fun onReceivedError(view: WebView?, request: WebResourceRequest?, error: WebResourceError?) {
                super.onReceivedError(view, request, error)
                if (request?.isForMainFrame == true) {
                    _syncState.value = SyncState.Error("Network error: ${error?.description}")
                }
            }

            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                val currentUrl = url?.lowercase() ?: ""
                if (currentUrl.contains("dopagent.indiapost.gov.in")) {
                    handlePageLifecycle(agentId, accessCode)
                }
            }
        }
    }

    fun handlePageLifecycle(agentId: String, accessCode: String) {
        val script = """
            (function() {
                // 1. If on login form
                var userField = document.getElementsByName('AuthenticationFG.USER_PRINCIPAL')[0];
                var captchaImg = document.getElementById('IMAGECAPTCHA');
                if (userField && captchaImg) {
                    userField.value = '$agentId';
                    var passField = document.getElementsByName('AuthenticationFG.ACCESS_CODE')[0];
                    if (passField) passField.value = '$accessCode';
                    
                    var scale = 3.5;
                    var w = captchaImg.naturalWidth || captchaImg.width || 120;
                    var h = captchaImg.naturalHeight || captchaImg.height || 35;
                    var canvas = document.createElement('canvas');
                    canvas.width = Math.round(w * scale);
                    canvas.height = Math.round(h * scale);
                    var ctx = canvas.getContext('2d');
                    ctx.imageSmoothingEnabled = true;
                    ctx.imageSmoothingQuality = 'high';
                    ctx.fillStyle = '#FFFFFF';
                    ctx.fillRect(0, 0, canvas.width, canvas.height);
                    ctx.drawImage(captchaImg, 0, 0, canvas.width, canvas.height);

                    var base64 = canvas.toDataURL('image/png');
                    AgentBuddyMobile.onCaptchaExtracted(base64);
                    return;
                }

                // 2. If on Deposit Accounts screen:
                // Always click "Fetch More Accounts" first before opening print preview!
                var nextBtn = document.getElementById("NEXT_ACCOUNTS") 
                           || document.querySelector("input[name='Action.NEXT_ACCOUNTS']")
                           || document.querySelector("input[value*='Fetch More Accounts']");
                var alreadyFetchedMore = sessionStorage.getItem("agentbuddy_fetched_more") === "true";

                if (nextBtn && !nextBtn.disabled && !alreadyFetchedMore) {
                    sessionStorage.setItem("agentbuddy_fetched_more", "true");
                    AgentBuddyMobile.onStepStatus("Clicking 'Fetch More Accounts'...");
                    nextBtn.click();
                    return;
                }

                // Once 'Fetch More Accounts' is clicked (or if unavailable), download schedule via Print Preview
                var printLink = document.getElementById('HREF_printPreview');
                if (printLink) {
                    sessionStorage.removeItem("agentbuddy_fetched_more");
                    AgentBuddyMobile.onStepStatus("Downloading schedule report via Print Preview...");
                    fetch(printLink.href)
                        .then(function(res) { return res.text(); })
                        .then(function(html) {
                            var parser = new DOMParser();
                            var doc = parser.parseFromString(html, "text/html");
                            var trs = doc.querySelectorAll("tr");
                            var accounts = [];
                            for (var i = 0; i < trs.length; i++) {
                                var cells = Array.from(trs[i].querySelectorAll("td, th")).map(function(c) { return c.innerText.trim(); });
                                if (cells.length >= 5) {
                                    var rowText = cells.join("\t");
                                    if (rowText.indexOf("Account No") === -1) {
                                        for (var c = 0; c < cells.length; c++) {
                                            if (/^\d{10,12}$/.test(cells[c])) {
                                                var accNo = cells[c];
                                                var name = cells[c + 1] || "";
                                                var denom = cells[c + 2] || "0";
                                                var monthPaid = cells[c + 3] || "";
                                                var nextDate = cells[c + 4] || "";
                                                accounts.push({
                                                    account_no: accNo,
                                                    account_name: name,
                                                    denomination: denom,
                                                    month_paid_upto: monthPaid,
                                                    next_installment_date: nextDate
                                                });
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            if (accounts.length > 0) {
                                AgentBuddyMobile.onStepStatus("Scraped " + accounts.length + " accounts! Saving...");
                                AgentBuddyMobile.onAccountsScraped(JSON.stringify(accounts));
                            } else {
                                AgentBuddyMobile.onStepStatus("No accounts found in schedule report.");
                            }
                        })
                        .catch(function(err) {
                            AgentBuddyMobile.onStepStatus("Report fetch failed: " + err);
                        });
                    return;
                }

                // 3. Fallback: Direct table scraping on Deposit Accounts screen
                var tables = document.querySelectorAll('table');
                var directResults = [];
                for (var t = 0; t < tables.length; t++) {
                    var trs = tables[t].querySelectorAll('tr');
                    for (var r = 0; r < trs.length; r++) {
                        var cells = trs[r].querySelectorAll('th, td');
                        if (cells.length >= 4) {
                            var cellTexts = [];
                            for (var c = 0; c < cells.length; c++) {
                                cellTexts.push((cells[c].innerText || cells[c].textContent || '').trim());
                            }
                            var rowStr = cellTexts.join('\t');
                            if (rowStr.indexOf('Account No') === -1) {
                                var accMatch = rowStr.match(/\b\d{10,12}\b/);
                                if (accMatch) {
                                    directResults.push({
                                        account_no: accMatch[0],
                                        account_name: cellTexts[2] || cellTexts[1] || '',
                                        denomination: (cellTexts[3] || '0').replace(/[^\d.]/g, ''),
                                        month_paid_upto: cellTexts[4] || '',
                                        next_installment_date: cellTexts[5] || cellTexts[4] || ''
                                    });
                                }
                            }
                        }
                    }
                }
                if (directResults.length > 0) {
                    AgentBuddyMobile.onAccountsScraped(JSON.stringify(directResults));
                    return;
                }

                // 4. If Accounts menu opened, click "Agent Enquire & Update Screen"
                var enquireLink = document.getElementById("Agent Enquire & Update Screen") 
                               || document.querySelector("a[name='HREF_Agent Enquire & Update Screen']")
                               || Array.from(document.querySelectorAll("a")).find(function(a) { return a.innerText && a.innerText.indexOf("Agent Enquire") !== -1; });
                if (enquireLink) {
                    AgentBuddyMobile.onStepStatus("Opening Agent Enquire Screen...");
                    window.location.href = enquireLink.href;
                    return;
                }

                // 5. If on Dashboard, click "Accounts"
                var accLink = document.getElementById("Accounts") || document.querySelector("a[name='HREF_Accounts']");
                if (accLink) {
                    AgentBuddyMobile.onStepStatus("Navigating to Accounts menu...");
                    window.location.href = accLink.href;
                    return;
                }
            })();
        """.trimIndent()

        targetWebView?.post {
            targetWebView?.evaluateJavascript(script, null)
        }
    }

    fun retryExtractCaptcha(agentId: String, accessCode: String) {
        _syncState.value = SyncState.SolvingCaptcha
        targetWebView?.post {
            injectLoginAutomation(agentId, accessCode)
        }
    }

    private fun injectLoginAutomation(agentId: String, accessCode: String) {
        val script = """
            (function() {
                var userField = document.getElementsByName('AuthenticationFG.USER_PRINCIPAL')[0];
                var passField = document.getElementsByName('AuthenticationFG.ACCESS_CODE')[0];
                var captchaImg = document.getElementById('IMAGECAPTCHA');

                if (userField && passField && captchaImg) {
                    userField.value = '$agentId';
                    passField.value = '$accessCode';

                    // Zoom / Scale rendered captcha 3.5x onto canvas for clear OCR
                    var scale = 3.5;
                    var w = captchaImg.naturalWidth || captchaImg.width || 120;
                    var h = captchaImg.naturalHeight || captchaImg.height || 35;
                    var canvas = document.createElement('canvas');
                    canvas.width = Math.round(w * scale);
                    canvas.height = Math.round(h * scale);
                    var ctx = canvas.getContext('2d');
                    ctx.imageSmoothingEnabled = true;
                    ctx.imageSmoothingQuality = 'high';
                    ctx.fillStyle = '#FFFFFF';
                    ctx.fillRect(0, 0, canvas.width, canvas.height);
                    ctx.drawImage(captchaImg, 0, 0, canvas.width, canvas.height);

                    var base64 = canvas.toDataURL('image/png');
                    AgentBuddyMobile.onCaptchaExtracted(base64);
                }
            })();
        """.trimIndent()

        targetWebView?.evaluateJavascript(script, null)
    }

    fun submitLogin(captchaCode: String) {
        _syncState.value = SyncState.Authenticating
        val submitScript = """
            (function() {
                var codeField = document.getElementsByName('AuthenticationFG.VERIFICATION_CODE')[0];
                if (codeField) {
                    codeField.value = '$captchaCode';
                    codeField.dispatchEvent(new Event('input', {bubbles: true}));
                    codeField.dispatchEvent(new Event('change', {bubbles: true}));
                }

                var btn = document.getElementsByName('Action.VALIDATE_RM_PLUS_CREDENTIALS_CATCHA_DISABLED')[0]
                       || document.querySelector("input[type='submit'][value*='Log']")
                       || document.querySelector("input[type='submit']");
                if (btn) {
                    btn.click();
                }
            })();
        """.trimIndent()

        targetWebView?.post {
            targetWebView?.evaluateJavascript(submitScript, null)
        }
    }

    private fun parseDenomination(denom: String?): Long {
        if (denom.isNullOrBlank()) return 0L
        val cleaned = denom.replace("Cr.", "", ignoreCase = true).replace(",", "").trim()
        return cleaned.toDoubleOrNull()?.toLong() ?: 0L
    }

    private fun parseMonthPaidNumber(monthPaid: String?): Int {
        if (monthPaid.isNullOrBlank()) return 0
        val match = Regex("""\d+""").find(monthPaid)
        return match?.value?.toIntOrNull() ?: 0
    }

    private fun parseDueDateIso(dateStr: String?): String {
        if (dateStr.isNullOrBlank()) return ""
        val trimmed = dateStr.trim()
        val formats = listOf("dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMMM-yyyy", "d-MMMM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy")
        for (fmt in formats) {
            try {
                val sdf = SimpleDateFormat(fmt, Locale.ENGLISH)
                sdf.isLenient = false
                val parsed = sdf.parse(trimmed)
                if (parsed != null) {
                    val outSdf = SimpleDateFormat("yyyy-MM-dd", Locale.ENGLISH)
                    return outSdf.format(parsed)
                }
            } catch (_: Exception) {}
        }
        return ""
    }

    inner class PortalJsBridge(private val agentId: String, private val accessCode: String) {

        @JavascriptInterface
        fun onStepStatus(status: String) {
            _syncState.value = SyncState.ScrapingAccounts(status)
        }

        @JavascriptInterface
        fun onCaptchaExtracted(base64Data: String) {
            _syncState.value = SyncState.SolvingCaptcha
            scope.launch(Dispatchers.IO) {
                val resolvedCaptcha = CaptchaOcrProcessor.recognizeFromBase64(base64Data)
                if (resolvedCaptcha.isNotEmpty()) {
                    submitLogin(resolvedCaptcha)
                } else {
                    _syncState.value = SyncState.Error("OCR could not read CAPTCHA clearly. Please tap retry.")
                }
            }
        }

        @JavascriptInterface
        fun onAccountsScraped(jsonPayload: String) {
            scope.launch(Dispatchers.IO) {
                try {
                    val array = JSONArray(jsonPayload)
                    val existingMap = database.rdAccountDao().getAllAccountsList().associateBy { it.accountNo }
                    val accounts = mutableListOf<RDAccount>()

                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        val accNo = obj.optString("account_no", "").trim()
                        if (accNo.length >= 10) {
                            val denomStr = obj.optString("denomination", "0")
                            val monthPaidStr = obj.optString("month_paid_upto", "")
                            val nextDateStr = obj.optString("next_installment_date", "")

                            val amount = parseDenomination(denomStr)
                            val monthPaidNum = parseMonthPaidNumber(monthPaidStr)
                            val nextDueIso = parseDueDateIso(nextDateStr)
                            val totalDeposit = if (amount > 0 && monthPaidNum > 0) amount * monthPaidNum else 0L

                            val existing = existingMap[accNo]

                            accounts.add(
                                RDAccount(
                                    id = existing?.id ?: 0,
                                    accountNo = accNo,
                                    accountName = obj.optString("account_name", "").trim(),
                                    aslaasNo = obj.optString("aslaas_no", "").takeIf { it.isNotBlank() } ?: existing?.aslaasNo ?: "",
                                    denomination = denomStr,
                                    monthPaidUpto = monthPaidStr,
                                    nextInstallmentDate = nextDateStr,
                                    amount = amount,
                                    monthPaidUptoNum = monthPaidNum,
                                    nextDueDateIso = nextDueIso,
                                    totalDeposit = totalDeposit,
                                    status = "active",
                                    isActive = 1,
                                    shortCode = existing?.shortCode
                                )
                            )
                        }
                    }

                    if (accounts.isNotEmpty()) {
                        database.rdAccountDao().insertOrUpdateAll(accounts)
                        _syncState.value = SyncState.Success(accounts.size)
                    } else {
                        _syncState.value = SyncState.Error("No accounts found in schedule.")
                    }
                } catch (e: Exception) {
                    _syncState.value = SyncState.Error("Parsing error: ${e.message}")
                }
            }
        }
    }
}
