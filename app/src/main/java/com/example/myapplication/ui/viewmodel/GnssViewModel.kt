package com.example.myapplication.ui.viewmodel

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import com.example.myapplication.data.gnss.model.GnssMeasurementsData
import com.example.myapplication.data.gnss.model.GnssStatusData
import com.example.myapplication.data.gnss.model.LocationData
import com.example.myapplication.data.gnss.model.ThreatScenario
import com.example.myapplication.data.gnss.model.TrustReport
import com.example.myapplication.data.gnss.repository.LocationGnssManager
import kotlinx.coroutines.flow.StateFlow

class GnssViewModel(application: Application) : AndroidViewModel(application) {

    val gnssManager = LocationGnssManager(application)

    val gnssStatusState: StateFlow<GnssStatusData> = gnssManager.gnssStatusData
    val gnssMeasurementsState: StateFlow<GnssMeasurementsData> = gnssManager.gnssMeasurementsData
    val locationState: StateFlow<LocationData> = gnssManager.locationData
    val trustReportState: StateFlow<TrustReport> = gnssManager.trustReportData
    val activeScenarioState: StateFlow<ThreatScenario> = gnssManager.activeScenario
    val isMockMode: StateFlow<Boolean> = gnssManager.isMockMode
    val isMonitoringActive: StateFlow<Boolean> = gnssManager.isMonitoringActive
    val permissionGranted: StateFlow<Boolean> = gnssManager.permissionGranted

    init {
        gnssManager.startMonitoring()
    }

    fun startMonitoring() {
        gnssManager.startMonitoring()
    }

    fun stopMonitoring() {
        gnssManager.stopMonitoring()
    }

    fun toggleMockMode(forceMock: Boolean) {
        gnssManager.setForceMockMode(forceMock)
    }

    fun selectThreatScenario(scenario: ThreatScenario) {
        gnssManager.setThreatScenario(scenario)
    }

    fun recalibrate() {
        gnssManager.reevaluateTrust()
    }

    fun refreshPermissions() {
        gnssManager.hasLocationPermission()
    }

    override fun onCleared() {
        gnssManager.stopMonitoring()
    }
}
