package com.example.myapplication.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.ui.theme.StatusAlertRed
import com.example.myapplication.ui.theme.StatusInfoCyan
import com.example.myapplication.ui.theme.StatusSecureGreen
import com.example.myapplication.ui.theme.StatusWarningOrange

enum class StatusLevel {
    SECURE,
    WARNING,
    ALERT,
    INFO
}

@Composable
fun StatusBadge(
    text: String,
    level: StatusLevel,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null
) {
    val (backgroundColor, contentColor) = when (level) {
        StatusLevel.SECURE -> StatusSecureGreen.copy(alpha = 0.18f) to StatusSecureGreen
        StatusLevel.WARNING -> StatusWarningOrange.copy(alpha = 0.18f) to StatusWarningOrange
        StatusLevel.ALERT -> StatusAlertRed.copy(alpha = 0.18f) to StatusAlertRed
        StatusLevel.INFO -> StatusInfoCyan.copy(alpha = 0.18f) to StatusInfoCyan
    }

    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(12.dp),
        color = backgroundColor,
        contentColor = contentColor
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Surface(
                modifier = Modifier.size(8.dp),
                shape = CircleShape,
                color = contentColor
            ) {}
            Spacer(modifier = Modifier.width(6.dp))
            if (icon != null) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    modifier = Modifier.size(14.dp),
                    tint = contentColor
                )
                Spacer(modifier = Modifier.width(4.dp))
            }
            Text(
                text = text,
                style = MaterialTheme.typography.labelMedium.copy(
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 12.sp
                ),
                color = contentColor
            )
        }
    }
}
