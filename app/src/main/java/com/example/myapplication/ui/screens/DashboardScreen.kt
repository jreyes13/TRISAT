package com.example.myapplication.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.BarChart
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.FilterList
import androidx.compose.material.icons.rounded.GppBad
import androidx.compose.material.icons.rounded.GppGood
import androidx.compose.material.icons.rounded.Key
import androidx.compose.material.icons.rounded.LocationOn
import androidx.compose.material.icons.rounded.Map
import androidx.compose.material.icons.rounded.Navigation
import androidx.compose.material.icons.rounded.Public
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.SatelliteAlt
import androidx.compose.material.icons.rounded.Security
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.material.icons.rounded.SignalCellular4Bar
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.example.myapplication.data.gnss.model.LocationData
import com.example.myapplication.data.gnss.model.ThreatScenario
import com.example.myapplication.data.gnss.model.ThreatType
import com.example.myapplication.data.gnss.model.TrustLevel
import com.example.myapplication.data.gnss.model.TrustSubScores
import com.example.myapplication.ui.components.StatusBadge
import com.example.myapplication.ui.components.StatusLevel
import com.example.myapplication.ui.components.TrustabilityGaugeMeter
import com.example.myapplication.ui.theme.TRISATTheme
import com.example.myapplication.ui.viewmodel.GnssViewModel
import java.util.Locale

