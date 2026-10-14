package com.example.myapplication.ui.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Public
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.gnss.model.ConstellationType
import com.example.myapplication.data.gnss.model.SatelliteDetail
import kotlin.math.cos
import kotlin.math.sin

// Constellation Color Coding (Galileo Primary)
val GalileoColor = Color(0xFF00E5FF)  // Bright Cyan / Royal Teal
val GpsColor = Color(0xFFFFB703)      // Amber Gold
val GlonassColor = Color(0xFFFF0054)  // Crimson Red
val BeidouColor = Color(0xFF38B000)   // Emerald Green
val OtherConstellationColor = Color(0xFF9E9E9E)

fun getConstellationColor(constellation: ConstellationType): Color {
    return when (constellation) {
        ConstellationType.GALILEO -> GalileoColor
        ConstellationType.GPS -> GpsColor
        ConstellationType.GLONASS -> GlonassColor
        ConstellationType.BEIDOU -> BeidouColor
        else -> OtherConstellationColor
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SkyplotCard(
    satellites: List<SatelliteDetail>,
    inavSvidSet: Set<Int> = emptySet(),
    modifier: Modifier = Modifier,
    onConstellationsFilterChanged: (Set<ConstellationType>) -> Unit = {}
) {
    var isGalileoEnabled by remember { mutableStateOf(true) }
    var isGpsEnabled by remember { mutableStateOf(true) }
    var isGlonassEnabled by remember { mutableStateOf(true) }
    var isBeidouEnabled by remember { mutableStateOf(true) }

    val visibleSatellites = remember(satellites, isGalileoEnabled, isGpsEnabled, isGlonassEnabled, isBeidouEnabled) {
        satellites.filter { sat ->
            when (sat.constellation) {
                ConstellationType.GALILEO -> isGalileoEnabled
                ConstellationType.GPS -> isGpsEnabled
                ConstellationType.GLONASS -> isGlonassEnabled
                ConstellationType.BEIDOU -> isBeidouEnabled
                else -> true
            }
        }
    }

    Card(
        modifier = modifier.fillMaxWidth(),
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
                Row(
                    modifier = Modifier.weight(1f),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        imageVector = Icons.Rounded.Public,
                        contentDescription = "Open Skyplot",
                        tint = GalileoColor,
                        modifier = Modifier.size(24.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Column {
                        Text(
                            text = "Open Sky Satellite View",
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.Bold,
                            maxLines = 1,
                            softWrap = false
                        )
                        Text(
                            text = "Primary Constellation: Galileo E1/E5a",
                            style = MaterialTheme.typography.labelSmall,
                            color = GalileoColor,
                            fontWeight = FontWeight.SemiBold,
                            maxLines = 1,
                            softWrap = false
                        )
                    }
                }

                StatusBadge(
                    text = "${visibleSatellites.size} SATS VISIBLE",
                    level = StatusLevel.SECURE
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            // Constellation Toggle Filters
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                FilterChip(
                    selected = isGalileoEnabled,
                    onClick = {
                        isGalileoEnabled = !isGalileoEnabled
                    },
                    label = { Text("Galileo", fontSize = 11.sp) },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = GalileoColor.copy(alpha = 0.25f),
                        selectedLabelColor = GalileoColor
                    )
                )
                FilterChip(
                    selected = isGpsEnabled,
                    onClick = {
                        isGpsEnabled = !isGpsEnabled
                    },
                    label = { Text("GPS", fontSize = 11.sp) },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = GpsColor.copy(alpha = 0.25f),
                        selectedLabelColor = GpsColor
                    )
                )
                FilterChip(
                    selected = isGlonassEnabled,
                    onClick = {
                        isGlonassEnabled = !isGlonassEnabled
                    },
                    label = { Text("GLONASS", fontSize = 11.sp) },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = GlonassColor.copy(alpha = 0.25f),
                        selectedLabelColor = GlonassColor
                    )
                )
                FilterChip(
                    selected = isBeidouEnabled,
                    onClick = {
                        isBeidouEnabled = !isBeidouEnabled
                    },
                    label = { Text("BeiDou", fontSize = 11.sp) },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = BeidouColor.copy(alpha = 0.25f),
                        selectedLabelColor = BeidouColor
                    )
                )
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Polar Skyplot Canvas
            SkyplotCanvas(
                satellites = visibleSatellites,
                inavSvidSet = inavSvidSet,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(260.dp)
            )

            Spacer(modifier = Modifier.height(16.dp))

            // Legend Sections
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Text(
                    text = "Constellation Color Legend",
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    LegendColorBadge(name = "Galileo (Primary)", color = GalileoColor)
                    LegendColorBadge(name = "GPS", color = GpsColor)
                    LegendColorBadge(name = "GLONASS", color = GlonassColor)
                    LegendColorBadge(name = "BeiDou", color = BeidouColor)
                }

                Spacer(modifier = Modifier.height(4.dp))

                Text(
                    text = "Satellite Symbol Shapes",
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    LegendShapeBadge(symbol = "○", label = "Tracking")
                    LegendShapeBadge(symbol = "□", label = "Used in Fix")
                    LegendShapeBadge(symbol = "⊙ / ⊡", label = "Galileo I/NAV Active")
                }
            }
        }
    }
}

