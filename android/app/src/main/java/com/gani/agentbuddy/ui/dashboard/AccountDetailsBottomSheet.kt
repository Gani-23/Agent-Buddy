package com.gani.agentbuddy.ui.dashboard

import android.content.Context
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.gani.agentbuddy.data.model.RDAccount
import java.text.NumberFormat
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountDetailsBottomSheet(
    account: RDAccount,
    onDismiss: () -> Unit,
    onSendWhatsApp: () -> Unit
) {
    val context = LocalContext.current
    val clipboardManager = LocalClipboardManager.current
    val currencyFormat = remember { NumberFormat.getCurrencyInstance(Locale("en", "IN")) }

    val amountVal = account.numericAmount
    val totalDeposit = if (account.totalDeposit > 0) account.totalDeposit else (amountVal * account.monthPaidUptoNum)
    val progress = (account.monthPaidUptoNum / 60f).coerceIn(0f, 1f)
    val remainingMonths = (60 - account.monthPaidUptoNum).coerceAtLeast(0)

    val dueDay = account.nextInstallmentDate.split("-", "/").firstOrNull()?.toIntOrNull() ?: 10
    val cycleName = if (dueDay in 1..15) "1st Half (1st – 15th)" else "2nd Half (16th – End of month)"

    val calMonthEnd = Calendar.getInstance().apply {
        set(Calendar.DAY_OF_MONTH, getActualMaximum(Calendar.DAY_OF_MONTH))
        set(Calendar.HOUR_OF_DAY, 23)
        set(Calendar.MINUTE, 59)
        set(Calendar.SECOND, 59)
    }
    val dueDate = parseDate(account.nextInstallmentDate)
    val isDepositedThisMonth = dueDate != null && dueDate.after(calMonthEnd.time)

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp),
        containerColor = MaterialTheme.colorScheme.surface,
        dragHandle = { BottomSheetDefaults.DragHandle() }
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 28.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Header Row: Account No + Short Code + Copy Button
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    if (!account.shortCode.isNullOrEmpty()) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(8.dp))
                                .background(Color(0xFFE0E7FF))
                                .padding(horizontal = 9.dp, vertical = 4.dp)
                        ) {
                            Text(
                                text = "#${account.shortCode}",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color(0xFF4338CA)
                            )
                        }
                    }

                    Text(
                        text = account.accountNo,
                        fontFamily = FontFamily.Monospace,
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )

                    IconButton(
                        onClick = {
                            clipboardManager.setText(AnnotatedString(account.accountNo))
                            Toast.makeText(context, "Account number copied!", Toast.LENGTH_SHORT).show()
                        },
                        modifier = Modifier.size(36.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.ContentCopy,
                            contentDescription = "Copy Account Number",
                            tint = Color(0xFF475569),
                            modifier = Modifier.size(18.dp)
                        )
                    }
                }

                // Status Badge
                val (statusBg, statusColor, statusLabel) = when {
                    account.monthPaidUptoNum >= 60 -> Triple(Color(0xFFEDE9FE), Color(0xFF6D28D9), "Matured")
                    account.monthPaidUptoNum in 54..59 -> Triple(Color(0xFFFEF3C7), Color(0xFFB45309), "Near Maturity")
                    isDepositedThisMonth -> Triple(Color(0xFFD1FAE5), Color(0xFF047857), "Deposited")
                    else -> Triple(Color(0xFFFEE2E2), Color(0xFFB91C1C), "Pending")
                }

                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(12.dp))
                        .background(statusBg)
                        .padding(horizontal = 12.dp, vertical = 5.dp)
                ) {
                    Text(
                        text = statusLabel,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = statusColor
                    )
                }
            }

            // Account Name
            Column {
                Text(
                    text = account.accountName,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = "Post Office Recurring Deposit (RD)",
                    fontSize = 14.sp,
                    color = Color(0xFF64748B)
                )
            }

            // 2x2 Metric Highlights Card
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    DetailMetricCard(
                        title = "MONTHLY DENOMINATION",
                        value = account.cleanDenomination,
                        subtitle = "Per month",
                        valueColor = Color(0xFF2563EB),
                        modifier = Modifier.weight(1f)
                    )
                    DetailMetricCard(
                        title = "TOTAL DEPOSITED",
                        value = currencyFormat.format(totalDeposit).replace(".00", ""),
                        subtitle = "${account.monthPaidUptoNum} months saved",
                        valueColor = Color(0xFF059669),
                        modifier = Modifier.weight(1f)
                    )
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    Card(
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(14.dp),
                        colors = CardDefaults.cardColors(containerColor = Color(0xFFF8FAFC)),
                        border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFE2E8F0))
                    ) {
                        Column(modifier = Modifier.padding(14.dp)) {
                            Text(
                                text = "INSTALLMENTS",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color(0xFF64748B)
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "${account.monthPaidUptoNum} / 60",
                                fontSize = 19.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color(0xFF0F172A)
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            LinearProgressIndicator(
                                progress = { progress },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(7.dp)
                                    .clip(RoundedCornerShape(3.5.dp)),
                                color = Color(0xFF2563EB),
                                trackColor = Color(0xFFE2E8F0),
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = if (remainingMonths == 0) "Matured" else "$remainingMonths remaining",
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                color = Color(0xFF64748B)
                            )
                        }
                    }

                    DetailMetricCard(
                        title = "NEXT DUE DATE",
                        value = account.nextInstallmentDate.ifEmpty { "Not specified" },
                        subtitle = if (isDepositedThisMonth) "Paid for current month" else "Due this cycle",
                        valueColor = if (isDepositedThisMonth) Color(0xFF047857) else Color(0xFFDC2626),
                        modifier = Modifier.weight(1f)
                    )
                }
            }

            // Additional Detailed Specs Table
            Card(
                shape = RoundedCornerShape(14.dp),
                colors = CardDefaults.cardColors(containerColor = Color(0xFFF8FAFC)),
                border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFE2E8F0)),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    DetailRow(label = "Collection Cycle", value = cycleName)
                    HorizontalDivider(color = Color(0xFFE2E8F0), thickness = 0.5.dp)
                    DetailRow(
                        label = "ASLAAS Number",
                        value = account.aslaasNo.ifEmpty { "Not linked" }
                    )
                    HorizontalDivider(color = Color(0xFFE2E8F0), thickness = 0.5.dp)
                    DetailRow(label = "Month Paid Upto", value = "Month #${account.monthPaidUpto.ifEmpty { "0" }}")
                    HorizontalDivider(color = Color(0xFFE2E8F0), thickness = 0.5.dp)
                    DetailRow(label = "Desktop ID", value = account.shortCode?.let { "#$it" } ?: "Not synced")
                }
            }

            Spacer(modifier = Modifier.height(4.dp))

            // Action Buttons
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                OutlinedButton(
                    onClick = {
                        val detailsText = """
                            Account No: ${account.accountNo}
                            Name: ${account.accountName}
                            Monthly: ${account.cleanDenomination}
                            Paid Upto: Month #${account.monthPaidUptoNum} of 60
                            Next Due: ${account.nextInstallmentDate}
                        """.trimIndent()
                        clipboardManager.setText(AnnotatedString(detailsText))
                        Toast.makeText(context, "Full account summary copied!", Toast.LENGTH_SHORT).show()
                    },
                    shape = RoundedCornerShape(14.dp),
                    modifier = Modifier
                        .weight(1f)
                        .height(52.dp)
                ) {
                    Icon(
                        imageVector = Icons.Default.CopyAll,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(text = "Copy Summary", fontSize = 14.sp, fontWeight = FontWeight.Bold)
                }

                Button(
                    onClick = onSendWhatsApp,
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366)),
                    shape = RoundedCornerShape(14.dp),
                    modifier = Modifier
                        .weight(1.3f)
                        .height(52.dp)
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.Send,
                        contentDescription = null,
                        tint = Color.White,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "WhatsApp",
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color.White
                    )
                }
            }
        }
    }
}

@Composable
private fun DetailMetricCard(
    title: String,
    value: String,
    subtitle: String,
    valueColor: Color,
    modifier: Modifier = Modifier
) {
    Card(
        modifier = modifier,
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = Color(0xFFF8FAFC)),
        border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFE2E8F0))
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Text(
                text = title,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = Color(0xFF64748B)
            )
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = value,
                fontSize = 19.sp,
                fontWeight = FontWeight.Black,
                color = valueColor
            )
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = subtitle,
                fontSize = 12.sp,
                fontWeight = FontWeight.Medium,
                color = Color(0xFF64748B)
            )
        }
    }
}

@Composable
private fun DetailRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = label, fontSize = 14.sp, color = Color(0xFF64748B))
        Text(text = value, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
    }
}

private fun parseDate(dateStr: String): Date? {
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