@Composable
fun DashboardScreen(
    gnssViewModel: GnssViewModel = viewModel(),
    onNavigateToSignal: () -> Unit = {},
    onNavigateToThreats: () -> Unit = {}
) {
    val trustReport by gnssViewModel.trustReportState.collectAsState()
    val gnssStatus by gnssViewModel.gnssStatusState.collectAsState()
    val locationData by gnssViewModel.locationState.collectAsState()
    val activeScenario by gnssViewModel.activeScenarioState.collectAsState()

    val trustLevel = trustReport.trustLevel

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 16.dp),
        contentPadding = PaddingValues(top = 16.dp, bottom = 32.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        // 1. Main Trustability Gauge Meter Card
        item {
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(24.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceContainer
                ),
                elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(20.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                imageVector = Icons.Rounded.Security,
                                contentDescription = "Trustability Engine",
                                tint = when (trustLevel) {
                                    TrustLevel.HIGH_TRUST -> MaterialTheme.colorScheme.tertiary
                                    TrustLevel.MEDIUM_TRUST -> MaterialTheme.colorScheme.secondary
                                    TrustLevel.LOW_TRUST -> MaterialTheme.colorScheme.error
                                },
                                modifier = Modifier.size(24.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(
                                text = "Trustability Overview",
                                style = MaterialTheme.typography.titleMedium,
                                fontWeight = FontWeight.Bold
                            )
                        }

                        val badgeText = when (trustLevel) {
                            TrustLevel.HIGH_TRUST -> "HIGH_TRUST"
                            TrustLevel.MEDIUM_TRUST -> "MEDIUM_TRUST"
                            TrustLevel.LOW_TRUST -> "THREAT_DETECTED"
                        }
                        val statusLevel = when (trustLevel) {
                            TrustLevel.HIGH_TRUST -> StatusLevel.SECURE
                            TrustLevel.MEDIUM_TRUST -> StatusLevel.WARNING
                            TrustLevel.LOW_TRUST -> StatusLevel.ALERT
                        }
                        StatusBadge(
                            text = badgeText,
                            level = statusLevel,
                            icon = if (trustLevel == TrustLevel.HIGH_TRUST) Icons.Rounded.CheckCircle else Icons.Rounded.Warning
                        )
                    }

                    Spacer(modifier = Modifier.height(16.dp))

                    // Gauge Meter
                    TrustabilityGaugeMeter(
                        score = trustReport.overallScore,
                        trustLevel = trustLevel
                    )

                    Spacer(modifier = Modifier.height(12.dp))

                    Text(
                        text = trustLevel.description,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(horizontal = 8.dp)
                    )
                }
            }
        }

        // 2. Active Threats Banner / Alert Cards
        if (trustReport.threatsDetected.isNotEmpty()) {
            item {
                Text(
                    text = "Active Threats Detected (${trustReport.threatsDetected.size})",
                    style = MaterialTheme.typography.titleSmall.copy(fontWeight = FontWeight.Bold),
                    color = MaterialTheme.colorScheme.error,
                    modifier = Modifier.padding(start = 4.dp)
                )
            }

            items(trustReport.threatsDetected) { threat ->
                DashboardThreatAlertCard(threat = threat, onClick = onNavigateToThreats)
            }
        }

        // 3. Interactive Threat Simulation Scenario Switcher Controls
        item {
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(20.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceContainer
                )
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(
                                imageVector = Icons.Rounded.FilterList,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(20.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(
                                text = "Threat Simulation & Testing",
                                style = MaterialTheme.typography.titleSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                        StatusBadge(
                            text = activeScenario.displayName.take(18),
                            level = if (activeScenario == ThreatScenario.NOMINAL) StatusLevel.SECURE else StatusLevel.WARNING
                        )
                    }

                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "Select a GNSS attack vector scenario to test TRISAT trust score evaluation interactively:",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    Spacer(modifier = Modifier.height(12.dp))

                    LazyRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        items(ThreatScenario.entries) { scenario ->
                            val isSelected = activeScenario == scenario
                            FilterChip(
                                selected = isSelected,
                                onClick = { gnssViewModel.selectThreatScenario(scenario) },
                                label = {
                                    Text(
                                        text = scenario.displayName,
                                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal
                                    )
                                },
                                leadingIcon = if (isSelected) {
                                    {
                                        Icon(
                                            imageVector = Icons.Rounded.CheckCircle,
                                            contentDescription = null,
                                            modifier = Modifier.size(16.dp)
                                        )
                                    }
                                } else null,
                                colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = if (scenario == ThreatScenario.NOMINAL) MaterialTheme.colorScheme.tertiaryContainer else MaterialTheme.colorScheme.errorContainer,
                                    selectedLabelColor = if (scenario == ThreatScenario.NOMINAL) MaterialTheme.colorScheme.onTertiaryContainer else MaterialTheme.colorScheme.onErrorContainer
                                )
                            )
                        }
                    }
                }
            }
        }

        // 4. Authenticated Earth Intelligence Location & Position Card
        item {
            Text(
                text = "Authenticated Earth Intelligence Metrics",
                style = MaterialTheme.typography.titleSmall.copy(fontWeight = FontWeight.Bold),
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(start = 4.dp, top = 4.dp)
            )
        }

        item {
            EarthIntelligenceLocationCard(
                location = locationData,
                satelliteCount = gnssStatus.satelliteCount,
                constellationCount = gnssStatus.constellationCounts.size,
                trustLevel = trustLevel
            )
        }

        // 5. Sub-Score Breakdown Grid / Cards (6 Core Metrics)
        item {
            Text(
                text = "Sub-Score Diagnostics Breakdown",
                style = MaterialTheme.typography.titleSmall.copy(fontWeight = FontWeight.Bold),
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(start = 4.dp, top = 4.dp)
            )
        }

        item {
            SubScoresOverviewGrid(subScores = trustReport.subScores)
        }

        // 6. Action & Recalibration Row
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Button(
                    onClick = { gnssViewModel.recalibrate() },
                    modifier = Modifier.weight(1f),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.primary
                    )
                ) {
                    Icon(
                        imageVector = Icons.Rounded.Refresh,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text("Recalibrate Engine")
                }

                OutlinedButton(
                    onClick = onNavigateToSignal,
                    modifier = Modifier.weight(1f)
                ) {
                    Icon(
                        imageVector = Icons.Rounded.BarChart,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text("Signal Monitor")
                }
            }
        }
    }
}

@Composable
fun DashboardThreatAlertCard(
    threat: ThreatType,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.errorContainer
        )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                imageVector = Icons.Rounded.GppBad,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onErrorContainer,
                modifier = Modifier.size(28.dp)
            )
            Spacer(modifier = Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = threat.title,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onErrorContainer
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = threat.description,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onErrorContainer
                )
            }
        }
    }
}

