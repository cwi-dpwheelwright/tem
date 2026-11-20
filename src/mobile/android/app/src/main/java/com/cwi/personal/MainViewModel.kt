package com.cwi.personal

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.cwi.personal.cache.PackCacheRepository
import com.cwi.personal.ask.AskRequest
import com.cwi.personal.ask.AskResponse
import com.cwi.personal.action.ActionRequest
import com.cwi.personal.action.ActionResponse
import com.cwi.personal.action.ApprovalTicket
import com.cwi.personal.action.ApprovalTicketStatus
import com.cwi.personal.killswitch.KillSwitchRepository
import com.cwi.personal.network.ApiClient
import com.cwi.personal.feedback.FeedbackStore
import com.cwi.personal.feedback.FeedbackUploader
import com.cwi.personal.feedback.OutcomeFeedbackEvent
import com.cwi.personal.feedback.OutcomeFeedbackRequest
import com.cwi.personal.interaction.ConsentStore
import com.cwi.personal.interaction.InteractionEvent
import com.cwi.personal.interaction.InteractionStore
import com.cwi.personal.interaction.InteractionUploader
import com.cwi.personal.pack.PackFetcher
import com.cwi.personal.pack.PackModule
import com.cwi.personal.pack.PackParser
import com.cwi.personal.pack.TrustState
import com.cwi.personal.pack.PackAction
import com.cwi.personal.telemetry.TelemetryClient
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import com.cwi.personal.reports.cache.ReportCacheRepository
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

