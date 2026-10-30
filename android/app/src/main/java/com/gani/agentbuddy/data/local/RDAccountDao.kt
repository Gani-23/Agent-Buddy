package com.gani.agentbuddy.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import com.gani.agentbuddy.data.model.RDAccount
import kotlinx.coroutines.flow.Flow

@Dao
interface RDAccountDao {

    @Query("SELECT * FROM rd_accounts WHERE is_active = 1 ORDER BY CAST(short_code AS INTEGER) ASC, account_no ASC")
    fun getActiveAccounts(): Flow<List<RDAccount>>

    @Query("SELECT * FROM rd_accounts ORDER BY CAST(short_code AS INTEGER) ASC, account_no ASC")
    fun getAllAccounts(): Flow<List<RDAccount>>

    @Query("SELECT * FROM rd_accounts")
    suspend fun getAllAccountsList(): List<RDAccount>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertOrUpdateAll(accounts: List<RDAccount>)

    @Query("UPDATE rd_accounts SET short_code = :shortCode WHERE account_no = :accountNo")
    suspend fun updateShortCode(accountNo: String, shortCode: String): Int

    @Query("DELETE FROM rd_accounts WHERE account_no = :accountNo")
    suspend fun deleteAccountByNumber(accountNo: String): Int

    @Query("SELECT COUNT(*) FROM rd_accounts WHERE is_active = 1")
    suspend fun getActiveCount(): Int

    @Query("SELECT SUM(amount) FROM rd_accounts WHERE is_active = 1")
    suspend fun getTotalMonthlyVolume(): Long?
}
