package com.gani.agentbuddy.data.model

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(
    tableName = "rd_accounts",
    indices = [
        Index(value = ["account_no"], unique = true),
        Index(value = ["short_code"])
    ]
)
data class RDAccount(
    @PrimaryKey(autoGenerate = true)
    val id: Long = 0,

    @ColumnInfo(name = "account_no")
    val accountNo: String,

    @ColumnInfo(name = "account_name")
    val accountName: String = "",

    @ColumnInfo(name = "aslaas_no")
    val aslaasNo: String = "",

    @ColumnInfo(name = "denomination")
    val denomination: String = "0",

    @ColumnInfo(name = "month_paid_upto")
    val monthPaidUpto: String = "",

    @ColumnInfo(name = "next_installment_date")
    val nextInstallmentDate: String = "",

    @ColumnInfo(name = "amount")
    val amount: Long = 0,

    @ColumnInfo(name = "month_paid_upto_num")
    val monthPaidUptoNum: Int = 0,

    @ColumnInfo(name = "next_due_date_iso")
    val nextDueDateIso: String = "",

    @ColumnInfo(name = "total_deposit")
    val totalDeposit: Long = 0,

    @ColumnInfo(name = "status")
    val status: String = "active",

    @ColumnInfo(name = "is_active")
    val isActive: Int = 1,

    @ColumnInfo(name = "short_code")
    val shortCode: String? = null
)
