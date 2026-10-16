package com.example.myapplication.ui.components

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.GppBad
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.gnss.model.TrustLevel
import com.example.myapplication.ui.theme.EmeraldAccent
import com.example.myapplication.ui.theme.StatusAlertRed
import com.example.myapplication.ui.theme.StatusSecureGreen
import com.example.myapplication.ui.theme.StatusWarningOrange
import com.example.myapplication.ui.theme.TRISATTheme
import kotlin.math.cos
import kotlin.math.sin

@Composable
fun TrustabilityGaugeMeter(
    score: Float,
    trustLevel: TrustLevel,
    modifier: Modifier = Modifier
) {
    val scoreClamped = score.coerceIn(0f, 100f)
    val animatedScoreFraction by animateFloatAsState(
        targetValue = scoreClamped / 100f,
        animationSpec = tween(durationMillis = 900, easing = FastOutSlowInEasing),
        label = "gauge_score_anim"
    )

    // Gauge Colors: Green for High, Amber for Medium, Crimson/Red for Low/Threat
    val gaugeColor = when (trustLevel) {
        TrustLevel.HIGH_TRUST -> StatusSecureGreen
        TrustLevel.MEDIUM_TRUST -> StatusWarningOrange
        TrustLevel.LOW_TRUST -> StatusAlertRed
    }

    val secondaryGaugeColor = when (trustLevel) {
        TrustLevel.HIGH_TRUST -> EmeraldAccent
        TrustLevel.MEDIUM_TRUST -> Color(0xFFFFD54F)
        TrustLevel.LOW_TRUST -> Color(0xFFFF5252)
    }

    val trackColor = MaterialTheme.colorScheme.surfaceContainerHigh
    val outlineColor = MaterialTheme.colorScheme.outline.copy(alpha = 0.3f)

    val startAngle = 140f
    val sweepAngle = 260f

    Box(
        modifier = modifier.size(230.dp),
        contentAlignment = Alignment.Center
    ) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            val strokeWidth = 18.dp.toPx()
            val arcPadding = strokeWidth / 2 + 10.dp.toPx()
            val arcSize = Size(
                width = size.width - 2 * arcPadding,
                height = size.height - 2 * arcPadding
            )
            val topLeft = Offset(arcPadding, arcPadding)
            val centerPoint = Offset(size.width / 2, size.height / 2)
            val radius = arcSize.width / 2

            // 1. Draw Background Gauge Arc
            drawArc(
                color = trackColor,
                startAngle = startAngle,
                sweepAngle = sweepAngle,
                useCenter = false,
                topLeft = topLeft,
                size = arcSize,
                style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
            )

            // 2. Draw Active Gauge Arc with Gradient Sweep
            val activeSweep = sweepAngle * animatedScoreFraction
            if (activeSweep > 0.5f) {
                drawArc(
                    brush = Brush.sweepGradient(
                        colors = listOf(gaugeColor, secondaryGaugeColor, gaugeColor),
                        center = centerPoint
                    ),
                    startAngle = startAngle,
                    sweepAngle = activeSweep,
                    useCenter = false,
                    topLeft = topLeft,
                    size = arcSize,
                    style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
                )
            }

            // 3. Draw Tick Marks
            val tickCount = 10
            for (i in 0..tickCount) {
                val tickFraction = i.toFloat() / tickCount
                val angleDeg = startAngle + tickFraction * sweepAngle
                val angleRad = Math.toRadians(angleDeg.toDouble())

                val innerR = radius - strokeWidth / 2 - 8.dp.toPx()
                val outerR = radius - strokeWidth / 2 - 2.dp.toPx()

                val p1 = Offset(
                    x = centerPoint.x + innerR * cos(angleRad).toFloat(),
                    y = centerPoint.y + innerR * sin(angleRad).toFloat()
                )
                val p2 = Offset(
                    x = centerPoint.x + outerR * cos(angleRad).toFloat(),
                    y = centerPoint.y + outerR * sin(angleRad).toFloat()
                )

                drawLine(
                    color = outlineColor,
                    start = p1,
                    end = p2,
                    strokeWidth = 2.dp.toPx(),
                    cap = StrokeCap.Round
                )
            }
        }

        // Center Content Text and Badge
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier.padding(top = 12.dp)
        ) {
            Text(
                text = "${(animatedScoreFraction * 100).toInt()}%",
                style = MaterialTheme.typography.headlineLarge.copy(
                    fontWeight = FontWeight.ExtraBold,
                    fontSize = 44.sp,
                    letterSpacing = (-1).sp
                ),
                color = gaugeColor
            )

            Spacer(modifier = Modifier.height(2.dp))

            val statusLevel = when (trustLevel) {
                TrustLevel.HIGH_TRUST -> StatusLevel.SECURE
                TrustLevel.MEDIUM_TRUST -> StatusLevel.WARNING
                TrustLevel.LOW_TRUST -> StatusLevel.ALERT
            }

            val icon = when (trustLevel) {
                TrustLevel.HIGH_TRUST -> Icons.Rounded.CheckCircle
                TrustLevel.MEDIUM_TRUST -> Icons.Rounded.Warning
                TrustLevel.LOW_TRUST -> Icons.Rounded.GppBad
            }

            StatusBadge(
                text = if (trustLevel == TrustLevel.LOW_TRUST) "THREAT DETECTED" else trustLevel.displayName,
                level = statusLevel,
                icon = icon
            )
        }
    }
}

@Preview(showBackground = true)
@Composable
fun TrustabilityGaugeMeterHighPreview() {
    TRISATTheme {
        TrustabilityGaugeMeter(score = 94f, trustLevel = TrustLevel.HIGH_TRUST)
    }
}

@Preview(showBackground = true)
@Composable
fun TrustabilityGaugeMeterLowPreview() {
    TRISATTheme {
        TrustabilityGaugeMeter(score = 38f, trustLevel = TrustLevel.LOW_TRUST)
    }
}
