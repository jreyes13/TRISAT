package com.example.myapplication.data.gnss.engine

import com.example.myapplication.data.gnss.model.ConstellationType
import com.example.myapplication.data.gnss.model.GnssMeasurementsData
import com.example.myapplication.data.gnss.model.GnssStatusData
import com.example.myapplication.data.gnss.model.IntelligenceLogEntry
import com.example.myapplication.data.gnss.model.LocationData
import com.example.myapplication.data.gnss.model.OsnmaStatus
import com.example.myapplication.data.gnss.model.ThreatType
import com.example.myapplication.data.gnss.model.TrustLevel
import com.example.myapplication.data.gnss.model.TrustReport
import com.example.myapplication.data.gnss.model.TrustSubScores
import java.util.Locale
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin
import kotlin.math.sqrt

data class DopEvaluationResult(
    val score: Float,
    val hdop: Float?,
    val vdop: Float?,
    val logs: List<IntelligenceLogEntry>
)

class TrustScoringEngine {

    fun evaluateTrust(
        gnssStatus: GnssStatusData,
        gnssMeasurements: GnssMeasurementsData,
        location: LocationData,
        osnmaOverride: OsnmaStatus? = null
    ): TrustReport {
        val now = System.currentTimeMillis()
        val detectedThreats = mutableListOf<ThreatType>()
        val warnings = mutableListOf<String>()
        val intelligenceLogs = mutableListOf<IntelligenceLogEntry>()

        // 1. Signal Power Anomalies & C/N0 Evaluation
        val (signalAuthScore, signalLogs) = evaluateSignalPowerAndCn0(gnssStatus, detectedThreats, warnings)
        intelligenceLogs.addAll(signalLogs)

        // 2. AGC Fluctuation & Jamming Detection
        val (agcScore, agcLogs) = evaluateAgcAndJamming(gnssMeasurements, detectedThreats, warnings)
        intelligenceLogs.addAll(agcLogs)

        // 3. Multi-Constellation Diversity
        val (diversityScore, diversityLogs) = evaluateConstellationDiversity(gnssStatus, detectedThreats, warnings)
        intelligenceLogs.addAll(diversityLogs)

        // 4. Geometry & DOP (HDOP/VDOP Estimation)
        val dopResult = evaluateGeometryAndDop(gnssStatus, detectedThreats, warnings)
        intelligenceLogs.addAll(dopResult.logs)

        // 5. Clock Drift & Hardware Anomaly
        val (clockScore, clockLogs) = evaluateClockStability(gnssMeasurements, detectedThreats, warnings)
        intelligenceLogs.addAll(clockLogs)

        // 6. OSNMA / Satellite Signal Authentication
        val (osnmaStatus, osnmaScore, osnmaLogs) = evaluateOsnmaAuthentication(gnssStatus, osnmaOverride, detectedThreats, warnings)
        intelligenceLogs.addAll(osnmaLogs)

        // 7. System Location Mock Check
        if (location.isMock && !gnssStatus.isMock) {
            if (!detectedThreats.contains(ThreatType.LOCATION_MOCK_DETECTED)) {
                detectedThreats.add(ThreatType.LOCATION_MOCK_DETECTED)
                warnings.add("System mock location provider active while hardware raw GNSS differs.")
                intelligenceLogs.add(
                    IntelligenceLogEntry(
                        timestampMillis = now,
                        severity = "ALERT",
                        tag = "LOCATION_MOCK",
                        message = "Device location provider reported mock position injection."
                    )
                )
            }
        }

        // Adjust OSNMA and diversity scores if active spoofing simulator detected
        val adjustedOsnmaScore = if (detectedThreats.contains(ThreatType.SPOOFING_SIMULATOR_DETECTED)) {
            minOf(osnmaScore, 20f)
        } else osnmaScore

        val adjustedDiversityScore = if (detectedThreats.contains(ThreatType.SPOOFING_SIMULATOR_DETECTED)) {
            minOf(diversityScore, 30f)
        } else diversityScore

        // Composite SubScores
        val subScores = TrustSubScores(
            signalAuthenticityScore = signalAuthScore,
            agcInterferenceScore = agcScore,
            constellationDiversityScore = adjustedDiversityScore,
            geometryDopScore = dopResult.score,
            clockStabilityScore = clockScore,
            osnmaAuthScore = adjustedOsnmaScore
        )

        val rawCalculatedScore = subScores.weightedOverallScore

        // Cap trust score at LOW_TRUST (< 50%) if critical threats (Spoofing, Jamming, Location Mock) are detected
        val hasCriticalThreat = detectedThreats.any {
            it == ThreatType.SPOOFING_SIMULATOR_DETECTED ||
                    it == ThreatType.RF_JAMMING_ELEVATED ||
                    it == ThreatType.LOCATION_MOCK_DETECTED
        }

        val overallScore = if (hasCriticalThreat) {
            minOf(rawCalculatedScore, 45f)
        } else {
            rawCalculatedScore
        }.coerceIn(0f, 100f)

        val trustLevel = TrustLevel.fromScore(overallScore)

        val hasAnomalies = detectedThreats.isNotEmpty()
        val hasMeasurements = gnssStatus.satellites.isNotEmpty() && gnssMeasurements.measurements.isNotEmpty()
        val severity = when {
            hasAnomalies && trustLevel == TrustLevel.LOW_TRUST -> "ALERT"
            hasAnomalies || !hasMeasurements || warnings.isNotEmpty() || trustLevel != TrustLevel.HIGH_TRUST -> "WARNING"
            else -> "INFO"
        }
        val anomalySummary = if (hasAnomalies) {
            "${detectedThreats.size} anomaly trigger(s) detected."
        } else {
            "No anomaly triggers detected."
        }
        val score = String.format(Locale.US, "%.1f", overallScore)
        val level = trustLevel.name.replace('_', ' ')
        val trustSummary = if (hasMeasurements) {
            "Calculated trust level $level ($score%)."
        } else {
            "Insufficient GNSS data for a complete assessment. Provisional trust level $level ($score%)."
        }
        intelligenceLogs.add(
            IntelligenceLogEntry(
                timestampMillis = now,
                severity = severity,
                tag = "ENGINE_EVAL",
                message = "$anomalySummary $trustSummary"
            )
        )

        return TrustReport(
            overallScore = overallScore,
            trustLevel = trustLevel,
            subScores = subScores,
            threatsDetected = detectedThreats.distinct(),
            activeThreatWarnings = warnings.distinct(),
            estimatedHdop = dopResult.hdop,
            estimatedVdop = dopResult.vdop,
            osnmaStatus = osnmaStatus,
            intelligenceLogs = intelligenceLogs,
            timestampMillis = now
        )
    }

