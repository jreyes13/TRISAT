package com.example.myapplication.data.gnss.tle

import com.example.myapplication.data.gnss.model.ConstellationType
import com.example.myapplication.data.gnss.model.SatelliteDetail
import java.util.Date
import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.tan

/**
 * Keplerian SGP4 Orbit Propagator for 4-Constellation Simulation (Galileo, GPS, GLONASS, BeiDou).
 */
object Sgp4OrbitPropagator {

    private const val PI = Math.PI

    private fun calculateGmst(date: Date, offsetHours: Double): Double {
        val utcMillis = date.time + (offsetHours * 3600.0 * 1000.0).toLong()
        val julianDate = (utcMillis / 1000.0) / 86400.0 + 2440587.5
        val d = julianDate - 2451545.0
        var gmstDeg = (280.46061837 + 360.98564736629 * d) % 360.0
        if (gmstDeg < 0.0) gmstDeg += 360.0
        return gmstDeg * PI / 180.0
    }

    /**
     * Propagates orbital TLE elements to calculate Elevation & Azimuth relative to ground observer location.
     */
    fun propagateToSkyplot(
        tles: List<TleData>,
        timeOffsetHours: Double,
        observerLat: Double,
        observerLon: Double
    ): List<SatelliteDetail> {
        val catalog = if (tles.isEmpty()) TleDownloader.generateBuiltInFourConstellationCatalog() else tles

        val satList = mutableListOf<SatelliteDetail>()
        val deltaMinutes = timeOffsetHours * 60.0
        val gmstRad = calculateGmst(Date(), timeOffsetHours)

        val obsLatRad = observerLat * PI / 180.0
        val obsLonRad = observerLon * PI / 180.0

        for (tle in catalog) {
            val meanAnomalyRad = ((tle.meanAnomalyDegrees + (tle.meanMotionRevsPerDay * 360.0 * (deltaMinutes / 1440.0))) % 360.0) * PI / 180.0
            val raanRad = ((tle.raanDegrees + (deltaMinutes * 0.25)) % 360.0) * PI / 180.0
            val incRad = tle.inclinationDegrees * PI / 180.0
            val argPerigeeRad = tle.argPerigeeDegrees * PI / 180.0

            val uRad = meanAnomalyRad + argPerigeeRad
            val satDeclination = asin(sin(incRad) * sin(uRad))
            val satRightAscension = raanRad + atan2(cos(incRad) * sin(uRad), cos(uRad))
            val hourAngle = gmstRad + obsLonRad - satRightAscension

            val sinElev = sin(obsLatRad) * sin(satDeclination) + cos(obsLatRad) * cos(satDeclination) * cos(hourAngle)
            val elevRad = asin(sinElev.coerceIn(-1.0, 1.0))
            val elevDeg = elevRad * 180.0 / PI

            // Elevation mask: 10°
            if (elevDeg < 10.0) continue

            val cosHA = cos(hourAngle)
            val sinHA = sin(hourAngle)
            val azimRad = atan2(-sinHA, cos(obsLatRad) * tan(satDeclination) - sin(obsLatRad) * cosHA)
            var azimDeg = (azimRad * 180.0 / PI + 360.0) % 360.0
            if (azimDeg < 0.0) azimDeg += 360.0

            val constKind = when (tle.constellation.uppercase()) {
                "GALILEO" -> ConstellationType.GALILEO
                "GPS"     -> ConstellationType.GPS
                "GLONASS" -> ConstellationType.GLONASS
                "BEIDOU"  -> ConstellationType.BEIDOU
                else      -> ConstellationType.GALILEO
            }

            val svidNum = tle.svid.toIntOrNull() ?: 1
            val isUsed = elevDeg > 18.0

            // Realistic C/N0 curve: 31 dB-Hz at 10°, up to 48.5 dB-Hz at zenith
            val cno = (31.0 + (elevDeg / 90.0) * 17.5).toFloat()

            val carrierFreqHz = when (constKind) {
                ConstellationType.GALILEO -> 1575.42e6
                ConstellationType.GPS     -> 1575.42e6
                ConstellationType.GLONASS -> 1602.00e6
                ConstellationType.BEIDOU  -> 1561.098e6
                else -> 1575.42e6
            }

            satList.add(
                SatelliteDetail(
                    svid = svidNum,
                    constellation = constKind,
                    cn0DbHz = cno,
                    elevationDegrees = elevDeg.toFloat(),
                    azimuthDegrees = azimDeg.toFloat(),
                    carrierFrequencyHz = carrierFreqHz,
                    hasCarrierFrequency = true,
                    usedInFix = isUsed,
                    hasAlmanacData = true,
                    hasEphemerisData = true
                )
            )
        }

        // Group by constellation and pick up to 6 highest-elevation satellites per constellation
        val finalCapped = mutableListOf<SatelliteDetail>()
        for (c in listOf(ConstellationType.GALILEO, ConstellationType.GPS, ConstellationType.GLONASS, ConstellationType.BEIDOU)) {
            val constSats = satList.filter { it.constellation == c }
                .sortedByDescending { it.elevationDegrees }
                .take(6)
            finalCapped.addAll(constSats)
        }

        return finalCapped
    }
}
