package com.gani.agentbuddy.ui.dashboard

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.gani.agentbuddy.data.model.AccountSegment
import com.gani.agentbuddy.data.model.DashboardMetrics
import com.gani.agentbuddy.data.model.RDAccount
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DashboardScreen(
    viewModel: DashboardViewModel,
    onOpenPortalSync: () -> Unit,
    onOpenDesktopSync: () -> Unit
) {
    val context = LocalContext.current
    val metrics by viewModel.metrics.collectAsState()
    val accounts by viewModel.filteredAccounts.collectAsState()
    val selectedSegment by viewModel.selectedSegment.collectAsState()
    val searchQuery by viewModel.searchQuery.collectAsState()
    val syncMessage by viewModel.syncMessage.collectAsState()

    var showSyncDialog by remember { mutableStateOf(false) }
    var desktopUrlInput by remember { mutableStateOf("http://192.168.1.") }
    val currencyFormat = remember { NumberFormat.getCurrencyInstance(Locale("en", "IN")) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("AgentBuddy", fontWeight = FontWeight.Bold, fontSize = 20.sp)
                        Text(
                            syncMessage ?: "Ready",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                },
                actions = {
                    IconButton(onClick = onOpenPortalSync) {
                        Icon(Icons.Default.CloudSync, contentDescription = "Sync Portal", tint = Color(0xFF2563EB))
                    }
                    IconButton(onClick = { showSyncDialog = true }) {
                        Icon(Icons.Default.QrCodeScanner, contentDescription = "Sync Desktop IDs", tint = Color(0xFF10B981))
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(Color(0xFFF8F9FA)),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // 1. TOP METRICS
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    MetricCard(
                        title = "ACTIVE ACCOUNTS",
                        value = "${metrics.totalAccounts}",
                        subtitle = "Total Depositors",
                        color = Color(0xFF2563EB),
                        modifier = Modifier.weight(1f)
                    )
                    MetricCard(
                        title = "MONTHLY VOLUME",
                        value = currencyFormat.format(metrics.totalAmount).replace(".00", ""),
                        subtitle = "Monthly Denomination",
                        color = Color(0xFF10B981),
                        modifier = Modifier.weight(1f)
                    )
                }
            }

            // 2. 1ST HALF & 2ND HALF COLLECTION MATRIX
            item {
                Text("Collection Breakdown", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                Spacer(modifier = Modifier.height(6.dp))
                CollectionMatrix(metrics, currencyFormat)
            }

            // 3. SEGMENT FILTER CHIPS
            item {
                Text("Filter Accounts", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                Spacer(modifier = Modifier.height(6.dp))
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(AccountSegment.values()) { segment ->
                        FilterChip(
                            selected = segment == selectedSegment,
                            onClick = { viewModel.onSegmentSelected(segment) },
                            label = { Text(segment.displayName, fontSize = 12.sp) }
                        )
                    }
                }
            }

            // 4. SEARCH BAR
            item {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { viewModel.onSearchQueryChanged(it) },
                    placeholder = { Text("Search by account no, name or ID...") },
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                    trailingIcon = {
                        if (searchQuery.isNotEmpty()) {
                            IconButton(onClick = { viewModel.onSearchQueryChanged("") }) {
                                Icon(Icons.Default.Clear, contentDescription = null)
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(12.dp),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedContainerColor = Color.White,
                        unfocusedContainerColor = Color.White
                    ),
                    singleLine = true
                )
            }

            // 5. ACCOUNTS COUNT HEADER
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        "${selectedSegment.displayName} (${accounts.size})",
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 13.sp,
                        color = Color(0xFF6B7280)
                    )
                }
            }

            // 6. ACCOUNT CARDS LIST
            items(accounts, key = { it.accountNo }) { account ->
                AccountCard(
                    account = account,
                    isClosedSegment = selectedSegment == AccountSegment.CLOSED,
                    onWhatsAppClick = { viewModel.sendWhatsAppReminder(context, account) },
                    onDeleteClick = { viewModel.deleteAccount(account.accountNo) }
                )
            }
        }
    }

    if (showSyncDialog) {
        AlertDialog(
            onDismissRequest = { showSyncDialog = false },
            title = { Text("Sync IDs with Desktop") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Enter Desktop Wi-Fi Sync URL (from desktop popup):", fontSize = 13.sp)
                    OutlinedTextField(
                        value = desktopUrlInput,
                        onValueChange = { desktopUrlInput = it },
                        modifier = Modifier.fillMaxWidth(),
                        singleLine = true
                    )
                }
            },
            confirmButton = {
                Button(onClick = {
                    showSyncDialog = false
                    viewModel.syncDesktopIds(desktopUrlInput) { }
                }) {
                    Text("Sync IDs")
                }
            },
            dismissButton = {
                TextButton(onClick = { showSyncDialog = false }) {
                    Text("Cancel")
                }
            }
        )
    }
}

