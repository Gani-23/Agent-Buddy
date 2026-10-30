package com.gani.agentbuddy.sync.desktop

import android.content.Context
import com.gani.agentbuddy.data.local.AgentBuddyDatabase
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import java.net.HttpURLConnection
import java.net.URL

object DesktopIdSyncClient {

    suspend fun syncIdsFromDesktopUrl(context: Context, desktopSyncUrl: String): Result<Int> = withContext(Dispatchers.IO) {
        try {
            val url = URL(desktopSyncUrl)
            val connection = (url.openConnection() as HttpURLConnection).apply {
                connectTimeout = 4000
                readTimeout = 6000
                requestMethod = "GET"
                setRequestProperty("Accept", "application/json")
            }

            if (connection.responseCode !in 200..299) {
                return@withContext Result.failure(Exception("Desktop replied with HTTP ${connection.responseCode}"))
            }

            val body = connection.inputStream.bufferedReader().use { it.readText() }
            val array = JSONArray(body)
            val database = AgentBuddyDatabase.getDatabase(context)
            val dao = database.rdAccountDao()

            var updatedCount = 0
            for (i in 0 until array.length()) {
                val item = array.getJSONObject(i)
                val accountNo = item.optString("account_no", "").trim()
                val shortCode = item.optString("short_code", "").trim()

                if (accountNo.isNotEmpty() && shortCode.isNotEmpty()) {
                    val rows = dao.updateShortCode(accountNo, shortCode)
                    if (rows > 0) updatedCount++
                }
            }

            Result.success(updatedCount)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
