package com.cwi.personal.feedback

import com.squareup.moshi.Json

data class OutcomeFeedbackRequest(
    @Json(name = "tenant_id") val tenantId: String,
    @Json(name = "trace_id") val traceId: String,
    @Json(name = "ticket_id") val ticketId: String? = null,
    @Json(name = "action_id") val actionId: String? = null,
    @Json(name = "actor_id") val actorId: String,
    @Json(name = "idempotency_key") val idempotencyKey: String? = null,
    val feedback: String,
    @Json(name = "consent_granted") val consentGranted: Boolean,
    @Json(name = "reason_codes") val reasonCodes: List<String> = emptyList(),
    val note: String? = null,
    @Json(name = "confidence_before") val confidenceBefore: Double? = null,
    @Json(name = "confidence_after") val confidenceAfter: Double? = null
)

data class OutcomeFeedbackSummary(
    @Json(name = "up_count") val upCount: Int,
    @Json(name = "down_count") val downCount: Int,
    @Json(name = "last_feedback") val lastFeedback: String? = null,
    @Json(name = "last_feedback_utc") val lastFeedbackUtc: String? = null
)

data class OutcomeFeedbackResponse(
    @Json(name = "recorded_utc") val recordedUtc: String,
    val feedback: String,
    @Json(name = "trace_id") val traceId: String,
    val summary: OutcomeFeedbackSummary
)

data class OutcomeFeedbackEvent(
    val eventId: String,
    val request: OutcomeFeedbackRequest
)
