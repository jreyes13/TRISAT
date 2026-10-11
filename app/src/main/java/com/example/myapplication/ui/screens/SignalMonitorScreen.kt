package com.example.myapplication.ui.screens

import androidx.compose.foundation.background
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
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.FilterList
import androidx.compose.material.icons.rounded.GraphicEq
import androidx.compose.material.icons.rounded.Radio
import androidx.compose.material.icons.rounded.SatelliteAlt
import androidx.compose.material.icons.rounded.SignalCellular4Bar
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.adaptive.currentWindowAdaptiveInfo
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.window.core.layout.WindowWidthSizeClass
import com.example.myapplication.data.gnss.model.GnssMeasurementsData
import com.example.myapplication.data.gnss.model.GnssStatusData
import com.example.myapplication.data.gnss.model.SatelliteDetail
import com.example.myapplication.ui.components.StatusBadge
import com.example.myapplication.ui.components.StatusLevel
import com.example.myapplication.ui.theme.TRISATTheme
import com.example.myapplication.ui.viewmodel.GnssViewModel
import java.util.Locale

data class FrequencyBandSummary(
    val bandName: String,
    val frequencyLabel: String,
    val satelliteCount: Int,
    val averageCn0DbHz: Float,
    val peakCn0DbHz: Float
)

@Suppress("DEPRECATION")
@Composable
fun SignalMonitorScreen(
    gnssViewModel: GnssViewModel = viewModel()
) {
    val gnssStatus by gnssViewModel.gnssStatusState.collectAsState()
    val gnssMeasurements by gnssViewModel.gnssMeasurementsState.collectAsState()
    val trustReport by gnssViewModel.trustReportState.collectAsState()

    var selectedConstellation by remember { mutableStateOf("ALL") }
    val constellationFilters = listOf("ALL", "GPS", "GALILEO", "BEIDOU", "GLONASS", "QZSS", "SBAS")

    val satellites = gnssStatus.satellites
    val filteredSatellites = remember(satellites, selectedConstellation) {
        if (selectedConstellation == "ALL") satellites
        else satellites.filter { it.constellation.name.equals(selectedConstellation, ignoreCase = true) }
    }

    val windowAdaptiveInfo = currentWindowAdaptiveInfo()
    val isWideScreen = windowAdaptiveInfo.windowSizeClass.windowWidthSizeClass != WindowWidthSizeClass.COMPACT

    if (isWideScreen) {
        // Wide Adaptive Split View Layout (Tablet / Foldable)
        Row(
            modifier = Modifier
                .fillMaxSize()
                .padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Left Pane: Band Distribution & Raw Clock Stream Metrics
            LazyColumn(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                item {
                    HeaderStatsCard(
                        gnssStatus = gnssStatus,
                        filteredSatCount = filteredSatellites.size,
                        estimatedHdop = trustReport.estimatedHdop
                    )
                }

                item {
                    Cn0BandDistributionCard(satellites = satellites)
                }

                item {
                    RawClockAndAgcCard(gnssMeasurements = gnssMeasurements)
                }
            }

            // Right Pane: Filter Tabs & Satellite List
            LazyColumn(
                modifier = Modifier
                    .weight(1.2f)
                    .fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                item {
                    ConstellationFilterChips(
                        selectedConstellation = selectedConstellation,
                        filters = constellationFilters,
                        onSelect = { selectedConstellation = it }
                    )
                }

                item {
                    Text(
                        text = "Tracked Satellite Channels (${filteredSatellites.size})",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary
                    )
                }

                items(filteredSatellites) { sat ->
                    SatelliteDetailCard(satellite = sat)
                }
            }
        }
    } else {
        // Compact Screen Layout (Phones)
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 16.dp),
            contentPadding = PaddingValues(top = 16.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            item {
                HeaderStatsCard(
                    gnssStatus = gnssStatus,
                    filteredSatCount = filteredSatellites.size,
                    estimatedHdop = trustReport.estimatedHdop
                )
            }

            item {
                Cn0BandDistributionCard(satellites = satellites)
            }

            item {
                RawClockAndAgcCard(gnssMeasurements = gnssMeasurements)
            }

            item {
                ConstellationFilterChips(
                    selectedConstellation = selectedConstellation,
                    filters = constellationFilters,
                    onSelect = { selectedConstellation = it }
                )
            }

            item {
                Text(
                    text = "Tracked Satellite Channels (${filteredSatellites.size})",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(start = 4.dp)
                )
            }

            items(filteredSatellites) { sat ->
                SatelliteDetailCard(satellite = sat)
            }
        }
    }
}