@Composable
fun EarthIntelligenceLocationCard(
    location: LocationData,
    satelliteCount: Int,
    constellationCount: Int,
    trustLevel: TrustLevel
) {
    val latStr = if (location.latitude >= 0) {
        String.format(Locale.US, "%.6f° N", location.latitude)
    } else {
        String.format(Locale.US, "%.6f° S", -location.latitude)
    }

    val lngStr = if (location.longitude >= 0) {
        String.format(Locale.US, "%.6f° E", location.longitude)
    } else {
        String.format(Locale.US, "%.6f° W", -location.longitude)
    }

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer
        )
    ) {
        Column(modifier = Modifier.padding(18.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = Icons.Rounded.Map,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(22.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "Authenticated Spatial Coordinates",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold
                    )
                }

                StatusBadge(
                    text = if (location.hasFix) "3D FIX LOCKED" else "NO FIX",
                    level = if (trustLevel == TrustLevel.HIGH_TRUST) StatusLevel.SECURE else StatusLevel.WARNING
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            // Main Lat/Lng Display
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = "LATITUDE",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        text = latStr,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                }

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = "LONGITUDE",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        text = lngStr,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                }
            }

            Spacer(modifier = Modifier.height(14.dp))

            // Details Row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                MetricPill(
                    label = "Altitude",
                    value = "${String.format(Locale.US, "%.1f", location.altitude)} m",
                    icon = Icons.Rounded.Navigation
                )
                MetricPill(
                    label = "Accuracy",
                    value = "±${String.format(Locale.US, "%.1f", location.accuracy)} m",
                    icon = Icons.Rounded.LocationOn
                )
                MetricPill(
                    label = "Satellites",
                    value = "$satelliteCount Sats ($constellationCount Const)",
                    icon = Icons.Rounded.SatelliteAlt
                )
            }
        }
    }
}

@Composable
fun MetricPill(
    label: String,
    value: String,
    icon: ImageVector
) {
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                imageVector = icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.secondary,
                modifier = Modifier.size(16.dp)
            )
            Spacer(modifier = Modifier.width(6.dp))
            Column {
                Text(
                    text = label,
                    style = MaterialTheme.typography.labelSmall,
                    fontSize = 10.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    text = value,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
            }
        }
    }
}

@Composable
fun SubScoresOverviewGrid(subScores: TrustSubScores) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            SubScoreCardItem(
                title = "Signal Power (C/N0)",
                score = subScores.signalAuthenticityScore,
                weight = "25%",
                icon = Icons.Rounded.SignalCellular4Bar,
                modifier = Modifier.weight(1f)
            )
            SubScoreCardItem(
                title = "AGC / Anti-Jam",
                score = subScores.agcInterferenceScore,
                weight = "20%",
                icon = Icons.Rounded.Speed,
                modifier = Modifier.weight(1f)
            )
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            SubScoreCardItem(
                title = "Constellations",
                score = subScores.constellationDiversityScore,
                weight = "15%",
                icon = Icons.Rounded.SatelliteAlt,
                modifier = Modifier.weight(1f)
            )
            SubScoreCardItem(
                title = "Geometry / DOP",
                score = subScores.geometryDopScore,
                weight = "15%",
                icon = Icons.Rounded.Shield,
                modifier = Modifier.weight(1f)
            )
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            SubScoreCardItem(
                title = "Clock Stability",
                score = subScores.clockStabilityScore,
                weight = "10%",
                icon = Icons.Rounded.Timer,
                modifier = Modifier.weight(1f)
            )
            SubScoreCardItem(
                title = "OSNMA Auth",
                score = subScores.osnmaAuthScore,
                weight = "15%",
                icon = Icons.Rounded.Key,
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
fun SubScoreCardItem(
    title: String,
    score: Float,
    weight: String,
    icon: ImageVector,
    modifier: Modifier = Modifier
) {
    val scoreClamped = score.coerceIn(0f, 100f)
    val color = when {
        scoreClamped >= 80f -> MaterialTheme.colorScheme.tertiary
        scoreClamped >= 50f -> MaterialTheme.colorScheme.secondary
        else -> MaterialTheme.colorScheme.error
    }

    Card(
        modifier = modifier,
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer
        )
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        tint = color,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = title,
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1
                    )
                }
                Text(
                    text = weight,
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontSize = 10.sp
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "${scoreClamped.toInt()}%",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.ExtraBold,
                    color = color
                )
            }

            Spacer(modifier = Modifier.height(6.dp))

            LinearProgressIndicator(
                progress = { scoreClamped / 100f },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(6.dp)
                    .clip(RoundedCornerShape(3.dp)),
                color = color,
                trackColor = MaterialTheme.colorScheme.surfaceContainerHigh
            )
        }
    }
}

@Preview(showBackground = true)
@Composable
fun DashboardScreenPreview() {
    TRISATTheme {
        DashboardScreen()
    }
}
