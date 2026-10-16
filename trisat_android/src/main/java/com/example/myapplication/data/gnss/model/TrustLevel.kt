package com.example.myapplication.data.gnss.model

import androidx.compose.ui.graphics.Color

enum class TrustLevel(
    val displayName: String,
    val description: String,
    val minScore: Float,
    val maxScore: Float
) {
    HIGH_TRUST(
        displayName = "HIGH TRUST",
        description = "High Confidence - Multi-constellation verified & Cryptographically authenticated",
        minScore = 80.0f,
        maxScore = 100.0f
    ),
    MEDIUM_TRUST(
        displayName = "MEDIUM TRUST",
        description = "Moderate Confidence - Minor signal degradation or unverified OSNMA signatures",
        minScore = 50.0f,
        maxScore = 79.99f
    ),
    LOW_TRUST(
        displayName = "LOW TRUST / THREAT DETECTED",
        description = "High Risk - Active spoofing or severe RF jamming interference detected",
        minScore = 0.0f,
        maxScore = 49.99f
    );

    companion object {
        fun fromScore(score: Float): TrustLevel {
            return when {
                score >= 80.0f -> HIGH_TRUST
                score >= 50.0f -> MEDIUM_TRUST
                else -> LOW_TRUST
            }
        }
    }
}
