package com.gani.agentbuddy.data.model

data class DashboardMetrics(
    val totalAccounts: Int = 0,
    val totalAmount: Long = 0,

    // Pending / Deposited this month
    val pendingThisMonthCount: Int = 0,
    val pendingThisMonthAmount: Long = 0,
    val depositedThisMonthCount: Int = 0,
    val depositedThisMonthAmount: Long = 0,

    // 1st Half (1-15)
    val firstHalfPendingCount: Int = 0,
    val firstHalfPendingAmount: Long = 0,
    val firstHalfDepositedCount: Int = 0,
    val firstHalfDepositedAmount: Long = 0,

    // 2nd Half (16+)
    val secondHalfPendingCount: Int = 0,
    val secondHalfPendingAmount: Long = 0,
    val secondHalfDepositedCount: Int = 0,
    val secondHalfDepositedAmount: Long = 0,

    // Agent Insights
    val rebateUpsellCount: Int = 0,
    val rebateUpsellAmount: Long = 0,

    val defaulterCount: Int = 0,
    val defaulterAmount: Long = 0,

    val atRiskCount: Int = 0,
    val atRiskAmount: Long = 0,

    val vipAccountsCount: Int = 0,
    val vipAccountsAmount: Long = 0,

    val loanEligibleCount: Int = 0,
    val loanEligibleAmount: Long = 0,

    val prematureClosureCount: Int = 0,
    val prematureClosureAmount: Long = 0,

    val aboutToMatureCount: Int = 0,
    val aboutToMatureAmount: Long = 0,

    val maturedCount: Int = 0,
    val maturedAmount: Long = 0,

    val closedAccountsCount: Int = 0,
    val closedAccountsAmount: Long = 0
)

enum class AccountSegment(val displayName: String) {
    ALL_ACCOUNTS("All Active Accounts"),
    PENDING_THIS_MONTH("Due This Month"),
    FIRST_HALF_PENDING("1st Half Pending (1-15)"),
    SECOND_HALF_PENDING("2nd Half Pending (16+)"),
    FIRST_HALF_DEPOSITED("1st Half Deposited"),
    SECOND_HALF_DEPOSITED("2nd Half Deposited"),
    REBATE_UPSELL("Rebate Upsell Prospects"),
    DEFAULTERS("Defaulters (4+ Mo Overdue)"),
    AT_RISK("At Risk (1-3 Mo Overdue)"),
    VIP_ACCOUNTS("VIP Accounts (≥ ₹5,000)"),
    LOAN_ELIGIBLE("Loan Eligible (12-59 Mo)"),
    PREMATURE_CLOSURE("Premature Closure (36-59 Mo)"),
    ABOUT_TO_MATURE("Near Maturity (54-59 Mo)"),
    MATURED("Matured Accounts (60+)"),
    CLOSED("Closed Accounts")
}

enum class DashboardSortOption(val displayName: String) {
    DUE_DATE_ASC("Next Due (Earliest)"),
    AMOUNT_DESC("Denomination (High to Low)"),
    NAME_ASC("Name (A to Z)"),
    MATURITY_DESC("Progress (Near Maturity)")
}
