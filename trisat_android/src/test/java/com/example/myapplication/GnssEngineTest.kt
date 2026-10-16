package com.example.myapplication

import com.example.myapplication.data.gnss.mock.MockGnssGenerator
import com.example.myapplication.data.gnss.model.ConstellationType
import com.example.myapplication.data.gnss.model.GnssMeasurementDetail
import com.example.myapplication.data.gnss.model.GnssMeasurementsData
import com.example.myapplication.data.gnss.model.GnssStatusData
import com.example.myapplication.data.gnss.model.SatelliteDetail
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GnssEngineTest {

    @Test
    fun testMockGnssGeneratorFlows() = runBlocking {
        val generator = MockGnssGenerator()

        val statusData = generator.createGnssStatusFlow().first()
        assertNotNull(statusData)
        assertTrue(statusData.isMock)
        assertTrue(statusData.satellites.isNotEmpty())
        assertTrue(statusData.satelliteCount > 0)
        assertTrue(statusData.usedInFixCount > 0)

        val measurementsData = generator.createGnssMeasurementsFlow().first()
        assertNotNull(measurementsData)
        assertTrue(measurementsData.isMock)
        assertTrue(measurementsData.measurements.isNotEmpty())

        val locationData = generator.createLocationFlow().first()
        assertNotNull(locationData)
        assertTrue(locationData.isMock)
        assertTrue(locationData.hasFix)
        assertEquals("mock_gnss", locationData.provider)
    }

    @Test
    fun testGnssStatusDataComputations() {
        val sats = listOf(
            SatelliteDetail(
                svid = 1,
                constellation = ConstellationType.GPS,
                cn0DbHz = 40.0f,
                elevationDegrees = 45f,
                azimuthDegrees = 90f,
                usedInFix = true
            ),
            SatelliteDetail(
                svid = 2,
                constellation = ConstellationType.GPS,
                cn0DbHz = 30.0f,
                elevationDegrees = 20f,
                azimuthDegrees = 180f,
                usedInFix = false
            ),
            SatelliteDetail(
                svid = 5,
                constellation = ConstellationType.GALILEO,
                cn0DbHz = 35.0f,
                elevationDegrees = 60f,
                azimuthDegrees = 270f,
                usedInFix = true
            )
        )

        val status = GnssStatusData(satellites = sats)
        assertEquals(3, status.satelliteCount)
        assertEquals(2, status.usedInFixCount)
        assertEquals(35.0f, status.averageCn0DbHz, 0.01f)
        assertEquals(2, status.constellationCounts[ConstellationType.GPS])
        assertEquals(1, status.constellationCounts[ConstellationType.GALILEO])
    }

    @Test
    fun testGnssMeasurementsDataComputations() {
        val meas = listOf(
            GnssMeasurementDetail(
                svid = 1,
                constellation = ConstellationType.GPS,
                state = 1,
                receivedSvTimeNanos = 1000L,
                cn0DbHz = 40.0,
                pseudorangeRateMetersPerSecond = -10.0,
                pseudorangeRateUncertaintyMetersPerSecond = 0.1,
                automaticGainControlLevelDb = -1.0,
                hasAgc = true
            ),
            GnssMeasurementDetail(
                svid = 2,
                constellation = ConstellationType.GPS,
                state = 1,
                receivedSvTimeNanos = 1000L,
                cn0DbHz = 30.0,
                pseudorangeRateMetersPerSecond = -10.0,
                pseudorangeRateUncertaintyMetersPerSecond = 0.1,
                automaticGainControlLevelDb = -3.0,
                hasAgc = true
            )
        )

        val measurementsData = GnssMeasurementsData(measurements = meas)
        assertEquals(-2.0, measurementsData.averageAgcDb, 0.01)
    }

    @Test
    fun testConstellationTypeMapping() {
        assertEquals(ConstellationType.GPS, ConstellationType.fromString("GPS"))
        assertEquals(ConstellationType.GALILEO, ConstellationType.fromString("Galileo"))
        assertEquals(ConstellationType.GLONASS, ConstellationType.fromString("GLONASS"))
        assertEquals(ConstellationType.BEIDOU, ConstellationType.fromString("BEIDOU"))
        assertEquals(ConstellationType.QZSS, ConstellationType.fromString("qzss"))
        assertEquals(ConstellationType.UNKNOWN, ConstellationType.fromString("nonexistent"))
    }
}
