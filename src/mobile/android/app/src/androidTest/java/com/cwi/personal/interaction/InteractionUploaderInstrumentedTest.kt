package com.cwi.personal.interaction

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.cwi.personal.action.ActionRequest
import com.cwi.personal.action.ActionResponse
import com.cwi.personal.action.ApprovalTicketStatus
import com.cwi.personal.ask.AskRequest
import com.cwi.personal.ask.AskResponse
import com.cwi.personal.feedback.OutcomeFeedbackRequest
import com.cwi.personal.feedback.OutcomeFeedbackResponse
import com.cwi.personal.killswitch.KillSwitchStatusResponse
import com.cwi.personal.network.ApiService
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import com.cwi.personal.survival.OfflineResponse
import com.cwi.personal.survival.SurvivalStatusResponse
import com.cwi.personal.telemetry.TelemetryRequest
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class InteractionUploaderInstrumentedTest {
    @Test
    fun flushSendsTelemetryAndClearsStore() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val store = InteractionStore(context)
        clearStore(store)

        store.enqueue(
            InteractionEvent(
                eventId = "event-1",
                tenantId = "tenant-1",
                userId = "user-1",
                traceId = "trace-1",
                actionId = null,
                eventType = "ask",
                createdUtc = "2026-02-02T00:00:00Z"
            )
        )
        store.enqueue(
            InteractionEvent(
                eventId = "event-2",
                tenantId = "tenant-1",
                userId = "user-1",
                traceId = "trace-2",
                actionId = "action-1",
                eventType = "action",
                createdUtc = "2026-02-02T00:01:00Z"
            )
        )

        val api = FakeApiService()
        val uploader = InteractionUploader(
            api = api,
            store = store,
            sampleRate = 1.0,
            maxBatchSize = 25,
            maxAttempts = 1,
            baseDelayMillis = 1
        )

        uploader.flush("tenant-1", "user-1")

        assertEquals(0, store.size())
        assertEquals(1, api.telemetryRequests.size)
        val payload = api.telemetryRequests.first()
        assertNotNull(payload.metadata["events_json"])
    }

    private fun clearStore(store: InteractionStore) {
        val pending = store.peek(1000)
        if (pending.isNotEmpty()) {
            store.remove(pending.map { it.eventId }.toSet())
        }
    }

    private class FakeApiService : ApiService {
        val telemetryRequests = mutableListOf<TelemetryRequest>()

        override suspend fun ingestTelemetry(request: TelemetryRequest) {
            telemetryRequests.add(request)
        }

        override suspend fun recordFeedback(request: OutcomeFeedbackRequest): OutcomeFeedbackResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun ask(request: AskRequest): AskResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun action(request: ActionRequest): ActionResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun approvalStatus(
            ticketId: String,
            tenantId: String,
            roles: String
        ): ApprovalTicketStatus {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun killSwitchStatus(): KillSwitchStatusResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun survivalStatus(): SurvivalStatusResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun survivalOfflineResponse(): OfflineResponse {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun listReports(tenantId: String): List<ReportSummary> {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun getReport(reportId: String, tenantId: String): ReportDetail {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun listDocuments(tenantId: String): List<DocumentSummary> {
            throw UnsupportedOperationException("Not required for this test")
        }

        override suspend fun getDocument(docId: String, tenantId: String): DocumentDetail {
            throw UnsupportedOperationException("Not required for this test")
        }
    }
}
