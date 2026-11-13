package com.example.myapplication

import com.example.myapplication.data.gnss.engine.TrustScoringEngine
import com.example.myapplication.data.gnss.mock.MockGnssGenerator
import com.example.myapplication.data.gnss.model.ConstellationType
import com.example.myapplication.data.gnss.model.GnssMeasurementDetail
import com.example.myapplication.data.gnss.model.GnssMeasurementsData
import com.example.myapplication.data.gnss.model.GnssStatusData
import com.example.myapplication.data.gnss.model.LocationData
import com.example.myapplication.data.gnss.model.OsnmaStatus
import com.example.myapplication.data.gnss.model.SatelliteDetail
import com.example.myapplication.data.gnss.model.ThreatScenario
import com.example.myapplication.data.gnss.model.ThreatType
import com.example.myapplication.data.gnss.model.TrustLevel
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TrustScoringEngineTest {

    private val engine = TrustScoringEngine()
    private val generator = MockGnssGenerator()

    @Test
    fun testNominalSignalTrustScoring() = runBlocking {
        generator.setScenario(ThreatScenario.NOMINAL)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = generator.createLocationFlow().first()

        val report = engine.evaluateTrust(status, measurements, location)

        assertNotNull(report)
        assertEquals(TrustLevel.HIGH_TRUST, report.trustLevel)
        assertTrue("Overall score should be >= 80%", report.overallScore >= 80f)
        assertTrue("No threats should be detected in nominal scenario", report.threatsDetected.isEmpty())
        assertTrue("Active warnings should be empty", report.activeThreatWarnings.isEmpty())
        assertEquals(OsnmaStatus.SIMULATED, report.osnmaStatus)
        assertEquals(
            OsnmaStatus.NOT_AVAILABLE,
            engine.evaluateTrust(status.copy(isMock = false), measurements, location.copy(isMock = false)).osnmaStatus
        )
        assertTrue("Intelligence logs should be generated", report.intelligenceLogs.isNotEmpty())
    }

    @Test
    fun testSpoofingSimulatorDetection() = runBlocking {
        generator.setScenario(ThreatScenario.SPOOFING_SIMULATOR)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = generator.createLocationFlow().first()

        val report = engine.evaluateTrust(status, measurements, location)

        assertNotNull(report)
        assertTrue("Should detect spoofing simulator threat", report.threatsDetected.contains(ThreatType.SPOOFING_SIMULATOR_DETECTED))
        assertTrue("Overall score should be reduced (< 65%)", report.overallScore < 65f)
        assertTrue("Signal authenticity subscore should drop", report.subScores.signalAuthenticityScore < 50f)
        assertTrue("Active warnings should mention spoofing", report.activeThreatWarnings.any { it.contains("uniform C/N0", ignoreCase = true) || it.contains("high C/N0", ignoreCase = true) })
    }

    @Test
    fun testRfJammingDetection() = runBlocking {
        generator.setScenario(ThreatScenario.RF_JAMMING)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = generator.createLocationFlow().first()

        val report = engine.evaluateTrust(status, measurements, location)

        assertNotNull(report)
        assertTrue("Should detect RF jamming threat", report.threatsDetected.contains(ThreatType.RF_JAMMING_ELEVATED))
        assertTrue("AGC subscore should drop under jamming", report.subScores.agcInterferenceScore < 50f)
        assertTrue("Warnings should mention AGC or RF interference", report.activeThreatWarnings.any { it.contains("AGC", ignoreCase = true) || it.contains("Interference", ignoreCase = true) })
    }

    @Test
    fun testClockDriftAnomalyDetection() = runBlocking {
        generator.setScenario(ThreatScenario.CLOCK_DRIFT_ATTACK)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = generator.createLocationFlow().first()

        val report = engine.evaluateTrust(status, measurements, location)

        assertNotNull(report)
        assertTrue("Should detect clock drift threat", report.threatsDetected.contains(ThreatType.CLOCK_DRIFT_ANOMALY))
        assertTrue("Clock stability subscore should drop", report.subScores.clockStabilityScore < 60f)
    }

    @Test
    fun testOsnmaAuthenticationFailure() = runBlocking {
        generator.setScenario(ThreatScenario.NOMINAL)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = generator.createLocationFlow().first()

        val report = engine.evaluateTrust(
            gnssStatus = status,
            gnssMeasurements = measurements,
            location = location,
            osnmaOverride = OsnmaStatus.SIMULATED_FAILURE
        )

        assertNotNull(report)
        assertEquals(OsnmaStatus.SIMULATED_FAILURE, report.osnmaStatus)
        assertFalse(report.threatsDetected.contains(ThreatType.UNAUTHENTICATED_SIGNAL))
        assertEquals(0f, report.subScores.osnmaAuthScore, 0.01f)
    }

    @Test
    fun testConstellationDiversityScoring() {
        // Single constellation test
        val singleConstSats = listOf(
            SatelliteDetail(svid = 1, constellation = ConstellationType.GPS, cn0DbHz = 40f, elevationDegrees = 45f, azimuthDegrees = 90f, usedInFix = true),
            SatelliteDetail(svid = 2, constellation = ConstellationType.GPS, cn0DbHz = 38f, elevationDegrees = 30f, azimuthDegrees = 180f, usedInFix = true),
            SatelliteDetail(svid = 3, constellation = ConstellationType.GPS, cn0DbHz = 42f, elevationDegrees = 60f, azimuthDegrees = 270f, usedInFix = true),
            SatelliteDetail(svid = 4, constellation = ConstellationType.GPS, cn0DbHz = 35f, elevationDegrees = 20f, azimuthDegrees = 310f, usedInFix = true),
            SatelliteDetail(svid = 5, constellation = ConstellationType.GPS, cn0DbHz = 39f, elevationDegrees = 50f, azimuthDegrees = 40f, usedInFix = true),
            SatelliteDetail(svid = 6, constellation = ConstellationType.GPS, cn0DbHz = 41f, elevationDegrees = 55f, azimuthDegrees = 120f, usedInFix = true),
            SatelliteDetail(svid = 7, constellation = ConstellationType.GPS, cn0DbHz = 37f, elevationDegrees = 25f, azimuthDegrees = 220f, usedInFix = true)
        )

        val status = GnssStatusData(satellites = singleConstSats)
        val measurements = GnssMeasurementsData()
        val location = LocationData()

        val report = engine.evaluateTrust(status, measurements, location)

        assertEquals(40f, report.subScores.constellationDiversityScore, 0.01f)
        assertTrue("Should flag constellation spoofing or single constellation dependency", report.threatsDetected.contains(ThreatType.CONSTELLATION_SPOOFING))
    }

    @Test
    fun testWellDispersedDopMatchesAnalyticReference() {
        val horizon = cardinalRing(0f)
        val zenith = horizon.first().copy(
            constellation = ConstellationType.GALILEO, elevationDegrees = 90f
        )
        // Four cardinal horizon rays and one at zenith give HDOP = 1, VDOP = sqrt(5/4).
        val report = evaluateGeometry(horizon + zenith)

        assertEquals(1.0f, requireNotNull(report.estimatedHdop), 1e-6f)
        assertEquals(1.118034f, requireNotNull(report.estimatedVdop), 1e-6f)
        assertEquals(100f, report.subScores.geometryDopScore, 0f)
        assertFalse(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
    }

    @Test
    fun testAsymmetricDopMatchesFullMatrixReferenceRegardlessOfOrder() {
        val angles = listOf(75f to 45f, 30f to 135f, 40f to 225f, 50f to 315f)
        val satellites = cardinalRing(0f).zip(angles) { satellite, (elevation, azimuth) ->
            satellite.copy(elevationDegrees = elevation, azimuthDegrees = azimuth)
        }

        // Reference values from independently inverting the full normal matrix.
        for (order in listOf(satellites, satellites.reversed())) {
            val report = evaluateGeometry(order)
            assertEquals(2.091518f, requireNotNull(report.estimatedHdop), 1e-5f)
            assertEquals(4.596476f, requireNotNull(report.estimatedVdop), 1e-5f)
        }
    }

    @Test
    fun testPoorVerticalDopIsReportedEvenWithGoodHorizontalGeometry() {
        val report = evaluateGeometry(cardinalRing(30f) + cardinalRing(31f, firstSvid = 5))

        // Two cardinal rings cancel the horizontal cross terms. Their small separation
        // in elevation makes vertical position and receiver clock hard to separate.
        assertEquals(0.820683f, requireNotNull(report.estimatedHdop), 1e-5f)
        assertEquals(47.0211f, requireNotNull(report.estimatedVdop), 1e-3f)
        assertEquals(35f, report.subScores.geometryDopScore, 0f)
        assertTrue(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
    }

    @Test
    fun testPoorHorizontalDopIsNotClamped() {
        val report = evaluateGeometry(cardinalRing(80f) + cardinalRing(85f, firstSvid = 5))

        assertEquals(5.146863f, requireNotNull(report.estimatedHdop), 1e-5f)
        assertEquals(62.0980f, requireNotNull(report.estimatedVdop), 1e-3f)
        assertEquals(35f, report.subScores.geometryDopScore, 0f)
        assertTrue(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
    }

    @Test
    fun testEqualElevationIsSingularEvenWithAnUnusedSatelliteAtZenith() {
        val ring = cardinalRing(30f)
        val unused = ring.first().copy(svid = 5, elevationDegrees = 90f, usedInFix = false)
        val report = evaluateGeometry(ring + unused)

        assertNull(report.estimatedHdop)
        assertNull(report.estimatedVdop)
        assertEquals(35f, report.subScores.geometryDopScore, 0f)
        assertTrue(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
        assertTrue(report.activeThreatWarnings.any { it.contains("receiver clock") })
    }

    @Test
    fun testNearlyCollinearAzimuthsDoNotYieldAUsableDop() {
        val clustered = cardinalRing(30f).mapIndexed { index, sat ->
            sat.copy(azimuthDegrees = index * 1e-8f, elevationDegrees = 20f + 10f * index)
        }
        val report = evaluateGeometry(clustered)

        assertNull(report.estimatedHdop)
        assertNull(report.estimatedVdop)
        assertTrue(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
    }

    @Test
    fun testDuplicateSignalsAndInvalidAnglesDoNotMakeFourUsableSatellites() {
        val three = cardinalRing(30f).take(3)
        val extraSignal = three.first().copy(carrierFrequencyHz = 1176.45e6)
        val invalid = three.first().copy(svid = 4, azimuthDegrees = Float.NaN)
        val report = evaluateGeometry(three + extraSignal + invalid)

        assertNull(report.estimatedHdop)
        assertNull(report.estimatedVdop)
        assertEquals(40f, report.subScores.geometryDopScore, 0f)
        assertFalse(report.threatsDetected.contains(ThreatType.POOR_GEOMETRY_WARNING))
        assertTrue(report.intelligenceLogs.any { it.tag == "DOP_EVAL" && it.severity == "WARNING" })
    }

    private fun cardinalRing(elevation: Float, firstSvid: Int = 1): List<SatelliteDetail> =
        listOf(0f, 90f, 180f, 270f).mapIndexed { index, azimuth ->
            SatelliteDetail(
                svid = firstSvid + index,
                constellation = ConstellationType.GPS,
                cn0DbHz = 36f + index,
                elevationDegrees = elevation,
                azimuthDegrees = azimuth,
                usedInFix = true
            )
        }

    private fun evaluateGeometry(satellites: List<SatelliteDetail>) =
        engine.evaluateTrust(GnssStatusData(satellites = satellites), GnssMeasurementsData(), LocationData())

    @Test
    fun testEvaluationLogReportsMediumTrustWithoutClaimingAnomaly() {
        val status = GnssStatusData(satellites = listOf(SatelliteDetail(
            svid = 1, constellation = ConstellationType.GPS, cn0DbHz = 22f,
            elevationDegrees = 45f, azimuthDegrees = 90f, usedInFix = true
        )))
        val measurements = GnssMeasurementsData(measurements = listOf(GnssMeasurementDetail(
            svid = 1, constellation = ConstellationType.GPS, state = 0,
            receivedSvTimeNanos = 1L, cn0DbHz = 22.0
        )))
        val report = engine.evaluateTrust(status, measurements, LocationData())
        val log = report.intelligenceLogs.single { it.tag == "ENGINE_EVAL" }

        assertTrue(report.threatsDetected.isEmpty())
        assertEquals(TrustLevel.MEDIUM_TRUST, report.trustLevel)
        assertEquals("WARNING", log.severity)
        assertTrue(log.message.contains("No anomaly triggers detected."))
        assertTrue(log.message.contains("Calculated trust level MEDIUM TRUST"))
    }

    @Test
    fun testEvaluationLogMarksMissingDataAsProvisional() = runBlocking {
        generator.setScenario(ThreatScenario.NOMINAL)
        val status = generator.createGnssStatusFlow().first()
        val measurements = generator.createGnssMeasurementsFlow().first()
        val location = LocationData()

        val reports = listOf(
            engine.evaluateTrust(GnssStatusData(), GnssMeasurementsData(), location),
            engine.evaluateTrust(GnssStatusData(), measurements, location),
            engine.evaluateTrust(status, GnssMeasurementsData(), location)
        )
        for (report in reports) {
            val log = report.intelligenceLogs.single { it.tag == "ENGINE_EVAL" }
            assertEquals("WARNING", log.severity)
            assertTrue(log.message.contains("Insufficient GNSS data"))
            assertTrue(log.message.contains("Provisional trust level ${report.trustLevel.name.replace('_', ' ')}"))
        }
    }

    @Test
    fun testEvaluationLogDistinguishesNominalAndDetectedAnomalies() = runBlocking {
        generator.setScenario(ThreatScenario.NOMINAL)
        val nominal = engine.evaluateTrust(
            generator.createGnssStatusFlow().first(),
            generator.createGnssMeasurementsFlow().first(),
            generator.createLocationFlow().first()
        ).intelligenceLogs.single { it.tag == "ENGINE_EVAL" }
        assertEquals("INFO", nominal.severity)
        assertTrue(nominal.message.contains("No anomaly triggers detected."))
        assertTrue(nominal.message.contains("Calculated trust level HIGH TRUST"))

        generator.setScenario(ThreatScenario.SPOOFING_SIMULATOR)
        val spoofing = engine.evaluateTrust(
            generator.createGnssStatusFlow().first(),
            generator.createGnssMeasurementsFlow().first(),
            generator.createLocationFlow().first()
        ).intelligenceLogs.single { it.tag == "ENGINE_EVAL" }
        assertEquals("ALERT", spoofing.severity)
        assertTrue(spoofing.message.contains("anomaly trigger(s) detected."))
        assertTrue(spoofing.message.contains("Calculated trust level LOW TRUST"))
    }
}
