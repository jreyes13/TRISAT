package com.example.myapplication.data.gnss.model

enum class OsnmaStatus(
    val label: String,
    val description: String
) {
    VERIFIED(
        label = "VERIFIED",
        description = "Galileo E1B cryptographic navigation message signature authenticated via Root Key."
    ),
    UNAUTHENTICATED(
        label = "UNAUTHENTICATED",
        description = "Galileo signals tracked but OSNMA cryptographic signature is missing or unverified."
    ),
    FAILED(
        label = "AUTH FAILED",
        description = "Galileo cryptographic signature verification failed - potential message tampering."
    ),
    NOT_AVAILABLE(
        label = "N/A",
        description = "No active Galileo satellites currently tracked for OSNMA verification."
    )
}