    private fun evaluateSignalPowerAndCn0(
        status: GnssStatusData,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): Pair<Float, List<IntelligenceLogEntry>> {
        val logs = mutableListOf<IntelligenceLogEntry>()
        val sats = status.satellites
        if (sats.isEmpty()) {
            return Pair(50f, listOf(IntelligenceLogEntry(tag = "C/N0_EVAL", severity = "WARNING", message = "No satellites currently tracked.")))
        }

        val cn0Values = sats.map { it.cn0DbHz }
        val avgCn0 = cn0Values.average().toFloat()
        val maxCn0 = cn0Values.maxOrNull() ?: 0f

        // Standard Deviation of C/N0
        val variance = cn0Values.map { (it - avgCn0) * (it - avgCn0) }.average()
        val stdDevCn0 = sqrt(variance).toFloat()

        var score = 100f

        // Detection Rule A: Uniform C/N0 distribution across satellites (Spoofing Simulator signature)
        if (sats.size >= 4 && stdDevCn0 < 1.2f) {
            score -= 55f
            threats.add(ThreatType.SPOOFING_SIMULATOR_DETECTED)
            warnings.add("Unnatural uniform C/N0 distribution across ${sats.size} satellites (StdDev: ${String.format(Locale.US, "%.2f", stdDevCn0)} dB-Hz).")
            logs.add(
                IntelligenceLogEntry(
                    severity = "ALERT",
                    tag = "SPOOFING_DETECTOR",
                    message = "ALERT: Uniform signal power across satellite channels indicates multi-channel simulator."
                )
            )
        }

        // Detection Rule B: Unrealistically high signal power (> 51 dB-Hz)
        if (maxCn0 > 51.0f || avgCn0 > 48.0f) {
            score -= 40f
            if (!threats.contains(ThreatType.SPOOFING_SIMULATOR_DETECTED)) {
                threats.add(ThreatType.SPOOFING_SIMULATOR_DETECTED)
            }
            warnings.add("Unusually high C/N0 power level (Max: ${String.format(Locale.US, "%.1f", maxCn0)} dB-Hz, Avg: ${String.format(Locale.US, "%.1f", avgCn0)} dB-Hz).")
            logs.add(
                IntelligenceLogEntry(
                    severity = "ALERT",
                    tag = "POWER_ANOMALY",
                    message = "High RF signal power detected above terrestrial open-sky thresholds."
                )
            )
        }

        // Detection Rule C: Depressed C/N0 across all sats (< 24 dB-Hz)
        if (avgCn0 < 24.0f) {
            score -= 30f
            warnings.add("Low average carrier-to-noise ratio (${String.format(Locale.US, "%.1f", avgCn0)} dB-Hz). Potential broad-spectrum attenuation or jamming.")
            logs.add(
                IntelligenceLogEntry(
                    severity = "WARNING",
                    tag = "SIGNAL_DEGRADATION",
                    message = "Average C/N0 depressed below nominal operating threshold."
                )
            )
        }

        logs.add(
            IntelligenceLogEntry(
                severity = "INFO",
                tag = "C/N0_EVAL",
                message = "C/N0 Eval: Avg ${String.format(Locale.US, "%.1f", avgCn0)} dB-Hz, Max ${String.format(Locale.US, "%.1f", maxCn0)} dB-Hz, StdDev ${String.format(Locale.US, "%.2f", stdDevCn0)} dB-Hz."
            )
        )

        return Pair(score.coerceIn(0f, 100f), logs)
    }