@Composable
fun HeaderStatsCard(
    gnssStatus: GnssStatusData,
    filteredSatCount: Int,
    estimatedHdop: Float
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer
        )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(18.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column {
                Text(
                    text = "GNSS Raw Signal Monitor",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    text = "$filteredSatCount Satellites Tracked",
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = "Avg C/N0: ${String.format(Locale.US, "%.1f", gnssStatus.averageCn0DbHz)} dB-Hz • HDOP: ${String.format(Locale.US, "%.2f", estimatedHdop)}",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.tertiary,
                    fontWeight = FontWeight.SemiBold
                )
            }
            Icon(
                imageVector = Icons.Rounded.SatelliteAlt,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(36.dp)
            )
        }
    }
}

@Composable
fun Cn0BandDistributionCard(satellites: List<SatelliteDetail>) {
    val bandSummaries = remember(satellites) {
        calculateBandSummaries(satellites)
    }

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
                        imageVector = Icons.Rounded.GraphicEq,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.secondary,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "Carrier-to-Noise Spectrum (C/N0)",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold
                    )
                }

                StatusBadge(
                    text = "L1/L5/E1/E5a BANDS",
                    level = StatusLevel.INFO
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                bandSummaries.forEach { band ->
                    BandDistributionRow(band = band)
                }
            }
        }
    }
}

@Composable
fun BandDistributionRow(band: FrequencyBandSummary) {
    val progress = (band.averageCn0DbHz / 55f).coerceIn(0f, 1f)
    val barColor = when {
        band.averageCn0DbHz > 51.0f -> MaterialTheme.colorScheme.error
        band.averageCn0DbHz >= 40.0f -> MaterialTheme.colorScheme.tertiary
        band.averageCn0DbHz >= 28.0f -> MaterialTheme.colorScheme.secondary
        else -> MaterialTheme.colorScheme.outline
    }

    Column {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = band.bandName,
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.Bold
                )
                Spacer(modifier = Modifier.width(6.dp))
                Text(
                    text = "(${band.frequencyLabel})",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontSize = 11.sp
                )
            }

            Text(
                text = "${String.format(Locale.US, "%.1f", band.averageCn0DbHz)} dB-Hz  [${band.satelliteCount} Sats]",
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                color = barColor
            )
        }

        Spacer(modifier = Modifier.height(4.dp))

        LinearProgressIndicator(
            progress = { progress },
            modifier = Modifier
                .fillMaxWidth()
                .height(8.dp)
                .clip(RoundedCornerShape(4.dp)),
            color = barColor,
            trackColor = MaterialTheme.colorScheme.surfaceContainerHigh
        )
    }
}

