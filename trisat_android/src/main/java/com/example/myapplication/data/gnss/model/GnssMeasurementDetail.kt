package com.example.myapplication.data.gnss.model

data class GnssMeasurementDetail(
    val svid: Int,
    val constellation: ConstellationType,
    val state: Int,
    val receivedSvTimeNanos: Long,
    val cn0DbHz: Double,
    val pseudorangeRateMetersPerSecond: Double = 0.0,
    val pseudorangeRateUncertaintyMetersPerSecond: Double = 0.0,
    val accumulatedDeltaRangeState: Int = 0,
    val accumulatedDeltaRangeMeters: Double = 0.0,
    val carrierFrequencyHz: Float = 0f,
    val automaticGainControlLevelDb: Double = 0.0,
    val hasAgc: Boolean = false,
    val multipathIndicator: Int = 0, // 0: UNKNOWN, 1: PRESENT, 2: NOT_PRESENT
    val snrInDb: Double = 0.0
) {
    val formattedSvid: String
        get() = "${constellation.codePrefix}${svid.toString().padStart(2, '0')}"

    val carrierFrequencyMhz: Float
        get() = if (carrierFrequencyHz > 0) carrierFrequencyHz / 1e6f else 0f

    val multipathIndicatorDescription: String
        get() = when (multipathIndicator) {
            1 -> "PRESENT"
            2 -> "NOT_PRESENT"
            else -> "UNKNOWN"
        }
}