data class MainUiState(
    val title: String = "CWI Android",
    val subtitle: String = "Pack-driven UI will render here.",
    val packName: String = BuildConfig.PACK_NAME,
    val tenantId: String = "tenant-1",
    val userId: String = "user-1",
    val policyMode: String = "approve",
    val question: String = "",
    val answer: String = "",
    val citations: List<AskResponse.Citation> = emptyList(),
    val trace: AskResponse.Trace? = null,
    val traceJson: String? = null,
    val lastAction: ActionResponse? = null,
    val approvalTicket: ApprovalTicket? = null,
    val approvalStatus: ApprovalTicketStatus? = null,
    val trustState: TrustState = TrustState.UNKNOWN,
    val modules: List<PackModule> = emptyList(),
    val offlineMode: Boolean = false,
    val offlineMessage: String? = null,
    val survivalMode: String = "normal",
    val survivalProfile: String = "",
    val survivalLastSignalUtc: String? = null,
    val autoModeEnabled: Boolean = false,
    val killSwitchEnabled: Boolean? = null,
    val killSwitchCachedAtMillis: Long? = null,
    val consentGranted: Boolean = false,
    val interactionQueueSize: Int = 0,
    val feedbackQueueSize: Int = 0,
    val feedbackStatus: String? = null,
    val reports: List<ReportSummary> = emptyList(),
    val documents: List<DocumentSummary> = emptyList(),
    val selectedReport: ReportDetail? = null,
    val selectedDocument: DocumentDetail? = null,
    val error: String? = null
)

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val _uiState = MutableStateFlow(MainUiState())
    val uiState: StateFlow<MainUiState> = _uiState
    private val cacheRepository = PackCacheRepository(application.applicationContext)
    private val reportCacheRepository = ReportCacheRepository(application.applicationContext)
    private val killSwitchRepository = KillSwitchRepository(application.applicationContext)
    private val telemetryClient = TelemetryClient()
    private val consentStore = ConsentStore(application.applicationContext)
    private val interactionStore = InteractionStore(application.applicationContext)
    private val interactionUploader = InteractionUploader(ApiClient.api(), interactionStore)
    private val feedbackStore = FeedbackStore(application.applicationContext)
    private val feedbackUploader = FeedbackUploader(ApiClient.api(), feedbackStore)
    private val traceAdapter = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()
        .adapter(AskResponse.Trace::class.java)
        .indent("  ")

    init {
        loadConsentState()
        loadCachedPack()
        loadCachedReports()
        loadCachedDocuments()
        refreshIfStale()
        loadCachedKillSwitch()
        refreshKillSwitchIfStale()
        refreshSurvivalStatus()
    }

    private fun loadConsentState() {
        val granted = consentStore.isGranted()
        _uiState.value = _uiState.value.copy(
            consentGranted = granted,
            interactionQueueSize = interactionStore.size(),
            feedbackQueueSize = feedbackStore.size()
        )
        if (granted) {
            val state = _uiState.value
            viewModelScope.launch(Dispatchers.IO) {
                interactionUploader.flush(state.tenantId, state.userId)
                feedbackUploader.flush()
                _uiState.value = _uiState.value.copy(
                    interactionQueueSize = interactionStore.size(),
                    feedbackQueueSize = feedbackStore.size()
                )
            }
        }
    }

    fun verifyPack() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val pack = PackFetcher().fetch(_uiState.value.packName)
                val modules = PackParser.parseModules(pack.rawJson)
                cacheRepository.save(pack)
                _uiState.value = _uiState.value.copy(
                    trustState = TrustState.VERIFIED,
                    modules = modules,
                    offlineMode = false,
                    offlineMessage = null,
                    error = null
                )
                telemetryClient.sendEvent(
                    tenantId = _uiState.value.tenantId,
                    event = "pack_verify",
                    metadata = mapOf("pack_name" to _uiState.value.packName)
                )
                recordInteraction(
                    eventType = "pack_verify",
                    metadata = mapOf("pack_name" to _uiState.value.packName)
                )
            } catch (ex: Exception) {
                val cached = cacheRepository.loadVerified(_uiState.value.packName)
                if (cached.pack != null) {
                    val modules = PackParser.parseModules(cached.pack.rawJson)
                    _uiState.value = _uiState.value.copy(
                        trustState = cached.trustState,
                        modules = modules,
                        offlineMode = true,
                        offlineMessage = "Using cached pack after verification failure.",
                        error = ex.message
                    )
                } else {
                    _uiState.value = _uiState.value.copy(
                        trustState = cached.trustState,
                        offlineMode = true,
                        offlineMessage = cached.error ?: "Offline mode enabled.",
                        error = ex.message
                    )
                }
                telemetryClient.sendEvent(
                    tenantId = _uiState.value.tenantId,
                    event = "pack_verify_failed",
                    metadata = mapOf("pack_name" to _uiState.value.packName)
                )
                recordInteraction(
                    eventType = "pack_verify_failed",
                    metadata = mapOf("pack_name" to _uiState.value.packName)
                )
            }
        }
    }

    private fun loadCachedPack() {
        viewModelScope.launch(Dispatchers.IO) {
            val cached = cacheRepository.loadVerified(_uiState.value.packName)
            if (cached.pack != null) {
                val modules = PackParser.parseModules(cached.pack.rawJson)
                _uiState.value = _uiState.value.copy(
                    trustState = cached.trustState,
                    modules = modules,
                    offlineMode = true,
                    offlineMessage = "Using cached pack while offline.",
                    error = null
                )
            } else {
                _uiState.value = _uiState.value.copy(
                    trustState = cached.trustState,
                    offlineMode = true,
                    offlineMessage = cached.error,
                    error = null
                )
            }
        }
    }

    private fun loadCachedReports() {
        viewModelScope.launch(Dispatchers.IO) {
            val cached = reportCacheRepository.loadReportList(_uiState.value.tenantId) ?: return@launch
            _uiState.value = _uiState.value.copy(reports = cached)
        }
    }

    private fun loadCachedDocuments() {
        viewModelScope.launch(Dispatchers.IO) {
            val cached = reportCacheRepository.loadDocumentList(_uiState.value.tenantId) ?: return@launch
            _uiState.value = _uiState.value.copy(documents = cached)
        }
    }

    private fun refreshIfStale(maxAgeMinutes: Long = 15) {
        viewModelScope.launch(Dispatchers.IO) {
            val lastSaved = cacheRepository.lastSavedAt(_uiState.value.packName) ?: return@launch
            val ageMinutes = (System.currentTimeMillis() - lastSaved) / 60000
            if (ageMinutes >= maxAgeMinutes) {
                verifyPack()
            }
        }
    }

    private fun loadCachedKillSwitch() {
        viewModelScope.launch(Dispatchers.IO) {
            val cached = killSwitchRepository.loadCached() ?: return@launch
            _uiState.value = _uiState.value.copy(
                killSwitchEnabled = cached.enabled,
                killSwitchCachedAtMillis = cached.cachedAtMillis
            )
        }
    }

    private fun refreshKillSwitchIfStale(maxAgeMinutes: Long = 5) {
        viewModelScope.launch(Dispatchers.IO) {
            if (killSwitchRepository.isStale(maxAgeMinutes)) {
                refreshKillSwitch()
            }
        }
    }

    fun refreshSurvivalStatus() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val status = ApiClient.api().survivalStatus()
                var offlineMessage = if (status.mode != "normal") status.offlineMessage else null
                if (status.mode != "normal") {
                    try {
                        val offline = ApiClient.api().survivalOfflineResponse()
                        offlineMessage = offline.message
                    } catch (_: Exception) {
                    }
                }
                _uiState.value = _uiState.value.copy(
                    survivalMode = status.mode,
                    survivalProfile = status.profile,
                    survivalLastSignalUtc = status.lastSignalUtc,
                    offlineMessage = offlineMessage
                )
            } catch (ex: Exception) {
                _uiState.value = _uiState.value.copy(error = ex.message)
            }
        }
    }

    fun refreshReports() {
        viewModelScope.launch(Dispatchers.IO) {
            val tenantId = _uiState.value.tenantId
            try {
                val reports = ApiClient.api().listReports(tenantId)
                reportCacheRepository.saveReportList(tenantId, reports)
                _uiState.value = _uiState.value.copy(
                    reports = reports,
                    error = null
                )
            } catch (ex: Exception) {
                val cached = reportCacheRepository.loadReportList(tenantId) ?: emptyList()
                _uiState.value = _uiState.value.copy(
                    reports = cached,
                    error = ex.message
                )
            }
        }
    }

    fun loadReport(reportId: String) {
        viewModelScope.launch(Dispatchers.IO) {
            val tenantId = _uiState.value.tenantId
            try {
                val report = ApiClient.api().getReport(reportId, tenantId)
                reportCacheRepository.saveReportDetail(tenantId, report)
                _uiState.value = _uiState.value.copy(
                    selectedReport = report,
                    error = null
                )
            } catch (ex: Exception) {
                val cached = reportCacheRepository.loadReportDetail(tenantId, reportId)
                _uiState.value = _uiState.value.copy(
                    selectedReport = cached,
                    error = ex.message
                )
            }
        }
    }

    fun refreshDocuments() {
        viewModelScope.launch(Dispatchers.IO) {
            val tenantId = _uiState.value.tenantId
            try {
                val documents = ApiClient.api().listDocuments(tenantId)
                reportCacheRepository.saveDocumentList(tenantId, documents)
                _uiState.value = _uiState.value.copy(
                    documents = documents,
                    error = null
                )
            } catch (ex: Exception) {
                val cached = reportCacheRepository.loadDocumentList(tenantId) ?: emptyList()
                _uiState.value = _uiState.value.copy(
                    documents = cached,
                    error = ex.message
                )
            }
        }
    }

    fun loadDocument(docId: String) {
        viewModelScope.launch(Dispatchers.IO) {
            val tenantId = _uiState.value.tenantId
            try {
                val document = ApiClient.api().getDocument(docId, tenantId)
                reportCacheRepository.saveDocumentDetail(tenantId, document)
                _uiState.value = _uiState.value.copy(
                    selectedDocument = document,
                    error = null
                )
            } catch (ex: Exception) {
                val cached = reportCacheRepository.loadDocumentDetail(tenantId, docId)
                _uiState.value = _uiState.value.copy(
                    selectedDocument = cached,
                    error = ex.message
                )
            }
        }
    }

    fun toggleConsent(granted: Boolean) {
        consentStore.setGranted(granted)
        _uiState.value = _uiState.value.copy(consentGranted = granted)
        if (granted) {
            val state = _uiState.value
            viewModelScope.launch(Dispatchers.IO) {
                interactionUploader.flush(state.tenantId, state.userId)
                feedbackUploader.flush()
                _uiState.value = _uiState.value.copy(
                    interactionQueueSize = interactionStore.size(),
                    feedbackQueueSize = feedbackStore.size()
                )
            }
        }
    }

    fun refreshKillSwitch() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val status = killSwitchRepository.refresh()
                _uiState.value = _uiState.value.copy(
                    killSwitchEnabled = status.enabled,
                    killSwitchCachedAtMillis = status.cachedAtMillis,
                    error = null
                )
            } catch (ex: Exception) {
                _uiState.value = _uiState.value.copy(error = ex.message)
            }
        }
    }

    fun updatePackName(name: String) {
        _uiState.value = _uiState.value.copy(
            packName = name,
            modules = emptyList(),
            trustState = TrustState.UNKNOWN,
            offlineMode = false,
            offlineMessage = null
        )
        loadCachedPack()
    }

    fun updateQuestion(question: String) {
        _uiState.value = _uiState.value.copy(question = question)
    }

    fun toggleAutoMode() {
        val enabled = !_uiState.value.autoModeEnabled
        _uiState.value = _uiState.value.copy(autoModeEnabled = enabled)
        recordInteraction(
            eventType = "auto_mode_toggle",
            metadata = mapOf("enabled" to enabled.toString())
        )
    }

    fun ask() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val request = AskRequest(
                    tenantId = _uiState.value.tenantId,
                    userId = _uiState.value.userId,
                    question = _uiState.value.question,
                    pack = AskRequest.PackRef(_uiState.value.packName),
                    context = AskRequest.AskContext(mode = _uiState.value.policyMode)
                )
                val response = ApiClient.api().ask(request)
                val traceJson = traceAdapter.toJson(response.trace)
                _uiState.value = _uiState.value.copy(
                    answer = response.answer,
                    citations = response.citations,
                    trace = response.trace,
                    traceJson = traceJson,
                    error = null
                )
                telemetryClient.sendEvent(
                    tenantId = _uiState.value.tenantId,
                    event = "ask",
                    metadata = mapOf(
                        "pack_name" to _uiState.value.packName,
                        "trace_id" to response.trace.traceId
                    )
                )
                recordInteraction(
                    eventType = "ask",
                    traceId = response.trace.traceId,
                    metadata = mapOf("pack_name" to _uiState.value.packName)
                )
            } catch (ex: Exception) {
                _uiState.value = _uiState.value.copy(error = ex.message)
                recordInteraction(eventType = "ask_failed")
            }
        }
    }

    fun invokeFirstAction() {
        val module = _uiState.value.modules.firstOrNull { it.actions.isNotEmpty() } ?: return
        val action = module.actions.first()
        invokeAction(action)
    }

    fun invokeAction(action: PackAction) {
        if (_uiState.value.killSwitchEnabled == true) {
            _uiState.value = _uiState.value.copy(error = "Action disabled: kill-switch active.")
            return
        }
        if (_uiState.value.survivalMode != "normal") {
            _uiState.value = _uiState.value.copy(
                error = "Action disabled: survival mode (${_uiState.value.survivalMode})."
            )
            return
        }
        if (_uiState.value.offlineMode) {
            _uiState.value = _uiState.value.copy(error = "Action disabled: offline mode active.")
            return
        }
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val request = ActionRequest(
                    tenantId = _uiState.value.tenantId,
                    userId = _uiState.value.userId,
                    policyMode = _uiState.value.policyMode,
                    action = ActionRequest.ActionPayload(
                        type = action.actionType,
                        args = action.args
                    )
                )
                val response = ApiClient.api().action(request)
                _uiState.value = _uiState.value.copy(
                    lastAction = response,
                    approvalTicket = response.approvalTicket,
                    approvalStatus = null,
                    error = null
                )
                telemetryClient.sendEvent(
                    tenantId = _uiState.value.tenantId,
                    event = "action",
                    metadata = mapOf(
                        "action_type" to action.actionType,
                        "pack_name" to _uiState.value.packName
                    )
                )
                recordInteraction(
                    eventType = "action",
                    actionId = action.id,
                    metadata = mapOf("action_type" to action.actionType)
                )
                val ticket = response.approvalTicket
                if (ticket != null) {
                    pollApprovalStatus(ticket.ticketId)
                }
            } catch (ex: Exception) {
                _uiState.value = _uiState.value.copy(error = ex.message)
                recordInteraction(
                    eventType = "action_failed",
                    actionId = action.id,
                    metadata = mapOf("action_type" to action.actionType)
                )
            }
        }
    }

    private fun pollApprovalStatus(ticketId: String, maxAttempts: Int = 12) {
        viewModelScope.launch(Dispatchers.IO) {
            repeat(maxAttempts) {
                try {
                    val status = ApiClient.api().approvalStatus(
                        ticketId = ticketId,
                        tenantId = _uiState.value.tenantId,
                        roles = "approver"
                    )
                    _uiState.value = _uiState.value.copy(approvalStatus = status)
                    if (!status.status.equals("pending", ignoreCase = true)) {
                        return@launch
                    }
                } catch (ex: Exception) {
                    _uiState.value = _uiState.value.copy(error = ex.message)
                }
                delay(5000)
            }
        }
    }

    fun submitFeedback(feedback: String) {
        val traceId = _uiState.value.trace?.traceId
        if (traceId.isNullOrBlank()) {
            _uiState.value = _uiState.value.copy(error = "Trace ID required for feedback.")
            return
        }
        if (!_uiState.value.consentGranted) {
            _uiState.value = _uiState.value.copy(error = "Consent required to submit feedback.")
            return
        }
        val request = OutcomeFeedbackRequest(
            tenantId = _uiState.value.tenantId,
            traceId = traceId,
            actorId = _uiState.value.userId,
            feedback = feedback,
            consentGranted = true,
            idempotencyKey = UUID.randomUUID().toString(),
            note = "android_client"
        )
        val event = OutcomeFeedbackEvent(
            eventId = UUID.randomUUID().toString(),
            request = request
        )
        feedbackStore.enqueue(event)
        _uiState.value = _uiState.value.copy(
            feedbackQueueSize = feedbackStore.size(),
            feedbackStatus = "Feedback queued for upload."
        )
        viewModelScope.launch(Dispatchers.IO) {
            feedbackUploader.flush()
            _uiState.value = _uiState.value.copy(
                feedbackQueueSize = feedbackStore.size(),
                feedbackStatus = "Feedback uploaded."
            )
        }
    }

    private fun recordInteraction(
        eventType: String,
        traceId: String? = null,
        actionId: String? = null,
        metadata: Map<String, String> = emptyMap()
    ) {
        val state = _uiState.value
        if (!state.consentGranted) return
        if (!interactionUploader.shouldSample()) return

        val event = InteractionEvent(
            eventId = UUID.randomUUID().toString(),
            tenantId = state.tenantId,
            userId = state.userId,
            traceId = traceId,
            actionId = actionId,
            eventType = eventType,
            createdUtc = Instant.now().toString(),
            metadata = metadata
        )
        interactionStore.enqueue(event)
        _uiState.value = _uiState.value.copy(interactionQueueSize = interactionStore.size())
        viewModelScope.launch(Dispatchers.IO) {
            interactionUploader.flush(state.tenantId, state.userId)
            _uiState.value = _uiState.value.copy(interactionQueueSize = interactionStore.size())
        }
    }
}

