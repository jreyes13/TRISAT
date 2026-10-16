package com.example.myapplication.data.gnss.model

data class LocationData(
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitude: Double = 0.0,
    val speed: Float = 0f,
    val bearing: Float = 0f,
    val accuracy: Float = 0f,
    val verticalAccuracyMeters: Float = 0f,
    val speedAccuracyMetersPerSecond: Float = 0f,
    val bearingAccuracyDegrees: Float = 0f,
    val elapsedRealtimeNanos: Long = 0L,
    val timestampMillis: Long = System.currentTimeMillis(),
    val provider: String = "unknown",
    val isMock: Boolean = false,
    val hasFix: Boolean = false
)
