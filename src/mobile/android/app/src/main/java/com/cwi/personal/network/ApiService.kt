package com.cwi.personal.network

import com.cwi.personal.ask.AskRequest
import com.cwi.personal.ask.AskResponse
import com.cwi.personal.action.ActionRequest
import com.cwi.personal.action.ActionResponse
import com.cwi.personal.action.ApprovalTicketStatus
import com.cwi.personal.killswitch.KillSwitchStatusResponse
import com.cwi.personal.feedback.OutcomeFeedbackRequest
import com.cwi.personal.feedback.OutcomeFeedbackResponse
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import com.cwi.personal.survival.OfflineResponse
import com.cwi.personal.survival.SurvivalStatusResponse
import com.cwi.personal.telemetry.TelemetryRequest
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

interface ApiService {
    @POST("api/ask")
    suspend fun ask(@Body request: AskRequest): AskResponse

    @POST("api/action")
    suspend fun action(@Body request: ActionRequest): ActionResponse

    @GET("api/approval/{ticketId}")
    suspend fun approvalStatus(
        @Path("ticketId") ticketId: String,
        @Header("x-tenant-id") tenantId: String,
        @Header("x-roles") roles: String
    ): ApprovalTicketStatus

    @GET("api/admin/kill_switch")
    suspend fun killSwitchStatus(): KillSwitchStatusResponse

    @POST("api/ingest/telemetry")
    suspend fun ingestTelemetry(@Body request: TelemetryRequest)

    @POST("api/feedback")
    suspend fun recordFeedback(@Body request: OutcomeFeedbackRequest): OutcomeFeedbackResponse

    @GET("api/survival/status")
    suspend fun survivalStatus(): SurvivalStatusResponse

    @GET("api/survival/offline-response")
    suspend fun survivalOfflineResponse(): OfflineResponse

    @GET("api/reports")
    suspend fun listReports(@Query("tenant_id") tenantId: String): List<ReportSummary>

    @GET("api/reports/{reportId}")
    suspend fun getReport(
        @Path("reportId") reportId: String,
        @Query("tenant_id") tenantId: String
    ): ReportDetail

    @GET("api/documents")
    suspend fun listDocuments(@Query("tenant_id") tenantId: String): List<DocumentSummary>

    @GET("api/documents/{docId}")
    suspend fun getDocument(
        @Path("docId") docId: String,
        @Query("tenant_id") tenantId: String
    ): DocumentDetail
}
