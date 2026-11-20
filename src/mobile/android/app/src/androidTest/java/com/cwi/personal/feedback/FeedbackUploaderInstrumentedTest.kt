package com.cwi.personal.feedback

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.cwi.personal.action.ActionRequest
import com.cwi.personal.action.ActionResponse
import com.cwi.personal.action.ApprovalTicketStatus
import com.cwi.personal.ask.AskRequest
import com.cwi.personal.ask.AskResponse
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
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class FeedbackUploaderInstrumentedTest {
    @Test
    fun flushSendsFeedbackAndClearsStore() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val store = FeedbackStore(context)
        clearStore(store)

        val request = OutcomeFeedbackRequest(
            tenantId = "tenant-1",
            traceId = "trace-1",
            actorId = "user-1",
            feedback = "down",
            consentGranted = true
        )
        store.enqueue(OutcomeFeedbackEvent(eventId = "fb-1", request = request))
        store.enqueue(OutcomeFeedbackEvent(eventId = "fb-2", request = request.copy(traceId = "trace-2")))

        val api = FakeApiService()
        val uploader = FeedbackUploader(
            api = api,
            store = store,
            maxBatchSize = 10,
            maxAttempts = 1,
            baseDelayMillis = 1
        )

        uploader.flush()

        assertEquals(0, store.size())
        assertEquals(2, api.feedbackRequests.size)
    }

    private fun clearStore(store: FeedbackStore) {
        val pending = store.peek(1000)
        if (pending.isNotEmpty()) {
            store.remove(pending.map { it.eventId }.toSet())
        }
    }

    private class FakeApiService : ApiService {
        val feedbackRequests = mutableListOf<OutcomeFeedbackRequest>()

        override suspend fun recordFeedback(request: OutcomeFeedbackRequest): OutcomeFeedbackResponse {
            feedbackRequests.add(request)
            return OutcomeFeedbackResponse(
                recordedUtc = "2026-02-02T00:00:00Z",
                feedback = request.feedback,
                traceId = request.traceId,
                summary = OutcomeFeedbackSummary(upCount = 1, downCount = 0)
            )
        }

        override suspend fun ingestTelemetry(request: TelemetryRequest) {
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
