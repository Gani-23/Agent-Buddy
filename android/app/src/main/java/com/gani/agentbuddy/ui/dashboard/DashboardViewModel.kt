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
import com.gani.agentbuddy.data.model.DashboardSortOption
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

    private val _selectedSegment = MutableStateFlow(AccountSegment.ALL_ACCOUNTS)
    val selectedSegment: StateFlow<AccountSegment> = _selectedSegment

    private val _selectedSort = MutableStateFlow(DashboardSortOption.DUE_DATE_ASC)
    val selectedSort: StateFlow<DashboardSortOption> = _selectedSort

    private val _syncMessage = MutableStateFlow<String?>("Synced & ready")
    val syncMessage: StateFlow<String?> = _syncMessage

    val allAccounts: StateFlow<List<RDAccount>> = dao.getActiveAccounts()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val metrics: StateFlow<DashboardMetrics> = allAccounts.combine(_searchQuery) { accounts, _ ->
        calculateMetrics(accounts)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), DashboardMetrics())

    val filteredAccounts: StateFlow<List<RDAccount>> = combine(allAccounts, _selectedSegment, _searchQuery, _selectedSort) { accounts, segment, query, sort ->
        val segmentFiltered = filterBySegment(accounts, segment)
        val queryFiltered = if (query.isBlank()) {
            segmentFiltered
        } else {
            val q = query.trim().lowercase(Locale.ROOT)
            segmentFiltered.filter {
                it.accountNo.contains(q, ignoreCase = true) ||
                it.accountName.contains(q, ignoreCase = true) ||
                (it.shortCode ?: "").contains(q, ignoreCase = true)
            }
        }
        sortAccounts(queryFiltered, sort)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    fun onSearchQueryChanged(newQuery: String) {
        _searchQuery.value = newQuery
    }

    fun onSegmentSelected(segment: AccountSegment) {
        _selectedSegment.value = segment
    }

    fun onSortSelected(sort: DashboardSortOption) {
        _selectedSort.value = sort
    }

    fun deleteAccount(accountNo: String) {
        viewModelScope.launch {
            dao.deleteAccountByNumber(accountNo)
        }
    }

    fun syncDesktopIds(url: String, onResult: (String) -> Unit) {
        viewModelScope.launch {
            val result = DesktopIdSyncClient.syncIdsFromDesktopUrl(getApplication(), url)
            result.onSuccess { msg ->
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
            val msg = "Dear ${account.accountName}, reminder for your Post Office RD Account #${account.accountNo}. Monthly installment of ${account.cleanDenomination} is due on ${account.nextInstallmentDate}. Thank you!"
            val encoded = URLEncoder.encode(msg, "UTF-8")
            val intent = Intent(Intent.ACTION_VIEW).apply {
                data = Uri.parse("https://api.whatsapp.com/send?text=$encoded")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
        } catch (e: Exception) {
            // WhatsApp not installed
        }
    }

    private fun calculateMetrics(accounts: List<RDAccount>): DashboardMetrics {
        var totalAmount = 0L

        var pendingThisMonthCount = 0
        var pendingThisMonthAmount = 0L
        var depositedThisMonthCount = 0
        var depositedThisMonthAmount = 0L

        var firstHalfPendingCount = 0
        var firstHalfPendingAmount = 0L
        var firstHalfDepositedCount = 0
        var firstHalfDepositedAmount = 0L

        var secondHalfPendingCount = 0
        var secondHalfPendingAmount = 0L
        var secondHalfDepositedCount = 0
        var secondHalfDepositedAmount = 0L

        var rebateUpsellCount = 0
        var rebateUpsellAmount = 0L

        var defaulterCount = 0
        var defaulterAmount = 0L

        var atRiskCount = 0
        var atRiskAmount = 0L

        var vipCount = 0
        var vipAmount = 0L

        var loanCount = 0
        var loanAmount = 0L

        var prematureCount = 0
        var prematureAmount = 0L

        var nearMatureCount = 0
        var nearMatureAmount = 0L

        var maturedCount = 0
        var maturedAmount = 0L

        val today = Calendar.getInstance().time
        val monthEndDate = getMonthEndDate()

        for (acc in accounts) {
            val denom = acc.numericAmount
            totalAmount += denom

            val dueDate = parseDueDate(acc.nextDueDateIso.ifEmpty { acc.nextInstallmentDate })

            // India Post Half Cycle rule:
            // Accounts without due date are already matured (60+ installments)
            if (dueDate != null) {
                val isDeposited = dueDate.after(monthEndDate)
                val isFirstHalf = extractDueDay(dueDate) <= 15

                if (!isDeposited) {
                    pendingThisMonthCount++
                    pendingThisMonthAmount += denom

                    if (isFirstHalf) {
                        firstHalfPendingCount++
                        firstHalfPendingAmount += denom
                    } else {
                        secondHalfPendingCount++
                        secondHalfPendingAmount += denom
                    }
                } else {
                    depositedThisMonthCount++
                    depositedThisMonthAmount += denom

                    if (isFirstHalf) {
                        firstHalfDepositedCount++
                        firstHalfDepositedAmount += denom
                    } else {
                        secondHalfDepositedCount++
                        secondHalfDepositedAmount += denom
                    }
                }

                val overdueMonths = calculateOverdueMonths(dueDate, today)
                if (overdueMonths == 0) {
                    rebateUpsellCount++
                    rebateUpsellAmount += denom
                } else if (overdueMonths in 1..3) {
                    atRiskCount++
                    atRiskAmount += denom
                } else if (overdueMonths >= 4) {
                    defaulterCount++
                    defaulterAmount += denom
                }
            } else {
                // No due date on portal -> Completely on-time or matured
                rebateUpsellCount++
                rebateUpsellAmount += denom
            }

            // Installment-based insights
            val paidNum = acc.monthPaidUptoNum
            if (paidNum >= 60) {
                maturedCount++
                maturedAmount += denom
            } else {
                if (paidNum in 54..59) {
                    nearMatureCount++
                    nearMatureAmount += denom
                }
                if (paidNum in 12..59) {
                    loanCount++
                    loanAmount += denom
                }
                if (paidNum in 36..59) {
                    prematureCount++
                    prematureAmount += denom
                }
            }

            // High Value / VIP (>= Rs. 5000)
            if (denom >= 5000L) {
                vipCount++
                vipAmount += denom
            }
        }

        return DashboardMetrics(
            totalAccounts = accounts.size,
            totalAmount = totalAmount,
            pendingThisMonthCount = pendingThisMonthCount,
            pendingThisMonthAmount = pendingThisMonthAmount,
            depositedThisMonthCount = depositedThisMonthCount,
            depositedThisMonthAmount = depositedThisMonthAmount,
            firstHalfPendingCount = firstHalfPendingCount,
            firstHalfPendingAmount = firstHalfPendingAmount,
            firstHalfDepositedCount = firstHalfDepositedCount,
            firstHalfDepositedAmount = firstHalfDepositedAmount,
            secondHalfPendingCount = secondHalfPendingCount,
            secondHalfPendingAmount = secondHalfPendingAmount,
            secondHalfDepositedCount = secondHalfDepositedCount,
            secondHalfDepositedAmount = secondHalfDepositedAmount,
            rebateUpsellCount = rebateUpsellCount,
            rebateUpsellAmount = rebateUpsellAmount,
            defaulterCount = defaulterCount,
            defaulterAmount = defaulterAmount,
            atRiskCount = atRiskCount,
            atRiskAmount = atRiskAmount,
            vipAccountsCount = vipCount,
            vipAccountsAmount = vipAmount,
            loanEligibleCount = loanCount,
            loanEligibleAmount = loanAmount,
            prematureClosureCount = prematureCount,
            prematureClosureAmount = prematureAmount,
            aboutToMatureCount = nearMatureCount,
            aboutToMatureAmount = nearMatureAmount,
            maturedCount = maturedCount,
            maturedAmount = maturedAmount
        )
    }

    private fun filterBySegment(accounts: List<RDAccount>, segment: AccountSegment): List<RDAccount> {
        val today = Calendar.getInstance().time
        val monthEndDate = getMonthEndDate()

        return when (segment) {
            AccountSegment.ALL_ACCOUNTS -> accounts.filter { it.isActive == 1 }

            AccountSegment.PENDING_THIS_MONTH -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && !due.after(monthEndDate)
            }

            AccountSegment.FIRST_HALF_PENDING -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && !due.after(monthEndDate) && extractDueDay(due) <= 15
            }

            AccountSegment.SECOND_HALF_PENDING -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && !due.after(monthEndDate) && extractDueDay(due) > 15
            }

            AccountSegment.FIRST_HALF_DEPOSITED -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && due.after(monthEndDate) && extractDueDay(due) <= 15
            }

            AccountSegment.SECOND_HALF_DEPOSITED -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && due.after(monthEndDate) && extractDueDay(due) > 15
            }

            AccountSegment.REBATE_UPSELL -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due == null || calculateOverdueMonths(due, today) == 0
            }

            AccountSegment.DEFAULTERS -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && calculateOverdueMonths(due, today) >= 4
            }

            AccountSegment.AT_RISK -> accounts.filter {
                val due = parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate })
                due != null && calculateOverdueMonths(due, today) in 1..3
            }

            AccountSegment.VIP_ACCOUNTS -> accounts.filter { it.numericAmount >= 5000L }

            AccountSegment.LOAN_ELIGIBLE -> accounts.filter { it.monthPaidUptoNum in 12..59 }

            AccountSegment.PREMATURE_CLOSURE -> accounts.filter { it.monthPaidUptoNum in 36..59 }

            AccountSegment.ABOUT_TO_MATURE -> accounts.filter { it.monthPaidUptoNum in 54..59 }

            AccountSegment.MATURED -> accounts.filter { it.monthPaidUptoNum >= 60 }

            AccountSegment.CLOSED -> accounts.filter { it.isActive == 0 }
        }
    }

    private fun sortAccounts(list: List<RDAccount>, sort: DashboardSortOption): List<RDAccount> {
        return when (sort) {
            DashboardSortOption.DUE_DATE_ASC -> list.sortedWith(compareBy(nullsLast()) { parseDueDate(it.nextDueDateIso.ifEmpty { it.nextInstallmentDate }) })
            DashboardSortOption.AMOUNT_DESC -> list.sortedByDescending { it.numericAmount }
            DashboardSortOption.NAME_ASC -> list.sortedBy { it.accountName.lowercase(Locale.ROOT) }
            DashboardSortOption.MATURITY_DESC -> list.sortedByDescending { it.monthPaidUptoNum }
        }
    }

    private fun calculateOverdueMonths(dueDate: Date, asOfDate: Date): Int {
        if (asOfDate.before(dueDate) || asOfDate == dueDate) return 0

        var overdueCount = 0
        val cal = Calendar.getInstance().apply { time = dueDate }
        val asOfCal = Calendar.getInstance().apply { time = asOfDate }

        val isFirstHalf = cal.get(Calendar.DAY_OF_MONTH) <= 15
        var checkYear = cal.get(Calendar.YEAR)
        var checkMonth = cal.get(Calendar.MONTH)

        while (true) {
            val cycleCal = Calendar.getInstance().apply {
                set(Calendar.YEAR, checkYear)
                set(Calendar.MONTH, checkMonth)
                val maxDay = getActualMaximum(Calendar.DAY_OF_MONTH)
                set(Calendar.DAY_OF_MONTH, if (isFirstHalf) 15 else maxDay)
                set(Calendar.HOUR_OF_DAY, 23)
                set(Calendar.MINUTE, 59)
                set(Calendar.SECOND, 59)
            }

            if (asOfCal.after(cycleCal)) {
                overdueCount++
                checkMonth++
                if (checkMonth > 11) {
                    checkMonth = 0
                    checkYear++
                }
                if (overdueCount >= 120) break
            } else {
                break
            }
        }

        return overdueCount
    }

    private fun getMonthEndDate(): Date {
        return Calendar.getInstance().apply {
            set(Calendar.DAY_OF_MONTH, getActualMaximum(Calendar.DAY_OF_MONTH))
            set(Calendar.HOUR_OF_DAY, 23)
            set(Calendar.MINUTE, 59)
            set(Calendar.SECOND, 59)
            set(Calendar.MILLISECOND, 999)
        }.time
    }

    private fun extractDueDay(date: Date): Int {
        val cal = Calendar.getInstance().apply { time = date }
        return cal.get(Calendar.DAY_OF_MONTH)
    }

    fun parseDueDate(dateStr: String): Date? {
        if (dateStr.isBlank()) return null
        val formats = listOf("yyyy-MM-dd", "dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMMM-yyyy", "d-MMMM-yyyy", "dd/MM/yyyy", "d/M/yyyy")
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