    private fun evaluateAgcAndJamming(
        measurements: GnssMeasurementsData,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): Pair<Float, List<IntelligenceLogEntry>> {
        val logs = mutableListOf<IntelligenceLogEntry>()
        val agcList = measurements.measurements.filter { it.hasAgc }.map { it.automaticGainControlLevelDb }

        if (agcList.isEmpty()) {
            return Pair(90f, listOf(IntelligenceLogEntry(tag = "AGC_EVAL", severity = "INFO", message = "AGC measurement level unavailable on current chipset.")))
        }

        val avgAgc = agcList.average()
        val minAgc = agcList.minOrNull() ?: 0.0

        var score = 100f

        if (avgAgc < -10.0 || minAgc < -14.0) {
            val severityDeduction = if (avgAgc < -15.0) 80f else 50f
            score -= severityDeduction
            threats.add(ThreatType.RF_JAMMING_ELEVATED)
            warnings.add("Elevated RF Interference: Automatic Gain Control gain suppressed (Avg AGC: ${String.format(Locale.US, "%.1f", avgAgc)} dB).")
            logs.add(
                IntelligenceLogEntry(
                    severity = "ALERT",
                    tag = "JAMMING_DETECTOR",
                    message = "ALERT: AGC gain suppression indicates high in-band RF power / jamming."
                )
            )
        } else if (avgAgc < -6.0) {
            score -= 20f
            warnings.add("Minor AGC gain decrease observed (${String.format(Locale.US, "%.1f", avgAgc)} dB). Monitoring interference.")
        }

        logs.add(
            IntelligenceLogEntry(
                severity = "INFO",
                tag = "AGC_EVAL",
                message = "AGC Eval: Avg ${String.format(Locale.US, "%.1f", avgAgc)} dB over ${agcList.size} raw channels."
            )
        )

        return Pair(score.coerceIn(0f, 100f), logs)
    }

