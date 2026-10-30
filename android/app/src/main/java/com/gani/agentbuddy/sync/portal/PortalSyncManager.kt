package com.gani.agentbuddy.sync.portal

import android.annotation.SuppressLint
import android.content.Context
import android.webkit.JavascriptInterface
import android.webkit.WebView
import android.webkit.WebViewClient
import com.gani.agentbuddy.data.local.AgentBuddyDatabase
import com.gani.agentbuddy.data.model.RDAccount
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray

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
        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true

        val jsBridge = PortalJsBridge(agentId, accessCode)
        webView.addJavascriptInterface(jsBridge, "AgentBuddyMobile")

        webView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                val currentUrl = url?.lowercase() ?: ""
                if (currentUrl.contains("dopagent.indiapost.gov.in")) {
                    injectLoginAutomation(agentId, accessCode)
                }
            }
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

                    // Draw rendered captcha onto canvas to extract exact base64
                    var canvas = document.createElement('canvas');
                    canvas.width = captchaImg.naturalWidth || captchaImg.width;
                    canvas.height = captchaImg.naturalHeight || captchaImg.height;
                    var ctx = canvas.getContext('2d');
                    ctx.drawImage(captchaImg, 0, 0);

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

                var btn = document.getElementsByName('Action.VALIDATE_RM_PLUS_CREDENTIALS_CATCHA_DISABLED')[0];
                if (!btn) {
                    btn = document.querySelector("input[type='submit'][value*='Login']");
                }
                if (btn) {
                    btn.click();
                }
            })();
        """.trimIndent()

        targetWebView?.post {
            targetWebView?.evaluateJavascript(submitScript, null)
        }
    }

    inner class PortalJsBridge(private val agentId: String, private val accessCode: String) {

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
                    val accounts = mutableListOf<RDAccount>()
                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        val accNo = obj.optString("account_no", "").trim()
                        if (accNo.length >= 10) {
                            accounts.add(
                                RDAccount(
                                    accountNo = accNo,
                                    accountName = obj.optString("account_name", ""),
                                    aslaasNo = obj.optString("aslaas_no", ""),
                                    denomination = obj.optString("denomination", "0"),
                                    monthPaidUpto = obj.optString("month_paid_upto", ""),
                                    nextInstallmentDate = obj.optString("next_installment_date", ""),
                                    amount = obj.optLong("amount", 0L),
                                    monthPaidUptoNum = obj.optInt("month_paid_upto_num", 0),
                                    nextDueDateIso = obj.optString("next_due_date_iso", ""),
                                    status = "active",
                                    isActive = 1
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
