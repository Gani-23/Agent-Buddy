package com.gani.agentbuddy.ui.dashboard

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.automirrored.filled.Sort
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.gani.agentbuddy.data.model.AccountSegment
import com.gani.agentbuddy.data.model.DashboardMetrics
import com.gani.agentbuddy.data.model.DashboardSortOption
import com.gani.agentbuddy.data.model.RDAccount
import com.gani.agentbuddy.ui.sync.QrScannerModal
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
    val selectedSort by viewModel.selectedSort.collectAsState()
    val searchQuery by viewModel.searchQuery.collectAsState()
    val syncMessage by viewModel.syncMessage.collectAsState()

    var isViewingAccountList by remember { mutableStateOf(false) }
    var showQrScanner by remember { mutableStateOf(false) }
    var showSortMenu by remember { mutableStateOf(false) }
    var desktopUrlInput by remember { mutableStateOf("https://rdbase.onrender.com/accounts") }
    var selectedAccountForDetails by remember { mutableStateOf<RDAccount?>(null) }
    val currencyFormat = remember { NumberFormat.getCurrencyInstance(Locale("en", "IN")) }
    val searchFocusRequester = remember { FocusRequester() }

    // Intercept Back button so it NEVER crashes the app!
    BackHandler(enabled = selectedAccountForDetails != null) {
        selectedAccountForDetails = null
    }

    BackHandler(enabled = selectedAccountForDetails == null && isViewingAccountList) {
        isViewingAccountList = false
        if (searchQuery.isNotEmpty()) {
            viewModel.onSearchQueryChanged("")
        }
    }

    val filteredSumAmount = remember(accounts) {
        accounts.sumOf { it.numericAmount }
    }

    Scaffold(
        containerColor = Color(0xFFF8FAFC),
        topBar = {
            if (isViewingAccountList) {
                // TopBar for Filtered Account List View
                TopAppBar(
                    navigationIcon = {
                        IconButton(
                            onClick = {
                                isViewingAccountList = false
                                if (searchQuery.isNotEmpty()) {
                                    viewModel.onSearchQueryChanged("")
                                }
                            },
                            modifier = Modifier
                                .padding(start = 4.dp)
                                .size(44.dp)
                        ) {
                            Icon(
                                imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                                contentDescription = "Back to Overview",
                                tint = Color(0xFF0F172A),
                                modifier = Modifier.size(26.dp)
                            )
                        }
                    },
                    title = {
                        Column {
                            Text(
                                text = selectedSegment.displayName,
                                fontWeight = FontWeight.Bold,
                                fontSize = 18.sp,
                                color = Color(0xFF0F172A),
                                maxLines = 1
                            )
                            Text(
                                text = "${accounts.size} Accounts • ${currencyFormat.format(filteredSumAmount).replace(".00", "")}",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Medium,
                                color = Color(0xFF2563EB)
                            )
                        }
                    },
                    actions = {
                        // Sort Button
                        Box {
                            IconButton(
                                onClick = { showSortMenu = true },
                                modifier = Modifier
                                    .padding(end = 8.dp)
                                    .size(44.dp)
                                    .clip(CircleShape)
                                    .background(Color(0xFFF1F5F9))
                            ) {
                                Icon(
                                    imageVector = Icons.AutoMirrored.Filled.Sort,
                                    contentDescription = "Sort Options",
                                    tint = Color(0xFF1E293B),
                                    modifier = Modifier.size(22.dp)
                                )
                            }

                            DropdownMenu(
                                expanded = showSortMenu,
                                onDismissRequest = { showSortMenu = false }
                            ) {
                                DashboardSortOption.values().forEach { option ->
                                    DropdownMenuItem(
                                        text = {
                                            Text(
                                                text = option.displayName,
                                                fontSize = 15.sp,
                                                fontWeight = if (option == selectedSort) FontWeight.Bold else FontWeight.Normal,
                                                color = if (option == selectedSort) Color(0xFF2563EB) else Color.Unspecified
                                            )
                                        },
                                        onClick = {
                                            viewModel.onSortSelected(option)
                                            showSortMenu = false
                                        },
                                        leadingIcon = if (option == selectedSort) {
                                            { Icon(Icons.Default.Check, contentDescription = null, tint = Color(0xFF2563EB), modifier = Modifier.size(20.dp)) }
                                        } else null
                                    )
                                }
                            }
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.White)
                )
            } else {
                // TopBar for Overview Dashboard
                TopAppBar(
                    title = {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Box(
                                modifier = Modifier
                                    .size(38.dp)
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(Color(0xFF0F172A)),
                                contentAlignment = Alignment.Center
                            ) {
                                Icon(
                                    imageVector = Icons.Default.AccountBalance,
                                    contentDescription = null,
                                    tint = Color.White,
                                    modifier = Modifier.size(22.dp)
                                )
                            }
                            Column {
                                Text(
                                    "AgentBuddy",
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 20.sp,
                                    color = Color(0xFF0F172A)
                                )
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                                    Box(
                                        modifier = Modifier
                                            .size(7.dp)
                                            .clip(CircleShape)
                                            .background(Color(0xFF10B981))
                                    )
                                    Text(
                                        syncMessage ?: "Ready",
                                        fontSize = 13.sp,
                                        fontWeight = FontWeight.Medium,
                                        color = Color(0xFF475569)
                                    )
                                }
                            }
                        }
                    },
                    actions = {
                        IconButton(
                            onClick = onOpenPortalSync,
                            modifier = Modifier
                                .padding(end = 6.dp)
                                .size(44.dp)
                                .clip(CircleShape)
                                .background(Color(0xFFEFF6FF))
                        ) {
                            Icon(Icons.Default.CloudSync, contentDescription = "Sync Portal", tint = Color(0xFF2563EB), modifier = Modifier.size(22.dp))
                        }
                        IconButton(
                            onClick = { showQrScanner = true },
                            modifier = Modifier
                                .padding(end = 8.dp)
                                .size(44.dp)
                                .clip(CircleShape)
                                .background(Color(0xFFECFDF5))
                        ) {
                            Icon(Icons.Default.QrCodeScanner, contentDescription = "Sync Desktop IDs via QR", tint = Color(0xFF059669), modifier = Modifier.size(22.dp))
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.White)
                )
            }
        }
    ) { padding ->
        if (isViewingAccountList) {
            // ==========================================
            // DEDICATED ACCOUNT LIST VIEW (UPON CARD TAP)
            // ==========================================
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                // Search field inside filtered view
                item {
                    OutlinedTextField(
                        value = searchQuery,
                        onValueChange = { viewModel.onSearchQueryChanged(it) },
                        placeholder = { Text("Search by name, account no, ID...", fontSize = 15.sp, color = Color(0xFF94A3B8)) },
                        leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, tint = Color(0xFF475569), modifier = Modifier.size(22.dp)) },
                        trailingIcon = {
                            if (searchQuery.isNotEmpty()) {
                                IconButton(onClick = { viewModel.onSearchQueryChanged("") }) {
                                    Icon(Icons.Default.Clear, contentDescription = "Clear", tint = Color(0xFF475569), modifier = Modifier.size(22.dp))
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp, fontWeight = FontWeight.Medium, color = Color(0xFF0F172A)),
                        shape = RoundedCornerShape(16.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor = Color.White,
                            unfocusedContainerColor = Color.White,
                            focusedBorderColor = Color(0xFF2563EB),
                            unfocusedBorderColor = Color(0xFFCBD5E1)
                        ),
                        singleLine = true
                    )
                }

                // Quick Segment Switcher Pills
                item {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        val segmentPills = listOf(
                            AccountSegment.ALL_ACCOUNTS to metrics.totalAccounts,
                            AccountSegment.FIRST_HALF_DEPOSITED to metrics.firstHalfDepositedCount,
                            AccountSegment.FIRST_HALF_PENDING to metrics.firstHalfPendingCount,
                            AccountSegment.SECOND_HALF_DEPOSITED to metrics.secondHalfDepositedCount,
                            AccountSegment.SECOND_HALF_PENDING to metrics.secondHalfPendingCount,
                            AccountSegment.PENDING_THIS_MONTH to metrics.pendingThisMonthCount,
                            AccountSegment.VIP_ACCOUNTS to metrics.vipAccountsCount,
                            AccountSegment.REBATE_UPSELL to metrics.rebateUpsellCount,
                            AccountSegment.DEFAULTERS to metrics.defaulterCount,
                            AccountSegment.AT_RISK to metrics.atRiskCount,
                            AccountSegment.LOAN_ELIGIBLE to metrics.loanEligibleCount,
                            AccountSegment.PREMATURE_CLOSURE to metrics.prematureClosureCount,
                            AccountSegment.ABOUT_TO_MATURE to metrics.aboutToMatureCount,
                            AccountSegment.MATURED to metrics.maturedCount
                        )

                        items(segmentPills) { (seg, count) ->
                            val isSelected = seg == selectedSegment
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(20.dp))
                                    .background(if (isSelected) Color(0xFF0F172A) else Color.White)
                                    .border(1.5.dp, if (isSelected) Color(0xFF0F172A) else Color(0xFFCBD5E1), RoundedCornerShape(20.dp))
                                    .clickable { viewModel.onSegmentSelected(seg) }
                                    .padding(horizontal = 14.dp, vertical = 7.dp)
                            ) {
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                    Text(
                                        seg.displayName,
                                        fontSize = 13.sp,
                                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.SemiBold,
                                        color = if (isSelected) Color.White else Color(0xFF1E293B)
                                    )
                                    Box(
                                        modifier = Modifier
                                            .clip(RoundedCornerShape(8.dp))
                                            .background(if (isSelected) Color(0xFF334155) else Color(0xFFE2E8F0))
                                            .padding(horizontal = 6.dp, vertical = 2.dp)
                                    ) {
                                        Text(
                                            "$count",
                                            fontSize = 11.sp,
                                            fontWeight = FontWeight.Bold,
                                            color = if (isSelected) Color.White else Color(0xFF475569)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                // List Header with count
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            "Showing ${accounts.size} accounts",
                            fontSize = 15.sp,
                            fontWeight = FontWeight.Bold,
                            color = Color(0xFF475569)
                        )
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(10.dp))
                                .background(Color(0xFFEFF6FF))
                                .padding(horizontal = 12.dp, vertical = 5.dp)
                        ) {
                            Text(
                                currencyFormat.format(filteredSumAmount).replace(".00", ""),
                                fontSize = 14.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color(0xFF1D4ED8)
                            )
                        }
                    }
                }

                // Empty state if search has no matches
                if (accounts.isEmpty()) {
                    item {
                        Card(
                            shape = RoundedCornerShape(16.dp),
                            colors = CardDefaults.cardColors(containerColor = Color.White),
                            modifier = Modifier.fillMaxWidth().padding(vertical = 32.dp)
                        ) {
                            Column(
                                modifier = Modifier.fillMaxWidth().padding(24.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Icon(Icons.Default.SearchOff, contentDescription = null, tint = Color(0xFF94A3B8), modifier = Modifier.size(48.dp))
                                Text("No accounts found", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Color(0xFF0F172A))
                                Text("Try clearing the search query or select another category.", fontSize = 13.sp, color = Color(0xFF64748B))
                            }
                        }
                    }
                }

                // Account Cards
                items(accounts, key = { it.accountNo }) { account ->
                    ModernAccountCard(
                        account = account,
                        onCardClick = { selectedAccountForDetails = account },
                        onWhatsAppClick = { viewModel.sendWhatsAppReminder(context, account) }
                    )
                }
            }
        } else {
            // ==========================================
            // EXECUTIVE DASHBOARD OVERVIEW (CARD HUB)
            // ==========================================
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                // 1. HERO PORTFOLIO METRICS CARD (MODERN SLATE THEME)
                item {
                    Card(
                        shape = RoundedCornerShape(22.dp),
                        colors = CardDefaults.cardColors(containerColor = Color(0xFF0F172A)),
                        elevation = CardDefaults.cardElevation(defaultElevation = 3.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                viewModel.onSegmentSelected(AccountSegment.ALL_ACCOUNTS)
                                isViewingAccountList = true
                            }
                    ) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(22.dp),
                            verticalArrangement = Arrangement.spacedBy(18.dp)
                        ) {
                            // Title row
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column {
                                    Text(
                                        "PORTFOLIO VOLUME",
                                        fontSize = 13.sp,
                                        fontWeight = FontWeight.Bold,
                                        letterSpacing = 1.2.sp,
                                        color = Color(0xFF94A3B8)
                                    )
                                    Spacer(modifier = Modifier.height(4.dp))
                                    Text(
                                        currencyFormat.format(metrics.totalAmount).replace(".00", ""),
                                        fontSize = 32.sp,
                                        fontWeight = FontWeight.Black,
                                        color = Color.White
                                    )
                                    Spacer(modifier = Modifier.height(2.dp))
                                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                        Text(
                                            "${metrics.totalAccounts} Active Recurring Deposit Accounts",
                                            fontSize = 15.sp,
                                            fontWeight = FontWeight.Medium,
                                            color = Color(0xFFCBD5E1)
                                        )
                                        Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null, tint = Color(0xFF94A3B8), modifier = Modifier.size(16.dp))
                                    }
                                }
                            }

                            HorizontalDivider(color = Color(0xFF1E293B), thickness = 1.dp)

                            // 2 Highlights: Deposited goes to LEFT side, Pending goes to RIGHT side
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(12.dp)
                            ) {
                                // LEFT: Deposited This Month -> Tap to open list!
                                Box(
                                    modifier = Modifier
                                        .weight(1f)
                                        .clip(RoundedCornerShape(14.dp))
                                        .background(Color(0xFF1E293B))
                                        .clickable {
                                            viewModel.onSegmentSelected(AccountSegment.FIRST_HALF_DEPOSITED)
                                            isViewingAccountList = true
                                        }
                                        .padding(14.dp)
                                ) {
                                    Column {
                                        Row(
                                            modifier = Modifier.fillMaxWidth(),
                                            horizontalArrangement = Arrangement.SpaceBetween,
                                            verticalAlignment = Alignment.CenterVertically
                                        ) {
                                            Text("DEPOSITED", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color(0xFF34D399))
                                            Icon(Icons.Default.ChevronRight, contentDescription = null, tint = Color(0xFF34D399), modifier = Modifier.size(18.dp))
                                        }
                                        Spacer(modifier = Modifier.height(4.dp))
                                        Text("${metrics.depositedThisMonthCount} Accounts", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color.White)
                                        Spacer(modifier = Modifier.height(2.dp))
                                        Text(currencyFormat.format(metrics.depositedThisMonthAmount).replace(".00", ""), fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(0xFFCBD5E1))
                                    }
                                }

                                // RIGHT: Due / Pending This Month -> Tap to open list!
                                Box(
                                    modifier = Modifier
                                        .weight(1f)
                                        .clip(RoundedCornerShape(14.dp))
                                        .background(Color(0xFF1E293B))
                                        .clickable {
                                            viewModel.onSegmentSelected(AccountSegment.PENDING_THIS_MONTH)
                                            isViewingAccountList = true
                                        }
                                        .padding(14.dp)
                                ) {
                                    Column {
                                        Row(
                                            modifier = Modifier.fillMaxWidth(),
                                            horizontalArrangement = Arrangement.SpaceBetween,
                                            verticalAlignment = Alignment.CenterVertically
                                        ) {
                                            Text("PENDING", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color(0xFFFBBF24))
                                            Icon(Icons.Default.ChevronRight, contentDescription = null, tint = Color(0xFFFBBF24), modifier = Modifier.size(18.dp))
                                        }
                                        Spacer(modifier = Modifier.height(4.dp))
                                        Text("${metrics.pendingThisMonthCount} Accounts", fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color.White)
                                        Spacer(modifier = Modifier.height(2.dp))
                                        Text(currencyFormat.format(metrics.pendingThisMonthAmount).replace(".00", ""), fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(0xFFCBD5E1))
                                    }
                                }
                            }
                        }
                    }
                }

                // Quick Search Bar right on Overview
                item {
                    OutlinedTextField(
                        value = searchQuery,
                        onValueChange = {
                            viewModel.onSearchQueryChanged(it)
                            if (it.isNotEmpty()) {
                                viewModel.onSegmentSelected(AccountSegment.ALL_ACCOUNTS)
                                isViewingAccountList = true
                            }
                        },
                        placeholder = { Text("Search by name, account no, ID...", fontSize = 15.sp, color = Color(0xFF94A3B8)) },
                        leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, tint = Color(0xFF475569), modifier = Modifier.size(22.dp)) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                viewModel.onSegmentSelected(AccountSegment.ALL_ACCOUNTS)
                                isViewingAccountList = true
                            },
                        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 16.sp, fontWeight = FontWeight.Medium, color = Color(0xFF0F172A)),
                        shape = RoundedCornerShape(16.dp),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor = Color.White,
                            unfocusedContainerColor = Color.White,
                            focusedBorderColor = Color(0xFF2563EB),
                            unfocusedBorderColor = Color(0xFFCBD5E1)
                        ),
                        singleLine = true
                    )
                }

                // 2. COLLECTION BREAKDOWN TILES (DEPOSITED ON LEFT, PENDING ON RIGHT -> TAP OPENS FILTERED LIST)
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("Collection Deadlines", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Color(0xFF0F172A))
                        Text("Tap card to view list", fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(0xFF2563EB))
                    }
                    Spacer(modifier = Modifier.height(8.dp))

                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        // Row 1: 1st Half Deposited (LEFT) | 1st Half Due (RIGHT)
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            CollectionTile(
                                title = "1st Half Deposited",
                                count = "${metrics.firstHalfDepositedCount}",
                                amount = currencyFormat.format(metrics.firstHalfDepositedAmount).replace(".00", ""),
                                badgeColor = Color(0xFF047857),
                                badgeBg = Color(0xFFD1FAE5),
                                isSelected = false,
                                onClick = {
                                    viewModel.onSegmentSelected(AccountSegment.FIRST_HALF_DEPOSITED)
                                    isViewingAccountList = true
                                },
                                modifier = Modifier.weight(1f)
                            )
                            CollectionTile(
                                title = "1st Half Due (1-15)",
                                count = "${metrics.firstHalfPendingCount}",
                                amount = currencyFormat.format(metrics.firstHalfPendingAmount).replace(".00", ""),
                                badgeColor = Color(0xFFB45309),
                                badgeBg = Color(0xFFFEF3C7),
                                isSelected = false,
                                onClick = {
                                    viewModel.onSegmentSelected(AccountSegment.FIRST_HALF_PENDING)
                                    isViewingAccountList = true
                                },
                                modifier = Modifier.weight(1f)
                            )
                        }

                        // Row 2: 2nd Half Deposited (LEFT) | 2nd Half Due (RIGHT)
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            CollectionTile(
                                title = "2nd Half Deposited",
                                count = "${metrics.secondHalfDepositedCount}",
                                amount = currencyFormat.format(metrics.secondHalfDepositedAmount).replace(".00", ""),
                                badgeColor = Color(0xFF047857),
                                badgeBg = Color(0xFFD1FAE5),
                                isSelected = false,
                                onClick = {
                                    viewModel.onSegmentSelected(AccountSegment.SECOND_HALF_DEPOSITED)
                                    isViewingAccountList = true
                                },
                                modifier = Modifier.weight(1f)
                            )
                            CollectionTile(
                                title = "2nd Half Due (16+)",
                                count = "${metrics.secondHalfPendingCount}",
                                amount = currencyFormat.format(metrics.secondHalfPendingAmount).replace(".00", ""),
                                badgeColor = Color(0xFFB91C1C),
                                badgeBg = Color(0xFFFEE2E2),
                                isSelected = false,
                                onClick = {
                                    viewModel.onSegmentSelected(AccountSegment.SECOND_HALF_PENDING)
                                    isViewingAccountList = true
                                },
                                modifier = Modifier.weight(1f)
                            )
                        }
                    }
                }

                // 3. PORTFOLIO SEGMENTS CARDS (TAP CARD TO VIEW LIST)
                item {
                    Text("Portfolio Segments", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Color(0xFF0F172A))
                    Spacer(modifier = Modifier.height(4.dp))
                    Text("Select a category to view matching accounts", fontSize = 13.sp, color = Color(0xFF64748B))
                }

                // VIP High Value
                item {
                    SegmentOverviewCard(
                        title = "VIP High Value Accounts (≥ ₹5,000)",
                        count = metrics.vipAccountsCount,
                        amount = currencyFormat.format(metrics.vipAccountsAmount).replace(".00", ""),
                        tagColor = Color(0xFF4F46E5),
                        tagBg = Color(0xFFEEF2FF),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.VIP_ACCOUNTS)
                            isViewingAccountList = true
                        }
                    )
                }

                // Rebate Upsell
                item {
                    SegmentOverviewCard(
                        title = "Rebate Upsell Prospects (0 Overdue)",
                        count = metrics.rebateUpsellCount,
                        amount = currencyFormat.format(metrics.rebateUpsellAmount).replace(".00", ""),
                        tagColor = Color(0xFF047857),
                        tagBg = Color(0xFFD1FAE5),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.REBATE_UPSELL)
                            isViewingAccountList = true
                        }
                    )
                }

                // At Risk
                item {
                    SegmentOverviewCard(
                        title = "At Risk Accounts (1-3 Mo Overdue)",
                        count = metrics.atRiskCount,
                        amount = currencyFormat.format(metrics.atRiskAmount).replace(".00", ""),
                        tagColor = Color(0xFFB45309),
                        tagBg = Color(0xFFFEF3C7),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.AT_RISK)
                            isViewingAccountList = true
                        }
                    )
                }

                // Defaulters
                item {
                    SegmentOverviewCard(
                        title = "Defaulter Accounts (4+ Mo Overdue)",
                        count = metrics.defaulterCount,
                        amount = currencyFormat.format(metrics.defaulterAmount).replace(".00", ""),
                        tagColor = Color(0xFFDC2626),
                        tagBg = Color(0xFFFEE2E2),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.DEFAULTERS)
                            isViewingAccountList = true
                        }
                    )
                }

                // Loan Eligible
                item {
                    SegmentOverviewCard(
                        title = "Loan Eligible Accounts (12-59 Mo)",
                        count = metrics.loanEligibleCount,
                        amount = currencyFormat.format(metrics.loanEligibleAmount).replace(".00", ""),
                        tagColor = Color(0xFF2563EB),
                        tagBg = Color(0xFFEFF6FF),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.LOAN_ELIGIBLE)
                            isViewingAccountList = true
                        }
                    )
                }

                // Premature Closure
                item {
                    SegmentOverviewCard(
                        title = "Premature Closure Eligible (36-59 Mo)",
                        count = metrics.prematureClosureCount,
                        amount = currencyFormat.format(metrics.prematureClosureAmount).replace(".00", ""),
                        tagColor = Color(0xFF7C3AED),
                        tagBg = Color(0xFFF5F3FF),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.PREMATURE_CLOSURE)
                            isViewingAccountList = true
                        }
                    )
                }

                // Near Maturity
                item {
                    SegmentOverviewCard(
                        title = "Near Maturity Accounts (54-59 Mo)",
                        count = metrics.aboutToMatureCount,
                        amount = currencyFormat.format(metrics.aboutToMatureAmount).replace(".00", ""),
                        tagColor = Color(0xFFB45309),
                        tagBg = Color(0xFFFEF3C7),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.ABOUT_TO_MATURE)
                            isViewingAccountList = true
                        }
                    )
                }

                // Matured
                item {
                    SegmentOverviewCard(
                        title = "Matured Accounts (60+ Mo)",
                        count = metrics.maturedCount,
                        amount = currencyFormat.format(metrics.maturedAmount).replace(".00", ""),
                        tagColor = Color(0xFF047857),
                        tagBg = Color(0xFFD1FAE5),
                        onClick = {
                            viewModel.onSegmentSelected(AccountSegment.MATURED)
                            isViewingAccountList = true
                        }
                    )
                }
            }
        }
    }

    // Interactive Account Details Bottom Sheet Modal
    selectedAccountForDetails?.let { acc ->
        AccountDetailsBottomSheet(
            account = acc,
            onDismiss = { selectedAccountForDetails = null },
            onSendWhatsApp = {
                selectedAccountForDetails = null
                viewModel.sendWhatsAppReminder(context, acc)
            }
        )
    }

    if (showQrScanner) {
        QrScannerModal(
            onDismiss = { showQrScanner = false },
            onQrCodeDetected = { scannedUrl ->
                showQrScanner = false
                viewModel.syncDesktopIds(scannedUrl) { resultMsg ->
                    android.widget.Toast.makeText(context, resultMsg, android.widget.Toast.LENGTH_LONG).show()
                }
            }
        )
    }
}

