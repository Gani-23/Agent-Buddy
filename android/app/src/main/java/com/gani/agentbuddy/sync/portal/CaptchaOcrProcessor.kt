package com.gani.agentbuddy.sync.portal

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter
import android.graphics.Paint
import android.util.Base64
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine

object CaptchaOcrProcessor {

    private val recognizer by lazy {
        TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)
    }

    suspend fun recognizeFromBase64(base64String: String): String = suspendCancellableCoroutine { continuation ->
        try {
            val cleanBase64 = if (base64String.contains(",")) {
                base64String.substringAfter(",")
            } else {
                base64String
            }
            val decodedBytes = Base64.decode(cleanBase64, Base64.DEFAULT)
            val rawBitmap = BitmapFactory.decodeByteArray(decodedBytes, 0, decodedBytes.size)

            if (rawBitmap == null) {
                continuation.resume("")
                return@suspendCancellableCoroutine
            }

            // 1. Zoom / Scale up 3.5x so characters are large enough for ML Kit (~120px height)
            val scaleFactor = 3.5f
            val scaledWidth = (rawBitmap.width * scaleFactor).toInt().coerceAtLeast(300)
            val scaledHeight = (rawBitmap.height * scaleFactor).toInt().coerceAtLeast(100)
            val scaledBitmap = Bitmap.createScaledBitmap(rawBitmap, scaledWidth, scaledHeight, true)

            // 2. Add white padding around image so characters don't touch edges
            val padding = 30
            val paddedBitmap = Bitmap.createBitmap(
                scaledWidth + (padding * 2),
                scaledHeight + (padding * 2),
                Bitmap.Config.ARGB_8888
            )
            val canvas = Canvas(paddedBitmap)
            canvas.drawColor(Color.WHITE)

            // 3. Apply grayscale & contrast enhancement for reddish text on grey background
            val colorMatrix = ColorMatrix().apply {
                setSaturation(0f) // Grayscale
            }
            val contrast = 1.8f
            val translate = (-0.5f * contrast + 0.5f) * 255f
            val contrastMatrix = floatArrayOf(
                contrast, 0f, 0f, 0f, translate,
                0f, contrast, 0f, 0f, translate,
                0f, 0f, contrast, 0f, translate,
                0f, 0f, 0f, 1f, 0f
            )
            colorMatrix.postConcat(ColorMatrix(contrastMatrix))

            val paint = Paint(Paint.FILTER_BITMAP_FLAG).apply {
                colorFilter = ColorMatrixColorFilter(colorMatrix)
            }
            canvas.drawBitmap(scaledBitmap, padding.toFloat(), padding.toFloat(), paint)

            val inputImage = InputImage.fromBitmap(paddedBitmap, 0)
            recognizer.process(inputImage)
                .addOnSuccessListener { visionText ->
                    // Filter down to alphanumeric tokens
                    val cleanText = visionText.text
                        .replace("\\s+".toRegex(), "")
                        .filter { it.isLetterOrDigit() }
                        .trim()
                    continuation.resume(cleanText)
                }
                .addOnFailureListener {
                    continuation.resume("")
                }
        } catch (e: Exception) {
            continuation.resume("")
        }
    }
}