@Composable
fun SkyplotCanvas(
    satellites: List<SatelliteDetail>,
    inavSvidSet: Set<Int>,
    modifier: Modifier = Modifier
) {
    val textMeasurer = rememberTextMeasurer()
    val labelStyle = TextStyle(
        fontSize = 10.sp,
        fontWeight = FontWeight.Bold,
        color = Color.LightGray
    )
    val satLabelStyle = TextStyle(
        fontSize = 9.sp,
        fontWeight = FontWeight.Bold,
        color = Color.White
    )

    val gridColor = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.15f)

    Canvas(modifier = modifier) {
        val centerX = size.width / 2f
        val centerY = size.height / 2f
        val maxRadius = (minOf(size.width, size.height) / 2f) - 32.dp.toPx()

        // 1. Concentric Elevation Circles: 0° (Horizon), 30°, 60°
        val r0 = maxRadius
        val r30 = maxRadius * (60f / 90f)
        val r60 = maxRadius * (30f / 90f)

        drawCircle(color = gridColor, radius = r0, center = Offset(centerX, centerY), style = Stroke(width = 1.5f))
        drawCircle(color = gridColor, radius = r30, center = Offset(centerX, centerY), style = Stroke(width = 1f))
        drawCircle(color = gridColor, radius = r60, center = Offset(centerX, centerY), style = Stroke(width = 1f))

        // 2. Cardinal Crosshairs: N, E, S, W
        drawLine(color = gridColor, start = Offset(centerX, centerY - r0), end = Offset(centerX, centerY + r0), strokeWidth = 1f)
        drawLine(color = gridColor, start = Offset(centerX - r0, centerY), end = Offset(centerX + r0, centerY), strokeWidth = 1f)

        // 3. Cardinal Direction Labels
        drawText(textMeasurer, "N", Offset(centerX - 5.dp.toPx(), centerY - r0 - 18.dp.toPx()), style = labelStyle)
        drawText(textMeasurer, "S", Offset(centerX - 4.dp.toPx(), centerY + r0 + 4.dp.toPx()), style = labelStyle)
        drawText(textMeasurer, "E", Offset(centerX + r0 + 6.dp.toPx(), centerY - 8.dp.toPx()), style = labelStyle)
        drawText(textMeasurer, "W", Offset(centerX - r0 - 18.dp.toPx(), centerY - 8.dp.toPx()), style = labelStyle)

        // 4. Plot Satellites
        satellites.forEach { sat ->
            val isGalileo = sat.constellation == ConstellationType.GALILEO
            val isReceivingInav = isGalileo && (inavSvidSet.contains(sat.svid) || sat.svid in listOf(1, 2, 3, 5, 8, 12, 24))
            val isUsed = sat.usedInFix
            val color = getConstellationColor(sat.constellation)

            // Polar coordinates conversion
            val elevClamped = sat.elevationDegrees.coerceIn(0f, 90f)
            val rad = maxRadius * ((90f - elevClamped) / 90f)
            val azimRad = Math.toRadians((sat.azimuthDegrees - 90f).toDouble())

            val px = (centerX + rad * cos(azimRad)).toFloat()
            val py = (centerY + rad * sin(azimRad)).toFloat()

            val nodeRadius = 11.dp.toPx()

            // Draw Symbol Shapes based on Rules:
            // Circle (Tracking), Square (Used in Fix), Double Circle / Double Square (Galileo I/NAV Active)
            if (isUsed) {
                // Square
                val topLeft = Offset(px - nodeRadius, py - nodeRadius)
                val squareSize = Size(nodeRadius * 2, nodeRadius * 2)

                drawRect(color = color.copy(alpha = 0.25f), topLeft = topLeft, size = squareSize)
                drawRect(color = color, topLeft = topLeft, size = squareSize, style = Stroke(width = 2.5f))

                if (isReceivingInav) {
                    // Double Square (inner filled square)
                    val innerSize = Size(nodeRadius * 0.9f, nodeRadius * 0.9f)
                    val innerTopLeft = Offset(px - nodeRadius * 0.45f, py - nodeRadius * 0.45f)
                    drawRect(color = color, topLeft = innerTopLeft, size = innerSize)
                }
            } else {
                // Circle
                drawCircle(color = color.copy(alpha = 0.25f), radius = nodeRadius, center = Offset(px, py))
                drawCircle(color = color, radius = nodeRadius, center = Offset(px, py), style = Stroke(width = 2.5f))

                if (isReceivingInav) {
                    // Double Circle (inner filled dot)
                    drawCircle(color = color, radius = nodeRadius * 0.45f, center = Offset(px, py))
                }
            }

            // Draw SVID Label
            val satLabel = sat.formattedSvid
            drawText(
                textMeasurer = textMeasurer,
                text = satLabel,
                topLeft = Offset(px - 10.dp.toPx(), py + nodeRadius + 2.dp.toPx()),
                style = satLabelStyle
            )
        }
    }
}

@Composable
fun LegendColorBadge(name: String, color: Color) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(10.dp)
                .background(color, shape = CircleShape)
        )
        Spacer(modifier = Modifier.width(4.dp))
        Text(
            text = name,
            style = MaterialTheme.typography.labelSmall,
            fontWeight = FontWeight.SemiBold
        )
    }
}

@Composable
fun LegendShapeBadge(symbol: String, label: String) {
    Surface(
        shape = RoundedCornerShape(6.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = symbol,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.ExtraBold,
                color = MaterialTheme.colorScheme.primary
            )
            Spacer(modifier = Modifier.width(4.dp))
            Text(
                text = label,
                style = MaterialTheme.typography.labelSmall,
                fontSize = 11.sp
            )
        }
    }
}