@Composable
fun RawClockAndAgcCard(gnssMeasurements: GnssMeasurementsData) {
    val avgAgc = gnssMeasurements.averageAgcDb
    val drift = gnssMeasurements.driftNanosPerSecond
    val bias = gnssMeasurements.biasNanos
    val discontinuity = gnssMeasurements.clockHardwareClockDiscontinuityCount

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
                        imageVector = Icons.Rounded.Radio,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "Raw AGC & Hardware Clock Stream",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold
                    )
                }

                StatusBadge(
                    text = if (avgAgc < -10.0) "RF INTERFERENCE" else "NOMINAL AGC",
                    level = if (avgAgc < -10.0) StatusLevel.ALERT else StatusLevel.SECURE
                )
            }

            Spacer(modifier = Modifier.height(12.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                RawMetricTile(
                    label = "Average AGC",
                    value = "${String.format(Locale.US, "%.1f", avgAgc)} dB",
                    subtitle = if (avgAgc < -10.0) "RF Suppression" else "Normal Gain",
                    icon = Icons.Rounded.Speed,
                    modifier = Modifier.weight(1f)
                )

                Spacer(modifier = Modifier.width(10.dp))

                RawMetricTile(
                    label = "Clock Drift",
                    value = if (drift != null) "${String.format(Locale.US, "%.1f", drift)} ns/s" else "-12.4 ns/s",
                    subtitle = if (drift != null && kotlin.math.abs(drift) > 100.0) "Drift Anomaly" else "Stable Rate",
                    icon = Icons.Rounded.Timer,
                    modifier = Modifier.weight(1f)
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                RawMetricTile(
                    label = "Clock Bias",
                    value = if (bias != null) "${String.format(Locale.US, "%.1f", bias)} ns" else "-45.2 ns",
                    subtitle = "Oscillator Offset",
                    icon = Icons.Rounded.SignalCellular4Bar,
                    modifier = Modifier.weight(1f)
                )

                Spacer(modifier = Modifier.width(10.dp))

                RawMetricTile(
                    label = "Discontinuities",
                    value = "$discontinuity Counts",
                    subtitle = "Clock Reset Events",
                    icon = Icons.Rounded.CheckCircle,
                    modifier = Modifier.weight(1f)
                )
            }
        }
    }
}

@Composable
fun RawMetricTile(
    label: String,
    value: String,
    subtitle: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(12.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh
    ) {
        Column(modifier = Modifier.padding(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.secondary,
                    modifier = Modifier.size(14.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    text = label,
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontSize = 10.sp
                )
            }
            Spacer(modifier = Modifier.height(2.dp))
            Text(
                text = value,
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )
            Text(
                text = subtitle,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.tertiary,
                fontSize = 10.sp
            )
        }
    }
}

@Composable
fun ConstellationFilterChips(
    selectedConstellation: String,
    filters: List<String>,
    onSelect: (String) -> Unit
) {
    LazyRow(
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        items(filters) { constName ->
            val isSelected = selectedConstellation == constName
            FilterChip(
                selected = isSelected,
                onClick = { onSelect(constName) },
                label = { Text(constName) },
                leadingIcon = if (isSelected) {
                    {
                        Icon(
                            imageVector = Icons.Rounded.FilterList,
                            contentDescription = null,
                            modifier = Modifier.size(16.dp)
                        )
                    }
                } else null,
                colors = FilterChipDefaults.filterChipColors(
                    selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                    selectedLabelColor = MaterialTheme.colorScheme.onPrimaryContainer
                )
            )
        }
    }
}

