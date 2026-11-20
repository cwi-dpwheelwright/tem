package com.cwi.personal.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val LightColors = lightColorScheme(
    primary = Color(0xFF1B4D3E),
    secondary = Color(0xFF2E7D63),
    tertiary = Color(0xFF0E5E49)
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF7FD9C2),
    secondary = Color(0xFF4FB39A),
    tertiary = Color(0xFF2B8C74)
)

@Composable
fun CwiAndroidTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = LightColors,
        typography = Typography,
        content = content
    )
}
