package com.example.myapplication.ui.components

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.R
import com.example.myapplication.ui.theme.TRISATTheme

@Composable
fun AppIconDisplay(
    modifier: Modifier = Modifier,
    sizeDp: Int = 120
) {
    Box(
        modifier = modifier
            .size(sizeDp.dp)
            .background(Color.Transparent),
        contentAlignment = Alignment.Center
    ) {
        // Background layer
        Image(
            painter = painterResource(id = R.drawable.ic_launcher_background),
            contentDescription = "Icon Background",
            modifier = Modifier.size(sizeDp.dp)
        )
        // Foreground layer
        Image(
            painter = painterResource(id = R.drawable.ic_launcher_foreground),
            contentDescription = "Icon Foreground",
            modifier = Modifier.size(sizeDp.dp)
        )
    }
}

@Composable
fun AppIconPreviewScreen() {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(Color(0xFF070B14))
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(
            text = "TRISAT App Icon Showcase",
            color = Color.White,
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold
        )
        Text(
            text = "Deep Blue • Teal Orbit Ring • Green Lock Badge",
            color = Color(0xFF00E5FF),
            fontSize = 12.sp,
            modifier = Modifier.padding(bottom = 24.dp)
        )

        // Full Raw Adaptive Icon (Unclipped 108dp)
        Text(
            text = "Adaptive Icon Full Canvas (108dp)",
            color = Color(0xFF94A3B8),
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium
        )
        Spacer(modifier = Modifier.height(8.dp))
        AppIconDisplay(sizeDp = 140)

        Spacer(modifier = Modifier.height(28.dp))

        // OEM Launcher Masks
        Text(
            text = "Launcher Shape Masks",
            color = Color(0xFF94A3B8),
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium
        )
        Spacer(modifier = Modifier.height(12.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            // Squircle
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Box(
                    modifier = Modifier
                        .size(80.dp)
                        .clip(RoundedCornerShape(22.dp))
                ) {
                    AppIconDisplay(sizeDp = 80)
                }
                Spacer(modifier = Modifier.height(6.dp))
                Text(text = "Squircle", color = Color(0xFFCBD5E1), fontSize = 11.sp)
            }

            // Circle
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Box(
                    modifier = Modifier
                        .size(80.dp)
                        .clip(CircleShape)
                ) {
                    AppIconDisplay(sizeDp = 80)
                }
                Spacer(modifier = Modifier.height(6.dp))
                Text(text = "Circle", color = Color(0xFFCBD5E1), fontSize = 11.sp)
            }

            // Rounded Square
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Box(
                    modifier = Modifier
                        .size(80.dp)
                        .clip(RoundedCornerShape(14.dp))
                ) {
                    AppIconDisplay(sizeDp = 80)
                }
                Spacer(modifier = Modifier.height(6.dp))
                Text(text = "Rounded Sq", color = Color(0xFFCBD5E1), fontSize = 11.sp)
            }
        }
    }
}

@Preview(showBackground = true)
@Composable
fun AppIconPreview() {
    TRISATTheme {
        AppIconPreviewScreen()
    }
}
