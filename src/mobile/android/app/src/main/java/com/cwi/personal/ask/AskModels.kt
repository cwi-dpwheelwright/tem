package com.cwi.personal.ask

import com.squareup.moshi.Json

data class AskRequest(
    @Json(name = "tenant_id") val tenantId: String,
    @Json(name = "user_id") val userId: String,
    val question: String,
    val pack: PackRef,
    val context: AskContext
) {
    data class PackRef(val name: String)
    data class AskContext(
        val mode: String,
        @Json(name = "top_k") val topK: Int? = null
    )
}

data class AskResponse(
    val answer: String,
    val citations: List<Citation>,
    val trace: Trace
) {
    data class Citation(
        val source: String? = null,
        val snippet: String? = null
    )

    data class Trace(
        @Json(name = "trace_id") val traceId: String? = null,
        @Json(name = "policy_mode") val policyMode: String? = null,
        val model: String? = null,
        @Json(name = "pack_hash") val packHash: String? = null,
        val confidence: Double? = null
    )
}
