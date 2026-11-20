package com.cwi.personal.telemetry

import com.cwi.personal.BuildConfig
import com.cwi.personal.network.ApiClient
import java.time.Instant

class TelemetryClient {
    suspend fun sendEvent(tenantId: String, event: String, metadata: Map<String, Any?> = emptyMap()) {
        if (!BuildConfig.TELEMETRY_ENABLED) return
        val now = Instant.now()
        val window = TelemetryWindow(start = now.toString(), end = now.toString())
        val signal = TelemetrySignal(tag = "client_event", unit = "count", series = listOf(1.0))
        val safeMetadata = metadata.mapValues { it.value?.toString() ?: "" }
        val payload = TelemetryRequest(
            tenantId = tenantId,
            window = window,
            signals = listOf(signal),
            metadata = mapOf(
                "event" to event,
                "app_version" to BuildConfig.VERSION_NAME
            ) + safeMetadata
        )
        try {
            ApiClient.api().ingestTelemetry(payload)
        } catch (_: Exception) {
        }
    }
}
