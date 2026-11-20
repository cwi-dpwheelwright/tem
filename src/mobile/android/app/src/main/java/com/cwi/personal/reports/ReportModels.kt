package com.cwi.personal.reports

import com.squareup.moshi.Json

data class ReportSummary(
    @Json(name = "report_id") val reportId: String,
    val title: String,
    @Json(name = "created_utc") val createdUtc: String,
    val status: String,
    @Json(name = "coherence_score") val coherenceScore: Double? = null,
    @Json(name = "conflict_count") val conflictCount: Int? = null,
    val summary: String? = null,
    @Json(name = "escalation_ticket") val escalationTicket: String? = null
)

data class ReportEvidence(
    val label: String,
    val value: String
)

data class ReportDetail(
    @Json(name = "report_id") val reportId: String,
    val title: String,
    @Json(name = "created_utc") val createdUtc: String,
    val status: String,
    @Json(name = "coherence_score") val coherenceScore: Double? = null,
    @Json(name = "conflict_count") val conflictCount: Int? = null,
    val summary: String? = null,
    @Json(name = "escalation_ticket") val escalationTicket: String? = null,
    val narrative: String? = null,
    val structured: Map<String, Any>? = null,
    val evidence: List<ReportEvidence>? = null
)
