package com.cwi.personal.interaction

import com.squareup.moshi.Json

data class InteractionEvent(
    @Json(name = "event_id") val eventId: String,
    @Json(name = "tenant_id") val tenantId: String,
    @Json(name = "user_id") val userId: String,
    @Json(name = "trace_id") val traceId: String? = null,
    @Json(name = "action_id") val actionId: String? = null,
    @Json(name = "event_type") val eventType: String,
    @Json(name = "created_utc") val createdUtc: String,
    val metadata: Map<String, String> = emptyMap()
)