    private fun evaluateConstellationDiversity(
        status: GnssStatusData,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): Pair<Float, List<IntelligenceLogEntry>> {
        val logs = mutableListOf<IntelligenceLogEntry>()
        val counts = status.constellationCounts
        val activeConstellations = counts.keys.filter { it != ConstellationType.UNKNOWN }

        var score: Float
        when {
            activeConstellations.size >= 4 -> score = 100f
            activeConstellations.size == 3 -> score = 85f
            activeConstellations.size == 2 -> score = 65f
            activeConstellations.size == 1 -> {
                score = 40f
                if (status.satelliteCount > 6) {
                    threats.add(ThreatType.CONSTELLATION_SPOOFING)
                    warnings.add("Single constellation lock (${activeConstellations.first().displayName}) despite high satellite count. Potential filter manipulation.")
                    logs.add(
                        IntelligenceLogEntry(
                            severity = "WARNING",
                            tag = "CONSTELLATION_CHECK",
                            message = "Single constellation dependency flagged."
                        )
                    )
                }
            }
            else -> score = 0f
        }

        logs.add(
            IntelligenceLogEntry(
                severity = "INFO",
                tag = "DIVERSITY_EVAL",
                message = "Constellation Diversity: ${activeConstellations.size} active (${activeConstellations.joinToString { it.codePrefix }})."
            )
        )

        return Pair(score, logs)
    }

    private fun evaluateGeometryAndDop(
        status: GnssStatusData,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): DopEvaluationResult {
        // Count each space vehicle once, even if the receiver reports several signals.
        val satellites = status.satellites.filter {
            it.usedInFix && it.elevationDegrees in 0f..90f && it.azimuthDegrees in 0f..360f
        }.distinctBy { it.constellation to it.svid }

        if (satellites.size < 4) {
            val log = IntelligenceLogEntry(
                tag = "DOP_EVAL",
                severity = "WARNING",
                message = "DOP unavailable: fewer than four satellites used in the fix have valid angles."
            )
            return DopEvaluationResult(40f, null, null, listOf(log))
        }

        val geometry = satellites.map { sat ->
            val elevation = Math.toRadians(sat.elevationDegrees.toDouble())
            val azimuth = Math.toRadians(sat.azimuthDegrees.toDouble())
            val horizontal = cos(elevation)
            doubleArrayOf(horizontal * sin(azimuth), horizontal * cos(azimuth), sin(elevation), 1.0)
        }
        val variances = positionVariances(geometry)
        if (variances == null) {
            val message = "DOP unavailable: satellite geometry cannot reliably resolve position and receiver clock."
            threats.add(ThreatType.POOR_GEOMETRY_WARNING)
            warnings.add(message)
            val log = IntelligenceLogEntry(tag = "DOP_EVAL", severity = "WARNING", message = message)
            return DopEvaluationResult(35f, null, null, listOf(log))
        }

        val hdop = sqrt(variances[0] + variances[1]).toFloat()
        val vdop = sqrt(variances[2]).toFloat()
        val worstDop = maxOf(hdop, vdop)
        val poorGeometry = worstDop > 5f
        val dopSummary = String.format(Locale.US, "Estimated HDOP = %.2f, VDOP = %.2f", hdop, vdop)
        val score = when {
            poorGeometry -> 35f
            worstDop > 3f -> 70f
            worstDop > 2f -> 88f
            else -> 100f
        }

        if (poorGeometry) {
            threats.add(ThreatType.POOR_GEOMETRY_WARNING)
            warnings.add("Poor satellite geometry. $dopSummary. High positioning uncertainty.")
        }
        val log = IntelligenceLogEntry(
            severity = if (poorGeometry) "WARNING" else "INFO",
            tag = "DOP_EVAL",
            message = "$dopSummary."
        )
        return DopEvaluationResult(score, hdop, vdop, listOf(log))
    }