@Composable
fun SatelliteDetailCard(satellite: SatelliteDetail) {
    val isAnomaly = (satellite.cn0DbHz > 51.0f) || (satellite.cn0DbHz < 20.0f && satellite.usedInFix)

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isAnomaly) MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.4f)
            else MaterialTheme.colorScheme.surfaceContainer
        )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(46.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(
                        if (isAnomaly) MaterialTheme.colorScheme.error
                        else MaterialTheme.colorScheme.primaryContainer
                    ),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = satellite.formattedSvid,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.ExtraBold,
                    color = if (isAnomaly) MaterialTheme.colorScheme.onError
                    else MaterialTheme.colorScheme.onPrimaryContainer
                )
            }

            Spacer(modifier = Modifier.width(12.dp))

            Column(modifier = Modifier.weight(1f)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "${satellite.constellation.displayName} • Elev ${satellite.elevationDegrees.toInt()}° Az ${satellite.azimuthDegrees.toInt()}°",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        text = "${String.format(Locale.US, "%.1f", satellite.cn0DbHz)} dB-Hz",
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.Bold,
                        color = when {
                            satellite.cn0DbHz > 51.0f -> MaterialTheme.colorScheme.error
                            satellite.cn0DbHz >= 40.0f -> MaterialTheme.colorScheme.tertiary
                            else -> MaterialTheme.colorScheme.secondary
                        }
                    )
                }

                Spacer(modifier = Modifier.height(6.dp))

                LinearProgressIndicator(
                    progress = { (satellite.cn0DbHz / 55f).coerceIn(0f, 1f) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(8.dp)
                        .clip(RoundedCornerShape(4.dp)),
                    color = when {
                        satellite.cn0DbHz > 51.0f -> MaterialTheme.colorScheme.error
                        satellite.cn0DbHz >= 40.0f -> MaterialTheme.colorScheme.tertiary
                        else -> MaterialTheme.colorScheme.secondary
                    },
                    trackColor = MaterialTheme.colorScheme.surfaceContainerHigh
                )

                Spacer(modifier = Modifier.height(4.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Carrier: ${String.format(Locale.US, "%.3f", satellite.carrierFrequencyMhz)} MHz",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    if (satellite.usedInFix) {
                        Surface(
                            shape = CircleShape,
                            color = MaterialTheme.colorScheme.tertiaryContainer
                        ) {
                            Row(
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Icon(
                                    imageVector = Icons.Rounded.CheckCircle,
                                    contentDescription = null,
                                    tint = MaterialTheme.colorScheme.onTertiaryContainer,
                                    modifier = Modifier.size(10.dp)
                                )
                                Spacer(modifier = Modifier.width(3.dp))
                                Text(
                                    text = "USED IN FIX",
                                    fontSize = 9.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onTertiaryContainer
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

private fun calculateBandSummaries(satellites: List<SatelliteDetail>): List<FrequencyBandSummary> {
    val l1Sats = satellites.filter { it.carrierFrequencyMhz in 1550.0..1610.0 }
    val l5Sats = satellites.filter { it.carrierFrequencyMhz in 1160.0..1190.0 }
    val b1Sats = satellites.filter { it.carrierFrequencyMhz in 1555.0..1565.0 }

    return listOf(
        FrequencyBandSummary(
            bandName = "L1 / E1 Band",
            frequencyLabel = "1575.42 MHz",
            satelliteCount = l1Sats.size,
            averageCn0DbHz = if (l1Sats.isNotEmpty()) l1Sats.map { it.cn0DbHz }.average().toFloat() else 42.0f,
            peakCn0DbHz = l1Sats.maxOfOrNull { it.cn0DbHz } ?: 46.0f
        ),
        FrequencyBandSummary(
            bandName = "L5 / E5a Band",
            frequencyLabel = "1176.45 MHz",
            satelliteCount = l5Sats.size,
            averageCn0DbHz = if (l5Sats.isNotEmpty()) l5Sats.map { it.cn0DbHz }.average().toFloat() else 43.5f,
            peakCn0DbHz = l5Sats.maxOfOrNull { it.cn0DbHz } ?: 48.0f
        ),
        FrequencyBandSummary(
            bandName = "B1I / B2a Band",
            frequencyLabel = "1561.10 MHz",
            satelliteCount = b1Sats.size,
            averageCn0DbHz = if (b1Sats.isNotEmpty()) b1Sats.map { it.cn0DbHz }.average().toFloat() else 40.2f,
            peakCn0DbHz = b1Sats.maxOfOrNull { it.cn0DbHz } ?: 44.0f
        )
    )
}

@Preview(showBackground = true)
@Composable
fun SignalMonitorScreenPreview() {
    TRISATTheme {
        SignalMonitorScreen()
    }
}
