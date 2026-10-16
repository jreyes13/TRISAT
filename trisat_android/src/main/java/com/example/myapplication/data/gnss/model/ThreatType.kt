package com.example.myapplication.data.gnss.model

enum class ThreatType(
    val title: String,
    val description: String,
    val severity: String
) {
    SPOOFING_SIMULATOR_DETECTED(
        title = "Spoofing Simulator Signature",
        description = "Unnatural uniform C/N0 power distribution or excessively elevated signal power across channels detected.",
        severity = "HIGH"
    ),
    RF_JAMMING_ELEVATED(
        title = "RF Jamming Interference",
        description = "Abnormally low Automatic Gain Control (AGC) level or sharp gain drop indicating strong in-band RF interference.",
        severity = "HIGH"
    ),
    CLOCK_DRIFT_ANOMALY(
        title = "Clock Drift Instability",
        description = "Excessive hardware clock drift rate or clock bias discontinuity jump detected.",
        severity = "MEDIUM"
    ),
    CONSTELLATION_SPOOFING(
        title = "Constellation Manipulation",
        description = "Single constellation dependency or unnatural constellation power imbalance observed.",
        severity = "MEDIUM"
    ),
    UNAUTHENTICATED_SIGNAL(
        title = "Unauthenticated Galileo OSNMA",
        description = "Galileo OSNMA cryptographic signature validation state unverified or signature mismatch.",
        severity = "MEDIUM"
    ),
    LOCATION_MOCK_DETECTED(
        title = "System Mock Location Provider",
        description = "Device position stream supplied by mock location provider or spoofed coordinate injection.",
        severity = "HIGH"
    ),
    POOR_GEOMETRY_WARNING(
        title = "Satellite Geometry Degradation",
        description = "High Geometric/Horizontal Dilution of Precision (DOP > 5.0) or satellite clustering detected.",
        severity = "LOW"
    )
}
