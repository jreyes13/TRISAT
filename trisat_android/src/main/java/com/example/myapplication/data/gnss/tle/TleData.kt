package com.example.myapplication.data.gnss.tle

/**
 * Represents parsed Two-Line Element (TLE) orbital data for a satellite.
 */
data class TleData(
    val name: String,
    val svid: String,
    val constellation: String,
    val line1: String,
    val line2: String,
    val inclinationDegrees: Double,
    val raanDegrees: Double,
    val eccentricity: Double,
    val argPerigeeDegrees: Double,
    val meanAnomalyDegrees: Double,
    val meanMotionRevsPerDay: Double
) {
    val id: String
        get() = "${constellation}_$svid"
}
