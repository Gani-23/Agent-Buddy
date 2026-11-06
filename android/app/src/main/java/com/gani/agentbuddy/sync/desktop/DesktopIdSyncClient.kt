package com.gani.agentbuddy.sync.desktop

import android.content.Context
import com.gani.agentbuddy.data.local.AgentBuddyDatabase
import com.gani.agentbuddy.data.model.RDAccount
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL

object DesktopIdSyncClient {

    suspend fun syncIdsFromDesktopUrl(context: Context, rawInput: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            var urlString = rawInput.trim()
            if (!urlString.startsWith("http://") && !urlString.startsWith("https://")) {
                val isNumericIp = urlString.matches(Regex("""^\d{1,3}(\.\d{1,3}){3}(:\d+)?.*"""))
                urlString = if (isNumericIp) "http://$urlString" else "https://$urlString"
            }

            // Cleanly format URL: add default port :5055 ONLY for local IPs / .local, not for public domains
            try {
                val uri = URI(urlString)
                val scheme = uri.scheme ?: "https"
                val host = uri.host ?: urlString.substringAfter("://").substringBefore(":")
                val isLocalNetwork = host.matches(Regex("""^\d{1,3}(\.\d{1,3}){3}$""")) ||
                                     host.endsWith(".local", ignoreCase = true) ||
                                     host.equals("localhost", ignoreCase = true)

                val portStr = when {
                    uri.port > 0 -> ":${uri.port}"
                    isLocalNetwork -> ":5055"
                    else -> ""
                }
                val path = if (uri.path.isNullOrBlank() || uri.path == "/") "/accounts" else uri.path
                urlString = "$scheme://$host$portStr$path"
            } catch (_: Exception) { }

            android.util.Log.i("DesktopSync", "Connecting to Sync URL: $urlString")

            val url = URL(urlString)
            val connection = (url.openConnection() as HttpURLConnection).apply {
                connectTimeout = 15000
                readTimeout = 30000
                requestMethod = "GET"
                setRequestProperty("Accept", "application/json")
            }

            if (connection.responseCode !in 200..299) {
                return@withContext Result.failure(Exception("Desktop replied with HTTP ${connection.responseCode}"))
            }

            val body = connection.inputStream.bufferedReader().use { it.readText() }
            val database = AgentBuddyDatabase.getDatabase(context)
            val dao = database.rdAccountDao()

            if (body.trim().startsWith("[")) {
                val array = JSONArray(body)
                if (array.length() == 0) {
                    return@withContext Result.success("Desktop server returned 0 records.")
                }

                val first = array.getJSONObject(0)
                // If it contains full account fields (like account_name, denomination, or month_paid_upto)
                if (first.has("account_name") || first.has("denomination") || first.has("month_paid_upto")) {
                    val existingAccounts = dao.getAllAccountsList()
                    val existingMap = existingAccounts.associateBy { it.accountNo }
                    val accountsToUpsert = ArrayList<RDAccount>(array.length())

                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        val accNo = obj.optString("account_no", "").trim()
                        if (accNo.length >= 8) {
                            val existing = existingMap[accNo]
                            val shortCode = obj.optString("short_code", "").takeIf { it.isNotBlank() } ?: existing?.shortCode
                            val amount = obj.optLong("amount", 0L).let {
                                if (it > 0) it else parseDenomination(obj.optString("denomination", "0"))
                            }
                            val totalDep = obj.optLong("total_deposit", 0L)
                            val monthNum = obj.optInt("month_paid_upto_num", 0)

                            accountsToUpsert.add(
                                RDAccount(
                                    id = existing?.id ?: 0,
                                    accountNo = accNo,
                                    accountName = obj.optString("account_name", "").trim().ifEmpty { existing?.accountName ?: "" },
                                    aslaasNo = obj.optString("aslaas_no", "").trim().ifEmpty { existing?.aslaasNo ?: "" },
                                    denomination = obj.optString("denomination", "").ifEmpty { existing?.denomination ?: "0" },
                                    monthPaidUpto = obj.optString("month_paid_upto", "").ifEmpty { existing?.monthPaidUpto ?: "" },
                                    nextInstallmentDate = obj.optString("next_installment_date", "").ifEmpty { existing?.nextInstallmentDate ?: "" },
                                    amount = amount,
                                    monthPaidUptoNum = if (monthNum > 0) monthNum else (existing?.monthPaidUptoNum ?: 0),
                                    nextDueDateIso = obj.optString("next_due_date_iso", "").ifEmpty { existing?.nextDueDateIso ?: "" },
                                    totalDeposit = if (totalDep > 0) totalDep else (existing?.totalDeposit ?: 0L),
                                    status = obj.optString("status", "active"),
                                    isActive = obj.optInt("is_active", 1),
                                    shortCode = shortCode
                                )
                            )
                        }
                    }

                    dao.insertOrUpdateAll(accountsToUpsert)
                    return@withContext Result.success("✓ Synced ${accountsToUpsert.size} accounts & IDs from Desktop!")
                } else {
                    // Fast short-code mapping
                    var updated = 0
                    for (i in 0 until array.length()) {
                        val obj = array.getJSONObject(i)
                        val accNo = obj.optString("account_no", "").trim()
                        val shortCode = obj.optString("short_code", "").trim()
                        if (accNo.isNotEmpty() && shortCode.isNotEmpty()) {
                            val rows = dao.updateShortCode(accNo, shortCode)
                            if (rows > 0) updated++
                        }
                    }
                    return@withContext Result.success("✓ Updated $updated account IDs/short codes!")
                }
            } else {
                return@withContext Result.failure(Exception("Unrecognized data format received from Desktop"))
            }
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun parseDenomination(str: String): Long {
        return str.replace("Cr.", "", ignoreCase = true).replace(",", "").trim().toLongOrNull() ?: 0L
    }
}
