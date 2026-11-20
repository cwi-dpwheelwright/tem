package com.cwi.personal.reports

import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class ReportModelsTest {
    private val moshi = Moshi.Builder().add(KotlinJsonAdapterFactory()).build()

    @Test
    fun parsesReportSummary() {
        val json = """
            {
              "report_id": "rep-1",
              "title": "Safety sweep",
              "created_utc": "2026-02-01T12:40:00Z",
              "status": "approved",
              "coherence_score": 0.94,
              "conflict_count": 1,
              "summary": "Summary",
              "escalation_ticket": null
            }
        """.trimIndent()
        val adapter = moshi.adapter(ReportSummary::class.java)
        val summary = adapter.fromJson(json)
        assertEquals("rep-1", summary?.reportId)
        assertEquals(0.94, summary?.coherenceScore)
    }

    @Test
    fun parsesReportDetail() {
        val json = """
            {
              "report_id": "rep-1",
              "title": "Safety sweep",
              "created_utc": "2026-02-01T12:40:00Z",
              "status": "approved",
              "coherence_score": 0.94,
              "conflict_count": 1,
              "summary": "Summary",
              "escalation_ticket": "ticket-1",
              "narrative": "Narrative",
              "structured": {
                "Risk posture": "Low"
              },
              "evidence": [
                {"label": "Trace ID", "value": "trace-1"}
              ]
            }
        """.trimIndent()
        val adapter = moshi.adapter(ReportDetail::class.java)
        val detail = adapter.fromJson(json)
        assertEquals("rep-1", detail?.reportId)
        assertNotNull(detail?.structured)
        assertEquals("ticket-1", detail?.escalationTicket)
    }
}
