package com.gani.agentbuddy.ui.dashboard

import android.app.Application
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.gani.agentbuddy.data.local.AgentBuddyDatabase
import com.gani.agentbuddy.data.model.AccountSegment
import com.gani.agentbuddy.data.model.DashboardMetrics
import com.gani.agentbuddy.data.model.RDAccount
import com.gani.agentbuddy.sync.desktop.DesktopIdSyncClient
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.net.URLEncoder
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

class DashboardViewModel(application: Application) : AndroidViewModel(application) {

    private val dao = AgentBuddyDatabase.getDatabase(application).rdAccountDao()

    private val _searchQuery = MutableStateFlow("")
    val searchQuery: StateFlow<String> = _searchQuery

    private val _selectedSegment = MutableStateFlow(AccountSegment.UP_TO_DATE)
    val selectedSegment: StateFlow<AccountSegment> = _selectedSegment

    private val _syncMessage = MutableStateFlow<String?>("Synced & ready")
    val syncMessage: StateFlow<String?> = _syncMessage

    val allAccounts: StateFlow<List<RDAccount>> = dao.getActiveAccounts()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val metrics: StateFlow<DashboardMetrics> = allAccounts.combine(_searchQuery) { accounts, _ ->
        calculateMetrics(accounts)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), DashboardMetrics())

    val filteredAccounts: StateFlow<List<RDAccount>> = combine(allAccounts, _selectedSegment, _searchQuery) { accounts, segment, query ->
        val segmentFiltered = filterBySegment(accounts, segment)
        if (query.isBlank()) {
            segmentFiltered
        } else {
            val q = query.trim().lowercase(Locale.ROOT)
            segmentFiltered.filter {
                it.accountNo.contains(q, ignoreCase = true) ||
                it.accountName.contains(q, ignoreCase = true) ||
                (it.shortCode ?: "").contains(q, ignoreCase = true)
            }
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    fun onSearchQueryChanged(newQuery: String) {
        _searchQuery.value = newQuery
    }

    fun onSegmentSelected(segment: AccountSegment) {
        _selectedSegment.value = segment
    }

    fun deleteAccount(accountNo: String) {
        viewModelScope.launch {
            dao.deleteAccountByNumber(accountNo)
        }
    }

    fun syncDesktopIds(url: String, onResult: (String) -> Unit) {
        viewModelScope.launch {
            val result = DesktopIdSyncClient.syncIdsFromDesktopUrl(getApplication(), url)
            result.onSuccess { count ->
                val msg = "✓ Successfully matched $count account IDs with Desktop!"
                _syncMessage.value = msg
                onResult(msg)
            }.onFailure { err ->
                val msg = "Sync failed: ${err.message}"
                _syncMessage.value = msg
                onResult(msg)
            }
        }
    }

    fun sendWhatsAppReminder(context: Context, account: RDAccount) {
        try {
            val msg = "Dear ${account.accountName}, reminder for your Post Office RD Account #${account.accountNo}. Monthly installment of ₹${account.denomination} is due for ${account.nextInstallmentDate}. Thank you!"
            val encoded = URLEncoder.encode(msg, "UTF-8")
            val intent = Intent(Intent.ACTION_VIEW).apply {
                data = Uri.parse("https://api.whatsapp.com/send?text=$encoded")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
        } catch (e: Exception) {
            // WhatsApp not installed fallback
        }
    }

    private fun calculateMetrics(accounts: List<RDAccount>): DashboardMetrics {
        var totalAmount = 0L
        var firstHalfPendingCount = 0
        var firstHalfPendingAmount = 0L
        var firstHalfDepositedCount = 0
        var firstHalfDepositedAmount = 0L
        var secondHalfPendingCount = 0
        var secondHalfPendingAmount = 0L
        var secondHalfDepositedCount = 0
        var secondHalfDepositedAmount = 0L

        var upToDate = 0
        var due1 = 0
        var due2 = 0
        var due3Plus = 0
        var matured = 0
        var aboutToMature = 0
        var newAcc = 0

        val calMonthEnd = Calendar.getInstance().apply {
            set(Calendar.DAY_OF_MONTH, getActualMaximum(Calendar.DAY_OF_MONTH))
            set(Calendar.HOUR_OF_DAY, 23)
            set(Calendar.MINUTE, 59)
            set(Calendar.SECOND, 59)
            set(Calendar.MILLISECOND, 999)
        }
        val monthEndDate = calMonthEnd.time

        val cal30DaysAgo = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -30) }.time
        val cal60DaysAgo = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -60) }.time

        for (acc in accounts) {
            val denom = acc.amount.takeIf { it > 0 } ?: acc.denomination.toLongOrNull() ?: 0L
            totalAmount += denom

            val dueDay = extractDueDay(acc.nextInstallmentDate)
            val dueDate = parseDueDate(acc.nextInstallmentDate)
            val isDepositedThisMonth = dueDate != null && dueDate.after(monthEndDate)

            if (dueDay in 1..15) {
                if (isDepositedThisMonth) {
                    firstHalfDepositedCount++
                    firstHalfDepositedAmount += denom
                } else {
                    firstHalfPendingCount++
                    firstHalfPendingAmount += denom
                }
            } else {
                if (isDepositedThisMonth) {
                    secondHalfDepositedCount++
                    secondHalfDepositedAmount += denom
                } else {
                    secondHalfPendingCount++
                    secondHalfPendingAmount += denom
                }
            }

            // Segments calculation matching desktop
            val paidUptoNum = acc.monthPaidUptoNum
            if (paidUptoNum >= 60) {
                matured++
            } else if (paidUptoNum in 54..59) {
                aboutToMature++
            } else if (isDepositedThisMonth) {
                upToDate++
            } else if (dueDate != null && dueDate.after(cal30DaysAgo)) {
                due1++
            } else if (dueDate != null && dueDate.after(cal60DaysAgo)) {
                due2++
            } else {
                due3Plus++
            }
        }

        return DashboardMetrics(
            totalAccounts = accounts.size,
            totalAmount = totalAmount,
            firstHalfPendingCount = firstHalfPendingCount,
            firstHalfPendingAmount = firstHalfPendingAmount,
            firstHalfDepositedCount = firstHalfDepositedCount,
            firstHalfDepositedAmount = firstHalfDepositedAmount,
            secondHalfPendingCount = secondHalfPendingCount,
            secondHalfPendingAmount = secondHalfPendingAmount,
            secondHalfDepositedCount = secondHalfDepositedCount,
            secondHalfDepositedAmount = secondHalfDepositedAmount,
            upToDateCount = upToDate,
            oneMonthDueCount = due1,
            twoMonthsDueCount = due2,
            threePlusMonthsDueCount = due3Plus,
            maturedCount = matured,
            aboutToMatureCount = aboutToMature,
            newAccountsCount = newAcc
        )
    }

    private fun filterBySegment(accounts: List<RDAccount>, segment: AccountSegment): List<RDAccount> {
        val calMonthEnd = Calendar.getInstance().apply {
            set(Calendar.DAY_OF_MONTH, getActualMaximum(Calendar.DAY_OF_MONTH))
            set(Calendar.HOUR_OF_DAY, 23)
            set(Calendar.MINUTE, 59)
            set(Calendar.SECOND, 59)
            set(Calendar.MILLISECOND, 999)
        }
        val monthEndDate = calMonthEnd.time
        val cal30DaysAgo = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -30) }.time
        val cal60DaysAgo = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -60) }.time

        return when (segment) {
            AccountSegment.UP_TO_DATE -> accounts.filter {
                val due = parseDueDate(it.nextInstallmentDate)
                due != null && due.after(monthEndDate) && it.monthPaidUptoNum < 54
            }
            AccountSegment.DUE_1_MONTH -> accounts.filter {
                val due = parseDueDate(it.nextInstallmentDate)
                due != null && !due.after(monthEndDate) && due.after(cal30DaysAgo) && it.monthPaidUptoNum < 54
            }
            AccountSegment.DUE_2_MONTHS -> accounts.filter {
                val due = parseDueDate(it.nextInstallmentDate)
                due != null && !due.after(cal30DaysAgo) && due.after(cal60DaysAgo) && it.monthPaidUptoNum < 54
            }
            AccountSegment.DUE_3_PLUS -> accounts.filter {
                val due = parseDueDate(it.nextInstallmentDate)
                (due != null && !due.after(cal60DaysAgo) && it.monthPaidUptoNum < 54) || it.status.equals("defaulter", ignoreCase = true)
            }
            AccountSegment.MATURED -> accounts.filter { it.monthPaidUptoNum >= 60 }
            AccountSegment.ABOUT_TO_MATURE -> accounts.filter { it.monthPaidUptoNum in 54..59 }
            AccountSegment.CLOSED -> accounts.filter { it.isActive == 0 }
            AccountSegment.NEW_ACCOUNTS -> accounts.take(20)
        }
    }

    private fun extractDueDay(dateStr: String): Int {
        val parts = dateStr.split("-", "/")
        return parts.firstOrNull()?.toIntOrNull() ?: 10
    }

    private fun parseDueDate(dateStr: String): Date? {
        if (dateStr.isBlank()) return null
        val formats = listOf("dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMMM-yyyy", "d-MMMM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy")
        for (fmt in formats) {
            try {
                val sdf = SimpleDateFormat(fmt, Locale.ENGLISH)
                sdf.isLenient = false
                val d = sdf.parse(dateStr.trim())
                if (d != null) return d
            } catch (_: Exception) {}
        }
        return null
    }
}
