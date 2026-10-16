package com.example.myapplication.data.gnss.model

import android.location.GnssStatus

enum class ConstellationType(
    val displayName: String,
    val codePrefix: String
) {
    GPS("GPS", "G"),
    GALILEO("Galileo", "E"),
    GLONASS("GLONASS", "R"),
    BEIDOU("BeiDou", "B"),
    QZSS("QZSS", "Q"),
    SBAS("SBAS", "S"),
    UNKNOWN("Unknown", "U");

    companion object {
        fun fromGnssStatusConstellation(constellationType: Int): ConstellationType {
            return when (constellationType) {
                GnssStatus.CONSTELLATION_GPS -> GPS
                GnssStatus.CONSTELLATION_GALILEO -> GALILEO
                GnssStatus.CONSTELLATION_GLONASS -> GLONASS
                GnssStatus.CONSTELLATION_BEIDOU -> BEIDOU
                GnssStatus.CONSTELLATION_QZSS -> QZSS
                GnssStatus.CONSTELLATION_SBAS -> SBAS
                else -> UNKNOWN
            }
        }

        fun fromString(name: String): ConstellationType {
            return entries.find {
                it.name.equals(name, ignoreCase = true) ||
                        it.displayName.equals(name, ignoreCase = true)
            } ?: UNKNOWN
        }
    }
}