    private fun positionVariances(geometry: List<DoubleArray>): DoubleArray? {
        // QR of the equal-weight ENU geometry with a common receiver clock.
        // Givens rotations avoid squaring the condition number in G^T G.
        val r = Array(4) { DoubleArray(4) }
        for (row in geometry) {
            for (column in 0 until 4) {
                if (row[column] == 0.0) continue
                val norm = hypot(r[column][column], row[column])
                val cosine = r[column][column] / norm
                val sine = row[column] / norm
                r[column][column] = norm
                for (j in column + 1 until 4) {
                    val upper = r[column][j]
                    r[column][j] = cosine * upper + sine * row[j]
                    row[j] = cosine * row[j] - sine * upper
                }
            }
        }

        // Scale the rank tolerance by the norm of the clock column.
        val tolerance = 1e-8 * sqrt(geometry.size.toDouble())
        if ((0 until 4).any { abs(r[it][it]) <= tolerance }) return null

        // diag((G^T G)^-1) is given by the squared row norms of R^-1.
        return DoubleArray(3) { axis ->
            val inverseRow = DoubleArray(4)
            for (column in axis until 4) {
                var value = if (column == axis) 1.0 else 0.0
                for (j in axis until column) {
                    value -= inverseRow[j] * r[j][column]
                }
                inverseRow[column] = value / r[column][column]
            }
            inverseRow.sumOf { it * it }
        }
    }

    private fun evaluateClockStability(
        measurements: GnssMeasurementsData,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): Pair<Float, List<IntelligenceLogEntry>> {
        val logs = mutableListOf<IntelligenceLogEntry>()
        var score = 100f

        val drift = measurements.driftNanosPerSecond
        val discontinuities = measurements.clockHardwareClockDiscontinuityCount

        if (drift != null && abs(drift) > 100.0) {
            score -= 45f
            threats.add(ThreatType.CLOCK_DRIFT_ANOMALY)
            warnings.add("Clock Drift Anomaly: Excessive drift rate (${String.format(Locale.US, "%.1f", drift)} ns/s) detected.")
            logs.add(
                IntelligenceLogEntry(
                    severity = "ALERT",
                    tag = "CLOCK_MONITOR",
                    message = "Clock drift exceeds receiver oscillator tolerance. Possible time-spoofing injection."
                )
            )
        }

        if (discontinuities > 1) {
            score -= 20f
            logs.add(
                IntelligenceLogEntry(
                    severity = "WARNING",
                    tag = "CLOCK_MONITOR",
                    message = "Hardware clock discontinuity count: $discontinuities."
                )
            )
        }

        logs.add(
            IntelligenceLogEntry(
                severity = "INFO",
                tag = "CLOCK_EVAL",
                message = "Clock Stability: Drift = ${if (drift != null) String.format(Locale.US, "%.1f", drift) else "N/A"} ns/s."
            )
        )

        return Pair(score.coerceIn(0f, 100f), logs)
    }

    private fun evaluateOsnmaAuthentication(
        status: GnssStatusData,
        osnmaOverride: OsnmaStatus?,
        threats: MutableList<ThreatType>,
        warnings: MutableList<String>
    ): Triple<OsnmaStatus, Float, List<IntelligenceLogEntry>> {
        val logs = mutableListOf<IntelligenceLogEntry>()
        val galileoCount = status.satellites.count { it.constellation == ConstellationType.GALILEO }

        val computedStatus = if (status.isMock) {
            if (osnmaOverride == OsnmaStatus.SIMULATED_FAILURE) {
                OsnmaStatus.SIMULATED_FAILURE
            } else {
                OsnmaStatus.SIMULATED
            }
        } else {
            osnmaOverride ?: OsnmaStatus.NOT_AVAILABLE
        }

        val score = when (computedStatus) {
            OsnmaStatus.VERIFIED -> 100f
            OsnmaStatus.UNAUTHENTICATED -> 75f
            OsnmaStatus.FAILED -> {
                warnings.add("Galileo OSNMA cryptographic verification FAILED - potential signal tampering!")
                threats.add(ThreatType.UNAUTHENTICATED_SIGNAL)
                0f
            }
            OsnmaStatus.SIMULATED_FAILURE -> 0f
            OsnmaStatus.SIMULATED, OsnmaStatus.NOT_AVAILABLE -> 75f
        }

        logs.add(
            IntelligenceLogEntry(
                severity = if (computedStatus == OsnmaStatus.FAILED) "ALERT" else "INFO",
                tag = "OSNMA_AUTH",
                message = "Galileo OSNMA Status: ${computedStatus.label} ($galileoCount Galileo sats tracked)."
            )
        )

        return Triple(computedStatus, score, logs)
    }
}
