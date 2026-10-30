package com.gani.agentbuddy

import android.os.Bundle
import android.webkit.WebView
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
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

        setContent {
            MaterialTheme {
                var showPortalSyncDialog by remember { mutableStateOf(false) }
                var agentId by remember { mutableStateOf("") }
                var password by remember { mutableStateOf("") }
                var isLoggingIn by remember { mutableStateOf(false) }
                val syncState by syncManager.syncState.collectAsState()

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
                                        .padding(16.dp),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text("DOP Portal Sync", style = MaterialTheme.typography.titleMedium)
                                    IconButton(onClick = { showPortalSyncDialog = false }) {
                                        Icon(Icons.Default.Close, contentDescription = "Close")
                                    }
                                }

                                if (!isLoggingIn) {
                                    Column(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .padding(20.dp),
                                        verticalArrangement = Arrangement.spacedBy(12.dp)
                                    ) {
                                        Text(
                                            "Enter credentials to sync latest RD schedule. On-device ML Kit will automatically read & solve the CAPTCHA.",
                                            fontSize = 13.sp,
                                            color = Color(0xFF6B7280)
                                        )

                                        OutlinedTextField(
                                            value = agentId,
                                            onValueChange = { agentId = it },
                                            label = { Text("Agent ID") },
                                            modifier = Modifier.fillMaxWidth(),
                                            singleLine = true
                                        )

                                        OutlinedTextField(
                                            value = password,
                                            onValueChange = { password = it },
                                            label = { Text("Password") },
                                            modifier = Modifier.fillMaxWidth(),
                                            singleLine = true
                                        )

                                        Button(
                                            onClick = { isLoggingIn = true },
                                            enabled = agentId.isNotBlank() && password.isNotBlank(),
                                            modifier = Modifier.fillMaxWidth()
                                        ) {
                                            Text("Start Automatic Portal Sync")
                                        }
                                    }
                                } else {
                                    // Portal Sync Active: Live Status Banner
                                    val statusText = when (syncState) {
                                        is SyncState.Idle -> "Opening portal..."
                                        is SyncState.LoadingPage -> "Loading DOP Agent page..."
                                        is SyncState.SolvingCaptcha -> "Reading CAPTCHA with On-Device ML Kit OCR..."
                                        is SyncState.Authenticating -> "Submitting login..."
                                        is SyncState.NavigatingToAccounts -> "Navigating to Schedule Report..."
                                        is SyncState.ScrapingAccounts -> (syncState as SyncState.ScrapingAccounts).progressText
                                        is SyncState.Success -> "✓ Synced ${(syncState as SyncState.Success).count} accounts!"
                                        is SyncState.Error -> "Error: ${(syncState as SyncState.Error).message}"
                                    }

                                    Surface(
                                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                                        color = Color(0xFFEFF6FF),
                                        shape = MaterialTheme.shapes.medium
                                    ) {
                                        Text(
                                            text = statusText,
                                            modifier = Modifier.padding(12.dp),
                                            color = Color(0xFF1E40AF),
                                            fontSize = 13.sp
                                        )
                                    }

                                    Spacer(modifier = Modifier.height(10.dp))

                                    // Embedded WebView where portal loads and DOM scripts execute
                                    AndroidView(
                                        modifier = Modifier.fillMaxSize(),
                                        factory = { context ->
                                            WebView(context).apply {
                                                syncManager.attachWebView(this, agentId, password)
                                                loadUrl("https://dopagent.indiapost.gov.in")
                                            }
                                        }
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
