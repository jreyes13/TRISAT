package com.example.myapplication.data.gnss.repository

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.GnssMeasurementsEvent
import android.location.GnssStatus
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import androidx.core.content.ContextCompat
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
import com.example.myapplication.data.gnss.model.TrustReport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.util.concurrent.Executor

class LocationGnssManager(private val context: Context) {

    private val locationManager =
        context.getSystemService(Context.LOCATION_SERVICE) as LocationManager

    private val mockGnssGenerator = MockGnssGenerator()
    private val trustScoringEngine = TrustScoringEngine()
    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())

    private val _gnssStatusData = MutableStateFlow(GnssStatusData())
    val gnssStatusData: StateFlow<GnssStatusData> = _gnssStatusData.asStateFlow()

    private val _gnssMeasurementsData = MutableStateFlow(GnssMeasurementsData())
    val gnssMeasurementsData: StateFlow<GnssMeasurementsData> = _gnssMeasurementsData.asStateFlow()

    private val _locationData = MutableStateFlow(LocationData())
    val locationData: StateFlow<LocationData> = _locationData.asStateFlow()

    private val _trustReportData = MutableStateFlow(TrustReport())
    val trustReportData: StateFlow<TrustReport> = _trustReportData.asStateFlow()

    private val _activeScenario = MutableStateFlow(ThreatScenario.NOMINAL)
    val activeScenario: StateFlow<ThreatScenario> = _activeScenario.asStateFlow()

    private val _isMockMode = MutableStateFlow(false)
    val isMockMode: StateFlow<Boolean> = _isMockMode.asStateFlow()

    private val _isMonitoringActive = MutableStateFlow(false)
    val isMonitoringActive: StateFlow<Boolean> = _isMonitoringActive.asStateFlow()

    private val _permissionGranted = MutableStateFlow(false)
    val permissionGranted: StateFlow<Boolean> = _permissionGranted.asStateFlow()

    private var forceMockMode = false
    private var mockJob: Job? = null
    private var hardwareCheckJob: Job? = null

    private var lastHardwareEventTime = 0L

    private val mainExecutor: Executor by lazy {
        ContextCompat.getMainExecutor(context)
    }

    private val handler by lazy {
        Handler(Looper.getMainLooper())
    }

    // Hardware Callbacks
    private val gnssStatusCallback = object : GnssStatus.Callback() {
        override fun onSatelliteStatusChanged(status: GnssStatus) {
            lastHardwareEventTime = System.currentTimeMillis()
            val count = status.satelliteCount
            val satList = mutableListOf<SatelliteDetail>()

            for (i in 0 until count) {
                val constType = ConstellationType.fromGnssStatusConstellation(status.getConstellationType(i))
                val svid = status.getSvid(i)
                val cn0 = status.getCn0DbHz(i)
                val elev = status.getElevationDegrees(i)
                val azim = status.getAzimuthDegrees(i)
                val used = status.usedInFix(i)
                val hasAlmanac = status.hasAlmanacData(i)
                val hasEphemeris = status.hasEphemerisData(i)

                var carrierFreqHz = 0.0
                var hasCarrierFreq = false
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && status.hasCarrierFrequencyHz(i)) {
                    carrierFreqHz = status.getCarrierFrequencyHz(i).toDouble()
                    hasCarrierFreq = true
                }

                var basebandCn0 = 0f
                var hasBasebandCn0 = false
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && status.hasBasebandCn0DbHz(i)) {
                    basebandCn0 = status.getBasebandCn0DbHz(i)
                    hasBasebandCn0 = true
                }

                satList.add(
                    SatelliteDetail(
                        svid = svid,
                        constellation = constType,
                        cn0DbHz = cn0,
                        elevationDegrees = elev,
                        azimuthDegrees = azim,
                        carrierFrequencyHz = carrierFreqHz,
                        hasCarrierFrequency = hasCarrierFreq,
                        usedInFix = used,
                        hasAlmanacData = hasAlmanac,
                        hasEphemerisData = hasEphemeris,
                        hasBasebandCn0 = hasBasebandCn0,
                        basebandCn0DbHz = basebandCn0
                    )
                )
            }

            if (!forceMockMode && satList.isNotEmpty()) {
                stopMockStream()
                _isMockMode.value = false
                val newStatus = GnssStatusData(
                    satellites = satList,
                    isMock = false,
                    timestampMillis = System.currentTimeMillis()
                )
                _gnssStatusData.value = newStatus
                reevaluateTrust(newStatus = newStatus)
            }
        }
    }

    private val gnssMeasurementsCallback = object : GnssMeasurementsEvent.Callback() {
        override fun onGnssMeasurementsReceived(event: GnssMeasurementsEvent) {
            lastHardwareEventTime = System.currentTimeMillis()
            val clock = event.clock
            val clockTimeNanos = clock.timeNanos
            val discontinuity = clock.hardwareClockDiscontinuityCount
            val fullBias = if (clock.hasFullBiasNanos()) clock.fullBiasNanos else null
            val bias = if (clock.hasBiasNanos()) clock.biasNanos else null
            val biasUncertainty = if (clock.hasBiasUncertaintyNanos()) clock.biasUncertaintyNanos else null
            val drift = if (clock.hasDriftNanosPerSecond()) clock.driftNanosPerSecond else null
            val driftUncertainty = if (clock.hasDriftUncertaintyNanosPerSecond()) clock.driftUncertaintyNanosPerSecond else null

            val measList = mutableListOf<GnssMeasurementDetail>()
            for (m in event.measurements) {
                val constType = ConstellationType.fromGnssStatusConstellation(m.constellationType)
                var carrierFreqHz = 0f
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && m.hasCarrierFrequencyHz()) {
                    carrierFreqHz = m.carrierFrequencyHz
                }

                var agc = 0.0
                var hasAgc = false
                @Suppress("DEPRECATION")
                val hasAgcLevel = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && m.hasAutomaticGainControlLevelDb()
                if (hasAgcLevel) {
                    @Suppress("DEPRECATION")
                    agc = m.automaticGainControlLevelDb
                    hasAgc = true
                }

                measList.add(
                    GnssMeasurementDetail(
                        svid = m.svid,
                        constellation = constType,
                        state = m.state,
                        receivedSvTimeNanos = m.receivedSvTimeNanos,
                        cn0DbHz = m.cn0DbHz,
                        pseudorangeRateMetersPerSecond = m.pseudorangeRateMetersPerSecond,
                        pseudorangeRateUncertaintyMetersPerSecond = m.pseudorangeRateUncertaintyMetersPerSecond,
                        accumulatedDeltaRangeState = m.accumulatedDeltaRangeState,
                        accumulatedDeltaRangeMeters = m.accumulatedDeltaRangeMeters,
                        carrierFrequencyHz = carrierFreqHz,
                        automaticGainControlLevelDb = agc,
                        hasAgc = hasAgc,
                        multipathIndicator = m.multipathIndicator,
                        snrInDb = if (m.hasSnrInDb()) m.snrInDb else 0.0
                    )
                )
            }

            if (!forceMockMode && measList.isNotEmpty()) {
                val newMeasurements = GnssMeasurementsData(
                    clockTimeNanos = clockTimeNanos,
                    clockHardwareClockDiscontinuityCount = discontinuity,
                    fullBiasNanos = fullBias,
                    biasNanos = bias,
                    biasUncertaintyNanos = biasUncertainty,
                    driftNanosPerSecond = drift,
                    driftUncertaintyNanosPerSecond = driftUncertainty,
                    measurements = measList,
                    isMock = false,
                    timestampMillis = System.currentTimeMillis()
                )
                _gnssMeasurementsData.value = newMeasurements
                reevaluateTrust(newMeasurements = newMeasurements)
            }
        }
    }

    private val locationListener = object : LocationListener {
        override fun onLocationChanged(location: Location) {
            lastHardwareEventTime = System.currentTimeMillis()
            val isMockLocation = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                location.isMock
            } else {
                @Suppress("DEPRECATION")
                location.isFromMockProvider
            }

            val locData = LocationData(
                latitude = location.latitude,
                longitude = location.longitude,
                altitude = location.altitude,
                speed = location.speed,
                bearing = location.bearing,
                accuracy = location.accuracy,
                verticalAccuracyMeters = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && location.hasVerticalAccuracy()) location.verticalAccuracyMeters else 0f,
                speedAccuracyMetersPerSecond = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && location.hasSpeedAccuracy()) location.speedAccuracyMetersPerSecond else 0f,
                bearingAccuracyDegrees = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && location.hasBearingAccuracy()) location.bearingAccuracyDegrees else 0f,
                elapsedRealtimeNanos = location.elapsedRealtimeNanos,
                timestampMillis = location.time,
                provider = location.provider ?: "gps",
                isMock = isMockLocation,
                hasFix = true
            )

            if (!forceMockMode) {
                _locationData.value = locData
                reevaluateTrust(newLocation = locData)
            }
        }
    }

    fun setThreatScenario(scenario: ThreatScenario) {
        _activeScenario.value = scenario
        mockGnssGenerator.setScenario(scenario)
        reevaluateTrust()
    }

    fun reevaluateTrust(
        newStatus: GnssStatusData = _gnssStatusData.value,
        newMeasurements: GnssMeasurementsData = _gnssMeasurementsData.value,
        newLocation: LocationData = _locationData.value
    ) {
        val osnmaOverride = if (_activeScenario.value == ThreatScenario.OSNMA_FAILURE) {
            OsnmaStatus.FAILED
        } else null

        val report = trustScoringEngine.evaluateTrust(
            gnssStatus = newStatus,
            gnssMeasurements = newMeasurements,
            location = newLocation,
            osnmaOverride = osnmaOverride
        )
        _trustReportData.value = report
    }

    fun hasLocationPermission(): Boolean {
        val finePermission = ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED

        val coarsePermission = ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.ACCESS_COARSE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED

        val granted = finePermission || coarsePermission
        _permissionGranted.value = granted
        return granted
    }

    fun startMonitoring() {
        if (_isMonitoringActive.value) return
        _isMonitoringActive.value = true

        val permissionOk = hasLocationPermission()

        if (!permissionOk || forceMockMode) {
            startMockStream()
            return
        }

        try {
            registerHardwareCallbacks()
            scheduleHardwareCheckTimer()
        } catch (_: SecurityException) {
            _permissionGranted.value = false
            startMockStream()
        } catch (_: Exception) {
            startMockStream()
        }
    }

    fun stopMonitoring() {
        _isMonitoringActive.value = false
        unregisterHardwareCallbacks()
        stopMockStream()
        hardwareCheckJob?.cancel()
    }

    fun setForceMockMode(enabled: Boolean) {
        forceMockMode = enabled
        if (enabled) {
            unregisterHardwareCallbacks()
            startMockStream()
        } else {
            stopMockStream()
            if (_isMonitoringActive.value) {
                if (hasLocationPermission()) {
                    try {
                        registerHardwareCallbacks()
                        scheduleHardwareCheckTimer()
                    } catch (_: Exception) {
                        startMockStream()
                    }
                } else {
                    startMockStream()
                }
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun registerHardwareCallbacks() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            locationManager.registerGnssStatusCallback(mainExecutor, gnssStatusCallback)
            locationManager.registerGnssMeasurementsCallback(mainExecutor, gnssMeasurementsCallback)
        } else {
            @Suppress("DEPRECATION")
            locationManager.registerGnssStatusCallback(gnssStatusCallback, handler)
            @Suppress("DEPRECATION")
            locationManager.registerGnssMeasurementsCallback(gnssMeasurementsCallback, handler)
        }

        if (locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER)) {
            locationManager.requestLocationUpdates(
                LocationManager.GPS_PROVIDER,
                1000L,
                0f,
                locationListener,
                Looper.getMainLooper()
            )
        }

        if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
            locationManager.requestLocationUpdates(
                LocationManager.NETWORK_PROVIDER,
                2000L,
                0f,
                locationListener,
                Looper.getMainLooper()
            )
        }
    }

    private fun unregisterHardwareCallbacks() {
        try {
            locationManager.unregisterGnssStatusCallback(gnssStatusCallback)
            locationManager.unregisterGnssMeasurementsCallback(gnssMeasurementsCallback)
            locationManager.removeUpdates(locationListener)
        } catch (_: Exception) {
            // Ignore if already unregistered
        }
    }

    private fun scheduleHardwareCheckTimer() {
        hardwareCheckJob?.cancel()
        hardwareCheckJob = scope.launch {
            delay(3500L)
            // Only start mock stream if explicitly forced or if location permissions are missing
            if (forceMockMode || !hasLocationPermission()) {
                startMockStream()
            }
        }
    }

    private fun startMockStream() {
        if (mockJob?.isActive == true) return
        _isMockMode.value = true

        mockJob = scope.launch {
            launch {
                mockGnssGenerator.createGnssStatusFlow().collect { status ->
                    if (_isMockMode.value || forceMockMode) {
                        _gnssStatusData.value = status
                        reevaluateTrust(newStatus = status)
                    }
                }
            }
            launch {
                mockGnssGenerator.createGnssMeasurementsFlow().collect { measurements ->
                    if (_isMockMode.value || forceMockMode) {
                        _gnssMeasurementsData.value = measurements
                        reevaluateTrust(newMeasurements = measurements)
                    }
                }
            }
            launch {
                mockGnssGenerator.createLocationFlow().collect { location ->
                    if (_isMockMode.value || forceMockMode) {
                        _locationData.value = location
                        reevaluateTrust(newLocation = location)
                    }
                }
            }
        }
    }

    private fun stopMockStream() {
        mockJob?.cancel()
        mockJob = null
    }
}
