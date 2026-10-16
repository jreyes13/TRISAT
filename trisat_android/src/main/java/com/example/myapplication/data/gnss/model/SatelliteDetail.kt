package com.example.myapplication.data.gnss.model

data class SatelliteDetail(
    val svid: Int,
    val constellation: ConstellationType,
    val cn0DbHz: Float,
    val elevationDegrees: Float,
    val azimuthDegrees: Float,
    val carrierFrequencyHz: Double = 0.0,
    val hasCarrierFrequency: Boolean = false,
    val usedInFix: Boolean = false,
    val hasAlmanacData: Boolean = false,
    val hasEphemerisData: Boolean = false,
    val hasBasebandCn0: Boolean = false,
    val basebandCn0DbHz: Float = 0f
) {
    val formattedSvid: String
        get() = "${constellation.codePrefix}${svid.toString().padStart(2, '0')}"

    val carrierFrequencyMhz: Double
        get() = if (hasCarrierFrequency && carrierFrequencyHz > 0) carrierFrequencyHz / 1e6 else 0.0
}
