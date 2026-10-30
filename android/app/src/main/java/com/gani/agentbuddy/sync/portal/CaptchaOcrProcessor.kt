package com.gani.agentbuddy.sync.portal

import android.graphics.BitmapFactory
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
            val bitmap = BitmapFactory.decodeByteArray(decodedBytes, 0, decodedBytes.size)

            if (bitmap == null) {
                continuation.resume("")
                return@suspendCancellableCoroutine
            }

            val inputImage = InputImage.fromBitmap(bitmap, 0)
            recognizer.process(inputImage)
                .addOnSuccessListener { visionText ->
                    // Strip non-alphanumeric characters, keeping standard 5-6 letter CAPTCHA
                    val cleanText = visionText.text
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
