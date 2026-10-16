package com.example.myapplication.data.gnss.model

data class GnssMeasurementsData(
    val clockTimeNanos: Long = 0L,
    val clockHardwareClockDiscontinuityCount: Int = 0,
    val fullBiasNanos: Long? = null,
    val biasNanos: Double? = null,
    val biasUncertaintyNanos: Double? = null,
    val driftNanosPerSecond: Double? = null,
    val driftUncertaintyNanosPerSecond: Double? = null,
    val measurements: List<GnssMeasurementDetail> = emptyList(),
    val isMock: Boolean = false,
    val timestampMillis: Long = System.currentTimeMillis()
) {
    val averageAgcDb: Double
        get() {
            val agcMeasurements = measurements.filter { it.hasAgc }
            return if (agcMeasurements.isNotEmpty()) {
                agcMeasurements.map { it.automaticGainControlLevelDb }.average()
            } else 0.0
        }
}
