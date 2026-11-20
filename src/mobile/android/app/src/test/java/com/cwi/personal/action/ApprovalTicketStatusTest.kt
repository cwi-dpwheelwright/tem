package com.cwi.personal.action

import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import org.junit.Assert.assertEquals
import org.junit.Test

class ApprovalTicketStatusTest {
    @Test
    fun parsesApprovalTicketStatus() {
        val json = """
            {
              "ticket_id": "ticket-1",
              "status": "pending",
              "risk": "high",
              "reason": "approval required",
              "updated_utc": "2026-01-31T12:00:00Z"
            }
        """.trimIndent()

        val moshi = Moshi.Builder()
            .add(KotlinJsonAdapterFactory())
            .build()
        val adapter = moshi.adapter(ApprovalTicketStatus::class.java)
        val status = adapter.fromJson(json)!!

        assertEquals("ticket-1", status.ticketId)
        assertEquals("pending", status.status)
        assertEquals("high", status.risk)
        assertEquals("approval required", status.reason)
        assertEquals("2026-01-31T12:00:00Z", status.updatedUtc)
    }
}
