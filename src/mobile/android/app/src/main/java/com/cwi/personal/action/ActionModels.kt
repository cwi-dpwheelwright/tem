package com.cwi.personal.action

import com.squareup.moshi.Json

data class ActionRequest(
    @Json(name = "tenant_id") val tenantId: String,
    @Json(name = "user_id") val userId: String,
    @Json(name = "policy_mode") val policyMode: String,
    val action: ActionPayload
) {
    data class ActionPayload(
        val type: String,
        val args: Map<String, String> = emptyMap()
    )
}

data class ApprovalTicket(
    @Json(name = "ticket_id") val ticketId: String,
    val reason: String? = null
)

data class ActionResponse(
    @Json(name = "approval_ticket") val approvalTicket: ApprovalTicket? = null,
    val result: Map<String, Any?>? = null
)

data class ApprovalTicketStatus(
    @Json(name = "ticket_id") val ticketId: String,
    val status: String,
    val risk: String,
    val reason: String,
    @Json(name = "updated_utc") val updatedUtc: String
)
