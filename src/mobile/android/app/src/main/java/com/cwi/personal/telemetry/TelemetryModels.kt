package com.cwi.personal.telemetry

import com.squareup.moshi.Json

data class TelemetryWindow(
    val start: String,
    val end: String
)

data class TelemetrySignal(
    val tag: String,
    val unit: String,
    val series: List<Double>
)

data class TelemetryRequest(
    @Json(name = "tenant_id") val tenantId: String,
    val window: TelemetryWindow,
    val signals: List<TelemetrySignal>,
    val metadata: Map<String, String>
)
