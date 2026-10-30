package com.gani.agentbuddy.data.model

data class DashboardMetrics(
    val totalAccounts: Int = 0,
    val totalAmount: Long = 0,

    val firstHalfPendingCount: Int = 0,
    val firstHalfPendingAmount: Long = 0,

    val firstHalfDepositedCount: Int = 0,
    val firstHalfDepositedAmount: Long = 0,

    val secondHalfPendingCount: Int = 0,
    val secondHalfPendingAmount: Long = 0,

    val secondHalfDepositedCount: Int = 0,
    val secondHalfDepositedAmount: Long = 0,

    val upToDateCount: Int = 0,
    val oneMonthDueCount: Int = 0,
    val twoMonthsDueCount: Int = 0,
    val threePlusMonthsDueCount: Int = 0,
    val maturedCount: Int = 0,
    val aboutToMatureCount: Int = 0,
    val closedAccountsCount: Int = 0,
    val newAccountsCount: Int = 0
)

enum class AccountSegment(val displayName: String) {
    UP_TO_DATE("Up to Date"),
    DUE_1_MONTH("1 Month Due"),
    DUE_2_MONTHS("2 Months Due"),
    DUE_3_PLUS("3+ Months Due"),
    MATURED("Matured"),
    ABOUT_TO_MATURE("About to Mature"),
    CLOSED("Closed"),
    NEW_ACCOUNTS("New Accounts")
}