@Composable
fun MetricCard(
    title: String,
    value: String,
    subtitle: String,
    color: Color,
    modifier: Modifier = Modifier
) {
    Card(
        modifier = modifier,
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Text(title, fontSize = 10.sp, fontWeight = FontWeight.Bold, color = color)
            Spacer(modifier = Modifier.height(4.dp))
            Text(value, fontSize = 20.sp, fontWeight = FontWeight.Black)
            Spacer(modifier = Modifier.height(2.dp))
            Text(subtitle, fontSize = 10.sp, color = Color(0xFF6B7280))
        }
    }
}

@Composable
fun CollectionMatrix(metrics: DashboardMetrics, currency: NumberFormat) {
    Card(
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                MatrixItem(
                    title = "1st Half Pending (1-15)",
                    count = metrics.firstHalfPendingCount,
                    amount = currency.format(metrics.firstHalfPendingAmount).replace(".00", ""),
                    bg = Color(0xFFFEF3C7),
                    textColor = Color(0xFFB45309),
                    modifier = Modifier.weight(1f)
                )
                MatrixItem(
                    title = "1st Half Deposited",
                    count = metrics.firstHalfDepositedCount,
                    amount = currency.format(metrics.firstHalfDepositedAmount).replace(".00", ""),
                    bg = Color(0xFFD1FAE5),
                    textColor = Color(0xFF065F46),
                    modifier = Modifier.weight(1f)
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                MatrixItem(
                    title = "2nd Half Pending (16+)",
                    count = metrics.secondHalfPendingCount,
                    amount = currency.format(metrics.secondHalfPendingAmount).replace(".00", ""),
                    bg = Color(0xFFFEE2E2),
                    textColor = Color(0xFF991B1B),
                    modifier = Modifier.weight(1f)
                )
                MatrixItem(
                    title = "2nd Half Deposited",
                    count = metrics.secondHalfDepositedCount,
                    amount = currency.format(metrics.secondHalfDepositedAmount).replace(".00", ""),
                    bg = Color(0xFFD1FAE5),
                    textColor = Color(0xFF065F46),
                    modifier = Modifier.weight(1f)
                )
            }
        }
    }
}

@Composable
fun MatrixItem(
    title: String,
    count: Int,
    amount: String,
    bg: Color,
    textColor: Color,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(10.dp))
            .background(bg)
            .padding(10.dp)
    ) {
        Column {
            Text(title, fontSize = 10.sp, fontWeight = FontWeight.Bold, color = textColor)
            Spacer(modifier = Modifier.height(2.dp))
            Text("$count Accounts", fontSize = 14.sp, fontWeight = FontWeight.Black, color = textColor)
            Text(amount, fontSize = 12.sp, fontWeight = FontWeight.Bold, color = textColor)
        }
    }
}

@Composable
fun AccountCard(
    account: RDAccount,
    isClosedSegment: Boolean,
    onWhatsAppClick: () -> Unit,
    onDeleteClick: () -> Unit
) {
    Card(
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (!account.shortCode.isNullOrEmpty()) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(6.dp))
                                .background(Color(0xFFE5E7EB))
                                .padding(horizontal = 6.dp, vertical = 2.dp)
                        ) {
                            Text(account.shortCode, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                    Text(
                        account.accountNo,
                        fontFamily = FontFamily.Monospace,
                        fontWeight = FontWeight.Bold,
                        fontSize = 14.sp
                    )
                }

                if (isClosedSegment) {
                    IconButton(
                        onClick = onDeleteClick,
                        modifier = Modifier
                            .size(34.dp)
                            .background(Color(0xFFEF4444), RoundedCornerShape(8.dp))
                    ) {
                        Icon(
                            Icons.Default.Delete,
                            contentDescription = "Delete",
                            tint = Color.White,
                            modifier = Modifier.size(16.dp)
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(4.dp))
            Text(account.accountName, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)

            Spacer(modifier = Modifier.height(8.dp))
            Divider(color = Color(0xFFF3F4F6))
            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column {
                    Text("Monthly", fontSize = 10.sp, color = Color(0xFF6B7280))
                    Text("₹${account.denomination}", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = Color(0xFF2563EB))
                }
                Column {
                    Text("Paid Upto", fontSize = 10.sp, color = Color(0xFF6B7280))
                    Text(account.monthPaidUpto.ifEmpty { "-" }, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
                }
                Column {
                    Text("Next Due", fontSize = 10.sp, color = Color(0xFF6B7280))
                    Text(account.nextInstallmentDate.ifEmpty { "-" }, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = Color(0xFFD97706))
                }
            }

            Spacer(modifier = Modifier.height(10.dp))
            Button(
                onClick = onWhatsAppClick,
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366)),
                shape = RoundedCornerShape(8.dp),
                modifier = Modifier.fillMaxWidth().height(36.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Icon(Icons.Default.Send, contentDescription = null, tint = Color.White, modifier = Modifier.size(14.dp))
                Spacer(modifier = Modifier.width(6.dp))
                Text("Send WhatsApp Reminder", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color.White)
            }
        }
    }
}