@Composable
fun CollectionTile(
    title: String,
    count: String,
    amount: String,
    badgeColor: Color,
    badgeBg: Color,
    isSelected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val borderModifier = if (isSelected) {
        Modifier.border(2.dp, badgeColor, RoundedCornerShape(16.dp))
    } else {
        Modifier.border(1.5.dp, Color(0xFFE2E8F0), RoundedCornerShape(16.dp))
    }

    Card(
        modifier = modifier
            .then(borderModifier)
            .clickable { onClick() },
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(title, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = Color(0xFF475569))
                if (isSelected) {
                    Icon(Icons.Default.CheckCircle, contentDescription = null, tint = badgeColor, modifier = Modifier.size(16.dp))
                }
            }

            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(count, fontSize = 24.sp, fontWeight = FontWeight.Black, color = Color(0xFF0F172A))
                Text("accs", fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(0xFF64748B))
            }

            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .background(badgeBg)
                    .padding(horizontal = 8.dp, vertical = 3.dp)
            ) {
                Text(amount, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = badgeColor)
            }
        }
    }
}

@Composable
fun ModernAccountCard(
    account: RDAccount,
    onCardClick: () -> Unit,
    onWhatsAppClick: () -> Unit
) {
    val progress = (account.monthPaidUptoNum / 60f).coerceIn(0f, 1f)

    Card(
        shape = RoundedCornerShape(18.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        modifier = Modifier
            .fillMaxWidth()
            .border(1.5.dp, Color(0xFFE2E8F0), RoundedCornerShape(18.dp))
            .clickable { onCardClick() }
    ) {
        Column(
            modifier = Modifier.padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Row 1: Short code + Account Number (Monospace) + Status Badge
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (!account.shortCode.isNullOrEmpty()) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(8.dp))
                                .background(Color(0xFFEEF2FF))
                                .padding(horizontal = 8.dp, vertical = 3.dp)
                        ) {
                            Text(
                                "#${account.shortCode}",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color(0xFF4F46E5)
                            )
                        }
                    }
                    Text(
                        account.accountNo,
                        fontFamily = FontFamily.Monospace,
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp,
                        color = Color(0xFF334155)
                    )
                }

                // Status Pill
                val statusText: String
                val statusBg: Color
                val statusColor: Color

                if (account.monthPaidUptoNum >= 60) {
                    statusText = "Matured"
                    statusBg = Color(0xFFF5F3FF)
                    statusColor = Color(0xFF7C3AED)
                } else if (account.nextInstallmentDate.isNotBlank()) {
                    statusText = "Due ${account.nextInstallmentDate}"
                    statusBg = Color(0xFFFFFBEB)
                    statusColor = Color(0xFFB45309)
                } else {
                    statusText = "Active"
                    statusBg = Color(0xFFECFDF5)
                    statusColor = Color(0xFF059669)
                }

                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(10.dp))
                        .background(statusBg)
                        .padding(horizontal = 10.dp, vertical = 4.dp)
                ) {
                    Text(statusText, fontSize = 12.sp, fontWeight = FontWeight.Bold, color = statusColor)
                }
            }

            // Row 2: Customer Name (Large High Contrast Bold Display for 50+ users)
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    account.accountName,
                    fontWeight = FontWeight.Bold,
                    fontSize = 19.sp,
                    color = Color(0xFF0F172A),
                    modifier = Modifier.weight(1f)
                )
                Icon(
                    imageVector = Icons.Default.ChevronRight,
                    contentDescription = "View Details",
                    tint = Color(0xFF94A3B8),
                    modifier = Modifier.size(22.dp)
                )
            }

            // Row 3: Denomination + Progress Bar + WhatsApp Button
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text("MONTHLY", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = Color(0xFF64748B))
                    Spacer(modifier = Modifier.height(2.dp))
                    Text(account.cleanDenomination, fontSize = 18.sp, fontWeight = FontWeight.Black, color = Color(0xFF1D4ED8))
                }

                Column(modifier = Modifier.width(115.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Text("PAID", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = Color(0xFF64748B))
                        Text("${account.monthPaidUptoNum}/60", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = Color(0xFF334155))
                    }
                    Spacer(modifier = Modifier.height(4.dp))
                    LinearProgressIndicator(
                        progress = { progress },
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(7.dp)
                            .clip(RoundedCornerShape(3.5.dp)),
                        color = Color(0xFF2563EB),
                        trackColor = Color(0xFFE2E8F0)
                    )
                }

                // Generous Accessible WhatsApp Button
                Button(
                    onClick = onWhatsAppClick,
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366)),
                    contentPadding = PaddingValues(horizontal = 14.dp, vertical = 8.dp),
                    modifier = Modifier.height(44.dp)
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.Send,
                        contentDescription = null,
                        tint = Color.White,
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(5.dp))
                    Text("WhatsApp", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = Color.White)
                }
            }
        }
    }
}

@Composable
fun SegmentOverviewCard(
    title: String,
    count: Int,
    amount: String,
    tagColor: Color,
    tagBg: Color,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    Card(
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = Color.White),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        border = androidx.compose.foundation.BorderStroke(1.5.dp, Color(0xFFE2E8F0)),
        modifier = modifier
            .fillMaxWidth()
            .clickable { onClick() }
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 14.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(0xFF0F172A)
                )
                Spacer(modifier = Modifier.height(4.dp))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        text = "$count accounts",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Medium,
                        color = Color(0xFF64748B)
                    )
                    Text("•", fontSize = 14.sp, color = Color(0xFFCBD5E1))
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(6.dp))
                            .background(tagBg)
                            .padding(horizontal = 7.dp, vertical = 2.dp)
                    ) {
                        Text(
                            text = amount,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            color = tagColor
                        )
                    }
                }
            }
            Icon(
                imageVector = Icons.Default.ChevronRight,
                contentDescription = "View list",
                tint = Color(0xFF94A3B8),
                modifier = Modifier.size(24.dp)
            )
        }
    }
}
