package com.example.myapplication.data.gnss.model

data class IntelligenceLogEntry(
    val timestampMillis: Long = System.currentTimeMillis(),
    val severity: String = "INFO", // "INFO", "SECURE", "WARNING", "ALERT"
    val tag: String,
    val message: String
)
