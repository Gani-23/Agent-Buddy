package com.gani.agentbuddy

import android.content.Context
import android.os.Bundle
import android.text.method.PasswordTransformationMethod
import android.view.WindowManager
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.lifecycleScope
import com.gani.agentbuddy.sync.portal.PortalSyncManager
import com.gani.agentbuddy.sync.portal.SyncState
import com.gani.agentbuddy.ui.dashboard.DashboardScreen
import com.gani.agentbuddy.ui.dashboard.DashboardViewModel

class MainActivity : ComponentActivity() {

    private val viewModel: DashboardViewModel by viewModels()
    private val syncManager by lazy { PortalSyncManager(this, lifecycleScope) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val prefs = getSharedPreferences("dop_credentials", Context.MODE_PRIVATE)
        val savedAgentId = prefs.getString("agent_id", "") ?: ""
        val savedPassword = prefs.getString("password", "") ?: ""

        setContent {
            MaterialTheme {
                var showPortalSyncDialog by remember { mutableStateOf(false) }
                var agentId by remember { mutableStateOf(savedAgentId) }
                var password by remember { mutableStateOf(savedPassword) }
                var passwordVisible by remember { mutableStateOf(false) }
                var isLoggingIn by remember { mutableStateOf(false) }
                var showLiveWebView by remember { mutableStateOf(true) }
                val syncState by syncManager.syncState.collectAsState()
                val loadProgress by syncManager.loadProgress.collectAsState()

                // Keep screen awake while sync dialog is open so sleep timer NEVER interrupts sync!
                DisposableEffect(showPortalSyncDialog) {
                    if (showPortalSyncDialog) {
                        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    }
                    onDispose {
                        window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    }
                }

                Box(modifier = Modifier.fillMaxSize()) {
                    DashboardScreen(
                        viewModel = viewModel,
                        onOpenPortalSync = { showPortalSyncDialog = true },
                        onOpenDesktopSync = { /* Handled via QR in dialog */ }
                    )

                    if (showPortalSyncDialog) {
                        Surface(
                            modifier = Modifier.fillMaxSize(),
                            color = MaterialTheme.colorScheme.background
                        ) {
                            Column(modifier = Modifier.fillMaxSize()) {
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 16.dp, vertical = 12.dp),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        Text("India Post Portal Sync", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                                        Box(
                                            modifier = Modifier
                                                .background(Color(0xFFDCFCE7), RoundedCornerShape(6.dp))
                                                .padding(horizontal = 6.dp, vertical = 2.dp)
                                        ) {
                                            Text("Screen Awake", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = Color(0xFF166534))
                                        }
                                    }
                                    IconButton(onClick = { 
                                        showPortalSyncDialog = false
                                        isLoggingIn = false
                                    }) {
                                        Icon(Icons.Default.Close, contentDescription = "Close")
                                    }
                                }

                                if (!isLoggingIn) {
                                    Column(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .padding(20.dp),
                                        verticalArrangement = Arrangement.spacedBy(14.dp)
                                    ) {
                                        Text(
                                            "Enter credentials to sync latest RD schedule. On-device ML Kit will automatically read & solve the CAPTCHA.",
                                            fontSize = 14.sp,
                                            color = Color(0xFF475569)
                                        )

                                        OutlinedTextField(
                                            value = agentId,
                                            onValueChange = { agentId = it },
                                            label = { Text("Agent ID") },
                                            modifier = Modifier.fillMaxWidth(),
                                            singleLine = true,
                                            shape = RoundedCornerShape(12.dp)
                                        )

                                        OutlinedTextField(
                                            value = password,
                                            onValueChange = { password = it },
                                            label = { Text("Password") },
                                            modifier = Modifier.fillMaxWidth(),
                                            singleLine = true,
                                            shape = RoundedCornerShape(12.dp),
                                            visualTransformation = if (passwordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                                            trailingIcon = {
                                                val image = if (passwordVisible) Icons.Filled.Visibility else Icons.Filled.VisibilityOff
                                                IconButton(onClick = { passwordVisible = !passwordVisible }) {
                                                    Icon(image, contentDescription = if (passwordVisible) "Hide password" else "Show password")
                                                }
                                            }
                                        )

                                        Button(
                                            onClick = { 
                                                prefs.edit()
                                                    .putString("agent_id", agentId)
                                                    .putString("password", password)
                                                    .apply()
                                                isLoggingIn = true 
                                            },
                                            enabled = agentId.isNotBlank() && password.isNotBlank(),
                                            shape = RoundedCornerShape(12.dp),
                                            modifier = Modifier
                                                .fillMaxWidth()
                                                .height(50.dp)
                                        ) {
                                            Text("Start Automatic Portal Sync", fontSize = 15.sp, fontWeight = FontWeight.Bold)
                                        }
                                    }
                                } else {
                                    // Portal Sync Active: Live Status Banner
                                    if (syncState is SyncState.Success) {
                                        LaunchedEffect(Unit) {
                                            kotlinx.coroutines.delay(1800)
                                            showPortalSyncDialog = false
                                        }
                                    }

                                    val isError = syncState is SyncState.Error
                                    val statusText = when (syncState) {
                                        is SyncState.Idle -> "Opening India Post portal..."
                                        is SyncState.LoadingPage -> "Loading portal (${loadProgress}%)..."
                                        is SyncState.SolvingCaptcha -> (syncState as SyncState.SolvingCaptcha).message
                                        is SyncState.Authenticating -> (syncState as SyncState.Authenticating).message
                                        is SyncState.NavigatingToAccounts -> "Navigating to Accounts & Schedule..."
                                        is SyncState.ScrapingAccounts -> (syncState as SyncState.ScrapingAccounts).progressText
                                        is SyncState.Success -> "✓ Synced ${(syncState as SyncState.Success).count} accounts!"
                                        is SyncState.Error -> (syncState as SyncState.Error).message
                                    }

                                    var manualCaptcha by remember { mutableStateOf("") }

                                    Surface(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .padding(horizontal = 16.dp),
                                        color = if (isError) Color(0xFFFEF2F2) else Color(0xFFEFF6FF),
                                        shape = RoundedCornerShape(14.dp),
                                        border = androidx.compose.foundation.BorderStroke(1.dp, if (isError) Color(0xFFFCA5A5) else Color(0xFFBFDBFE))
                                    ) {
                                        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                            Row(
                                                modifier = Modifier.fillMaxWidth(),
                                                horizontalArrangement = Arrangement.SpaceBetween,
                                                verticalAlignment = Alignment.CenterVertically
                                            ) {
                                                Text(
                                                    text = statusText,
                                                    color = if (isError) Color(0xFFDC2626) else Color(0xFF1E40AF),
                                                    fontSize = 14.sp,
                                                    fontWeight = FontWeight.Bold,
                                                    modifier = Modifier.weight(1f)
                                                )
                                                IconButton(
                                                    onClick = { showLiveWebView = !showLiveWebView },
                                                    modifier = Modifier.size(28.dp)
                                                ) {
                                                    Icon(
                                                        imageVector = Icons.Default.Language,
                                                        contentDescription = "Toggle Portal View",
                                                        tint = if (isError) Color(0xFFDC2626) else Color(0xFF2563EB),
                                                        modifier = Modifier.size(20.dp)
                                                    )
                                                }
                                            }

                                            // Linear Progress Bar
                                            if (loadProgress in 1..99) {
                                                LinearProgressIndicator(
                                                    progress = { loadProgress / 100f },
                                                    modifier = Modifier
                                                        .fillMaxWidth()
                                                        .height(5.dp),
                                                    color = Color(0xFF2563EB),
                                                    trackColor = Color(0xFFDBEAFE)
                                                )
                                            } else if (syncState is SyncState.Authenticating || syncState is SyncState.ScrapingAccounts || syncState is SyncState.SolvingCaptcha) {
                                                LinearProgressIndicator(
                                                    modifier = Modifier
                                                        .fillMaxWidth()
                                                        .height(5.dp),
                                                    color = Color(0xFF2563EB),
                                                    trackColor = Color(0xFFDBEAFE)
                                                )
                                            }

                                            // Manual CAPTCHA override if needed
                                            Row(
                                                modifier = Modifier.fillMaxWidth(),
                                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                                verticalAlignment = Alignment.CenterVertically
                                            ) {
                                                OutlinedTextField(
                                                    value = manualCaptcha,
                                                    onValueChange = { manualCaptcha = it },
                                                    placeholder = { Text("Manual CAPTCHA override", fontSize = 13.sp) },
                                                    modifier = Modifier.weight(1f),
                                                    singleLine = true,
                                                    shape = RoundedCornerShape(10.dp),
                                                    keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                                                        imeAction = androidx.compose.ui.text.input.ImeAction.Done
                                                    ),
                                                    keyboardActions = androidx.compose.foundation.text.KeyboardActions(
                                                        onDone = {
                                                            if (manualCaptcha.isNotBlank()) {
                                                                syncManager.submitLogin(manualCaptcha.trim(), password)
                                                            }
                                                        }
                                                    ),
                                                    colors = OutlinedTextFieldDefaults.colors(
                                                        focusedContainerColor = Color.White,
                                                        unfocusedContainerColor = Color.White
                                                    )
                                                )

                                                Button(
                                                    onClick = {
                                                        if (manualCaptcha.isNotBlank()) {
                                                            syncManager.submitLogin(manualCaptcha.trim(), password)
                                                        }
                                                    },
                                                    enabled = manualCaptcha.isNotBlank(),
                                                    shape = RoundedCornerShape(10.dp)
                                                ) {
                                                    Text("Submit")
                                                }

                                                IconButton(
                                                    onClick = { syncManager.retryExtractCaptcha(agentId, password) }
                                                ) {
                                                    Icon(Icons.Default.Refresh, contentDescription = "Retry OCR", tint = Color(0xFF2563EB))
                                                }
                                            }
                                        }
                                    }

                                    Spacer(modifier = Modifier.height(8.dp))

                                    // WebView Container with Loading Placeholder (No Blank White Screen!)
                                    Box(modifier = Modifier.fillMaxSize()) {
                                        AndroidView(
                                            modifier = Modifier.fillMaxSize(),
                                            factory = { context ->
                                                WebView(context).apply {
                                                    syncManager.attachWebView(this, agentId, password)
                                                    loadUrl("https://dopagent.indiapost.gov.in")
                                                }
                                            }
                                        )

                                        // Overlaid loading state so user never sees a bare white screen
                                        if (loadProgress < 80 && syncState !is SyncState.ScrapingAccounts && syncState !is SyncState.Success) {
                                            Box(
                                                modifier = Modifier
                                                    .fillMaxSize()
                                                    .background(Color(0xFFF8FAFC)),
                                                contentAlignment = Alignment.Center
                                            ) {
                                                Column(
                                                    horizontalAlignment = Alignment.CenterHorizontally,
                                                    verticalArrangement = Arrangement.spacedBy(14.dp),
                                                    modifier = Modifier.padding(24.dp)
                                                ) {
                                                    CircularProgressIndicator(
                                                        color = Color(0xFF2563EB),
                                                        strokeWidth = 3.5.dp,
                                                        modifier = Modifier.size(44.dp)
                                                    )
                                                    Text(
                                                        text = "Connecting to India Post...",
                                                        fontWeight = FontWeight.Bold,
                                                        fontSize = 16.sp,
                                                        color = Color(0xFF0F172A)
                                                    )
                                                    Text(
                                                        text = "Loading portal resources (${loadProgress}%)\nScreen is locked awake to prevent interruption.",
                                                        fontSize = 13.sp,
                                                        color = Color(0xFF64748B),
                                                        textAlign = androidx.compose.ui.text.style.TextAlign.Center
                                                    )
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
