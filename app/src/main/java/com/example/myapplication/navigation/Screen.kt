package com.example.myapplication.navigation

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Dashboard
import androidx.compose.material.icons.rounded.SatelliteAlt
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.ui.graphics.vector.ImageVector

sealed class Screen(
    val route: String,
    val title: String,
    val icon: ImageVector,
    val subtitle: String
) {
    data object Dashboard : Screen(
        route = "dashboard",
        title = "Dashboard",
        icon = Icons.Rounded.Dashboard,
        subtitle = "Trustability Overview"
    )

    data object SignalMonitor : Screen(
        route = "signal_monitor",
        title = "Signal Monitor",
        icon = Icons.Rounded.SatelliteAlt,
        subtitle = "Constellations & C/N0"
    )

    data object ThreatAnalysis : Screen(
        route = "threat_analysis",
        title = "Threat Analysis",
        icon = Icons.Rounded.Shield,
        subtitle = "Spoofing & Jamming"
    )

    data object Settings : Screen(
        route = "settings",
        title = "Settings & Logs",
        icon = Icons.Rounded.Settings,
        subtitle = "Preferences & Diagnostics"
    )

    companion object {
        val items = listOf(Dashboard, SignalMonitor, ThreatAnalysis, Settings)
    }
}
