package com.cwi.personal

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Spacer
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.Switch
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import android.content.Intent
import android.net.Uri
import com.cwi.personal.ui.theme.CwiAndroidTheme
import com.cwi.personal.network.ApiClient
import com.cwi.personal.pack.TrustState
import com.cwi.personal.pack.PackAction
import com.cwi.personal.pack.PackModule
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ApiClient.initialize(this)
        setContent {
            CwiAndroidTheme {
                Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    HomeScreen()
                }
            }
        }
    }
}

@Composable
fun HomeScreen() {
    val viewModel: MainViewModel = viewModel()
    val uiState = viewModel.uiState.collectAsState().value
    val context = LocalContext.current
    HomeScreenContent(
        uiState = uiState,
        onVerifyPack = { viewModel.verifyPack() },
        onToggleAutoMode = { viewModel.toggleAutoMode() },
        onUpdateQuestion = { viewModel.updateQuestion(it) },
        onAsk = { viewModel.ask() },
        onInvokeFirstAction = { viewModel.invokeFirstAction() },
        onInvokeAction = { viewModel.invokeAction(it) },
        onRefreshKillSwitch = { viewModel.refreshKillSwitch() },
        onToggleConsent = { granted -> viewModel.toggleConsent(granted) },
        onSubmitFeedback = { feedback -> viewModel.submitFeedback(feedback) },
        onRefreshReports = { viewModel.refreshReports() },
        onOpenReport = { reportId -> viewModel.loadReport(reportId) },
        onRefreshDocuments = { viewModel.refreshDocuments() },
        onOpenDocument = { docId -> viewModel.loadDocument(docId) },
        onOpenApproval = { ticketId ->
            val deepLink = "${ApiClient.baseUrl().trimEnd('/')}/approvals/$ticketId"
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(deepLink))
            context.startActivity(intent)
        }
    )
}

@Composable
fun HomeScreenContent(
    uiState: MainUiState,
    onVerifyPack: () -> Unit,
    onToggleAutoMode: () -> Unit,
    onUpdateQuestion: (String) -> Unit,
    onAsk: () -> Unit,
    onInvokeFirstAction: () -> Unit,
    onInvokeAction: (PackAction) -> Unit,
    onRefreshKillSwitch: () -> Unit,
    onToggleConsent: (Boolean) -> Unit,
    onSubmitFeedback: (String) -> Unit,
    onRefreshReports: () -> Unit,
    onOpenReport: (String) -> Unit,
    onRefreshDocuments: () -> Unit,
    onOpenDocument: (String) -> Unit,
    onOpenApproval: (String) -> Unit
) {
    var showTraceViewer by remember { mutableStateOf(false) }
    val scrollState = rememberScrollState()
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(scrollState),
        verticalArrangement = Arrangement.Top,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        SurvivalBanner(
            mode = uiState.survivalMode,
            profile = uiState.survivalProfile,
            lastSignalUtc = uiState.survivalLastSignalUtc
        )
        TrustBanner(uiState.trustState)
        KillSwitchBanner(
            enabled = uiState.killSwitchEnabled,
            cachedAtMillis = uiState.killSwitchCachedAtMillis,
            onRefresh = onRefreshKillSwitch
        )
        ConsentPanel(
            granted = uiState.consentGranted,
            interactionQueueSize = uiState.interactionQueueSize,
            feedbackQueueSize = uiState.feedbackQueueSize,
            onToggle = onToggleConsent
        )
        Text(text = uiState.title)
        Text(text = uiState.subtitle, style = MaterialTheme.typography.bodyMedium)
        Button(
            onClick = onVerifyPack,
            modifier = Modifier.padding(top = 12.dp)
        ) {
            Text("Verify Pack")
        }
        Button(
            onClick = onToggleAutoMode,
            modifier = Modifier.padding(top = 12.dp)
        ) {
            Text(if (uiState.autoModeEnabled) "Exit Android Auto Mode" else "Android Auto Mode")
        }
        if (!uiState.autoModeEnabled) {
            TextField(
                value = uiState.question,
                onValueChange = onUpdateQuestion,
                label = { Text("Ask question") },
                modifier = Modifier.padding(top = 16.dp)
            )
            Button(
                onClick = onAsk,
                modifier = Modifier.padding(top = 12.dp)
            ) {
                Text("Ask")
            }
        }
        if (uiState.answer.isNotEmpty()) {
            Text("Answer:", modifier = Modifier.padding(top = 12.dp))
            Text(uiState.answer)
        }
        if (uiState.citations.isNotEmpty()) {
            Text("Citations:", modifier = Modifier.padding(top = 12.dp))
            uiState.citations.forEach { citation ->
                Text("- ${citation.source ?: "source"}")
                val snippet = citation.snippet
                if (!snippet.isNullOrBlank()) {
                    Text(snippet, color = Color(0xFF6D6D6D))
                }
            }
        }
        if (uiState.trace != null) {
            Text("Trace:", modifier = Modifier.padding(top = 12.dp))
            Text("trace_id: ${uiState.trace.traceId ?: "unknown"}")
            Text("policy_mode: ${uiState.trace.policyMode ?: "unknown"}")
            Text("model: ${uiState.trace.model ?: "unknown"}")
            Text("pack_hash: ${uiState.trace.packHash ?: "unknown"}")
            Button(
                onClick = { showTraceViewer = true },
                modifier = Modifier.padding(top = 8.dp)
            ) {
                Text("Open Trace Viewer")
            }
            FeedbackPanel(
                enabled = uiState.consentGranted,
                onSubmitFeedback = onSubmitFeedback,
                status = uiState.feedbackStatus
            )
        }
        if (uiState.offlineMode) {
            OfflineBanner(
                message = uiState.offlineMessage ?: "Offline mode active. Using cached pack."
            )
        }
        if (uiState.autoModeEnabled) {
            AutoModeSection(
                modules = uiState.modules,
                killSwitchEnabled = uiState.killSwitchEnabled == true,
                survivalMode = uiState.survivalMode,
                offlineMode = uiState.offlineMode,
                onActionClick = onInvokeAction
            )
        } else if (uiState.modules.any { it.actions.isNotEmpty() }) {
            Button(
                onClick = onInvokeFirstAction,
                enabled = uiState.killSwitchEnabled != true && uiState.survivalMode == "normal" && !uiState.offlineMode,
                modifier = Modifier
                    .padding(top = 16.dp)
                    .testTag("invoke_action_button")
            ) {
                Text(
                    if (uiState.killSwitchEnabled == true) {
                        "Action Disabled"
                    } else if (uiState.survivalMode != "normal") {
                        "Action Disabled: Survival"
                    } else if (uiState.offlineMode) {
                        "Action Disabled: Offline"
                    } else {
                        "Invoke Action"
                    }
                )
            }
        }
        if (uiState.approvalTicket != null) {
            Text("Approval ticket:", modifier = Modifier.padding(top = 12.dp))
            Text("ticket_id: ${uiState.approvalTicket.ticketId}")
            Text("reason: ${uiState.approvalTicket.reason ?: "Approval required"}")
            if (uiState.approvalStatus != null) {
                Text("status: ${uiState.approvalStatus.status}")
                Text("updated: ${uiState.approvalStatus.updatedUtc}")
            }
            Button(
                onClick = {
                    onOpenApproval(uiState.approvalTicket.ticketId)
                },
                modifier = Modifier.padding(top = 8.dp)
            ) {
                Text("Open Approval Surface")
            }
        }
        ReportsDocumentsSection(
            reports = uiState.reports,
            documents = uiState.documents,
            selectedReport = uiState.selectedReport,
            selectedDocument = uiState.selectedDocument,
            onRefreshReports = onRefreshReports,
            onOpenReport = onOpenReport,
            onRefreshDocuments = onRefreshDocuments,
            onOpenDocument = onOpenDocument,
            onOpenApproval = onOpenApproval
        )
        if (uiState.modules.isNotEmpty()) {
            Text("Modules: ${uiState.modules.size}", modifier = Modifier.padding(top = 12.dp))
        }
        if (uiState.error != null) {
            Text(uiState.error, color = Color.Red, modifier = Modifier.padding(top = 12.dp))
        }
    }
    if (showTraceViewer && uiState.traceJson != null) {
        TraceViewerDialog(
            traceJson = uiState.traceJson,
            onDismiss = { showTraceViewer = false }
        )
    }
}

@Composable
private fun ReportsDocumentsSection(
    reports: List<ReportSummary>,
    documents: List<DocumentSummary>,
    selectedReport: ReportDetail?,
    selectedDocument: DocumentDetail?,
    onRefreshReports: () -> Unit,
    onOpenReport: (String) -> Unit,
    onRefreshDocuments: () -> Unit,
    onOpenDocument: (String) -> Unit,
    onOpenApproval: (String) -> Unit
) {
    Card(
        modifier = Modifier
            .padding(top = 16.dp)
            .fillMaxWidth(0.95f),
        colors = CardDefaults.cardColors(containerColor = Color(0xFFF7F7F7))
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text("Reports & Documents", style = MaterialTheme.typography.titleMedium)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Button(onClick = onRefreshReports) { Text("Refresh Reports") }
                Button(onClick = onRefreshDocuments) { Text("Refresh Docs") }
            }

            if (reports.isEmpty()) {
                Text("No reports available.", color = Color(0xFF6D6D6D), modifier = Modifier.padding(top = 8.dp))
            } else {
                Text("Reports", modifier = Modifier.padding(top = 12.dp))
                reports.forEach { report ->
                    ReportSummaryCard(report = report, onOpen = onOpenReport, onOpenApproval = onOpenApproval)
                }
            }

            if (selectedReport != null) {
                ReportDetailCard(report = selectedReport, onOpenApproval = onOpenApproval)
            }

            Spacer(modifier = Modifier.height(12.dp))

            if (documents.isEmpty()) {
                Text("No documents available.", color = Color(0xFF6D6D6D))
            } else {
                Text("Documents", modifier = Modifier.padding(top = 12.dp))
                documents.forEach { document ->
                    DocumentSummaryCard(document = document, onOpen = onOpenDocument)
                }
            }

            if (selectedDocument != null) {
                DocumentDetailCard(document = selectedDocument)
            }
        }
    }
}

@Composable
private fun ReportSummaryCard(
    report: ReportSummary,
    onOpen: (String) -> Unit,
    onOpenApproval: (String) -> Unit
) {
    Card(modifier = Modifier.padding(top = 8.dp), colors = CardDefaults.cardColors(containerColor = Color.White)) {
        Column(modifier = Modifier.padding(10.dp)) {
            Text(report.title)
            Text(report.createdUtc, color = Color(0xFF6D6D6D))
            Row(modifier = Modifier.padding(top = 6.dp)) {
                CoherenceBadge(report.coherenceScore)
                ConflictBadge(report.conflictCount)
            }
            if (!report.summary.isNullOrBlank()) {
                Text(report.summary, color = Color(0xFF6D6D6D))
            }
            Row(modifier = Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { onOpen(report.reportId) }) { Text("View") }
                if (!report.escalationTicket.isNullOrBlank()) {
                    Button(onClick = { onOpenApproval(report.escalationTicket) }) { Text("Approval") }
                }
            }
        }
    }
}

@Composable
private fun ReportDetailCard(report: ReportDetail, onOpenApproval: (String) -> Unit) {
    Card(modifier = Modifier.padding(top = 10.dp), colors = CardDefaults.cardColors(containerColor = Color.White)) {
        Column(modifier = Modifier.padding(10.dp)) {
            Text("Report Detail", style = MaterialTheme.typography.titleSmall)
            Text(report.title)
            Text("Status: ${report.status}", color = Color(0xFF6D6D6D))
            Row(modifier = Modifier.padding(top = 6.dp)) {
                CoherenceBadge(report.coherenceScore)
                ConflictBadge(report.conflictCount)
            }
            if (!report.summary.isNullOrBlank()) {
                Text(report.summary, color = Color(0xFF6D6D6D))
            }
            if (!report.narrative.isNullOrBlank()) {
                Text(report.narrative, color = Color(0xFF6D6D6D))
            }
            if (!report.escalationTicket.isNullOrBlank()) {
                Button(onClick = { onOpenApproval(report.escalationTicket) }, modifier = Modifier.padding(top = 6.dp)) {
                    Text("Open Approval")
                }
            }
        }
    }
}

@Composable
private fun DocumentSummaryCard(document: DocumentSummary, onOpen: (String) -> Unit) {
    Card(modifier = Modifier.padding(top = 8.dp), colors = CardDefaults.cardColors(containerColor = Color.White)) {
        Column(modifier = Modifier.padding(10.dp)) {
            Text(document.title)
            Text("${document.type} • ${document.owner}", color = Color(0xFF6D6D6D))
            Text("Status: ${document.status}", color = Color(0xFF6D6D6D))
            if (!document.latestVersion.isNullOrBlank()) {
                Text("Latest: ${document.latestVersion} (v${document.versionsCount ?: 0})", color = Color(0xFF6D6D6D))
            }
            Button(onClick = { onOpen(document.docId) }, modifier = Modifier.padding(top = 6.dp)) {
                Text("View")
            }
        }
    }
}

@Composable
private fun DocumentDetailCard(document: DocumentDetail) {
    Card(modifier = Modifier.padding(top = 10.dp), colors = CardDefaults.cardColors(containerColor = Color.White)) {
        Column(modifier = Modifier.padding(10.dp)) {
            Text("Document Detail", style = MaterialTheme.typography.titleSmall)
            Text(document.title)
            Text("${document.type} • ${document.owner}", color = Color(0xFF6D6D6D))
            Text("Status: ${document.status}", color = Color(0xFF6D6D6D))
            document.versions.forEach { version ->
                Card(
                    modifier = Modifier.padding(top = 6.dp),
                    colors = CardDefaults.cardColors(containerColor = Color(0xFFF1F1F1))
                ) {
                    Column(modifier = Modifier.padding(8.dp)) {
                        Text("v${version.version}")
                        Text("Author: ${version.author}", color = Color(0xFF6D6D6D))
                        Text(version.createdUtc, color = Color(0xFF6D6D6D))
                        if (!version.notes.isNullOrBlank()) {
                            Text(version.notes, color = Color(0xFF6D6D6D))
                        }
                        if (!version.content.isNullOrBlank()) {
                            Text(version.content, color = Color(0xFF6D6D6D))
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun CoherenceBadge(score: Double?) {
    if (score == null) return
    val color = when {
        score >= 0.9 -> Color(0xFF2E7D32)
        score >= 0.75 -> Color(0xFFFFA000)
        else -> Color(0xFFC62828)
    }
    Text(
        text = "Coherence ${"%.2f".format(score)}",
        color = color,
        modifier = Modifier
            .padding(end = 8.dp)
            .background(color.copy(alpha = 0.15f))
            .padding(horizontal = 6.dp, vertical = 2.dp)
    )
}

@Composable
private fun ConflictBadge(count: Int?) {
    if (count == null) return
    val color = if (count > 0) Color(0xFFFF8F00) else Color(0xFF2E7D32)
    Text(
        text = "Conflicts $count",
        color = color,
        modifier = Modifier
            .background(color.copy(alpha = 0.15f))
            .padding(horizontal = 6.dp, vertical = 2.dp)
    )
}

@Composable
private fun TrustBanner(state: TrustState) {
    val (label, color) = when (state) {
        TrustState.VERIFIED -> "Verified" to Color(0xFF2E7D32)
        TrustState.UNVERIFIED -> "Unverified" to Color(0xFFC62828)
        TrustState.UNKNOWN -> "Unknown" to Color(0xFF6D6D6D)
    }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = "Pack trust: $label", color = color)
    }
}

@Composable
private fun KillSwitchBanner(
    enabled: Boolean?,
    cachedAtMillis: Long?,
    onRefresh: () -> Unit
) {
    val (label, color) = when (enabled) {
        true -> "Kill-switch ACTIVE" to Color(0xFFC62828)
        false -> "Kill-switch inactive" to Color(0xFF2E7D32)
        null -> "Kill-switch unknown" to Color(0xFF6D6D6D)
    }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(text = label, color = color)
        if (cachedAtMillis != null) {
            val minutesAgo = ((System.currentTimeMillis() - cachedAtMillis) / 60000).coerceAtLeast(0)
            Text(text = "Status cached ${minutesAgo}m ago", color = Color(0xFF6D6D6D))
        }
        Button(onClick = onRefresh, modifier = Modifier.padding(top = 4.dp)) {
            Text("Refresh Kill-switch")
        }
    }
}

@Composable
private fun SurvivalBanner(
    mode: String,
    profile: String,
    lastSignalUtc: String?
) {
    val (label, color) = when (mode.lowercase()) {
        "survival" -> "Survival mode active" to Color(0xFFC62828)
        "degraded" -> "Degraded mode active" to Color(0xFFE65100)
        else -> "Normal mode" to Color(0xFF2E7D32)
    }
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 4.dp)
            .testTag("survival_banner"),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(text = label, color = color)
        if (profile.isNotEmpty()) {
            Text(text = "Profile: $profile", color = Color(0xFF6D6D6D))
        }
        if (!lastSignalUtc.isNullOrBlank()) {
            Text(text = "Last signal: $lastSignalUtc", color = Color(0xFF6D6D6D))
        }
    }
}

@Composable
private fun ConsentPanel(
    granted: Boolean,
    interactionQueueSize: Int,
    feedbackQueueSize: Int,
    onToggle: (Boolean) -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Switch(
                checked = granted,
                onCheckedChange = onToggle
            )
            Text(
                text = "Share interactions for quality monitoring",
                modifier = Modifier.padding(start = 8.dp)
            )
        }
        Text(
            text = "Queued interactions: $interactionQueueSize | queued feedback: $feedbackQueueSize",
            color = Color(0xFF6D6D6D),
            modifier = Modifier.padding(top = 4.dp)
        )
    }
}

@Composable
private fun FeedbackPanel(
    enabled: Boolean,
    onSubmitFeedback: (String) -> Unit,
    status: String?
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 8.dp)
    ) {
        Text(text = "Outcome feedback", style = MaterialTheme.typography.bodyMedium)
        Row(
            modifier = Modifier.padding(top = 4.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Button(
                onClick = { onSubmitFeedback("up") },
                enabled = enabled,
                modifier = Modifier.padding(end = 8.dp)
            ) {
                Text("Thumbs up")
            }
            Button(
                onClick = { onSubmitFeedback("down") },
                enabled = enabled
            ) {
                Text("Thumbs down")
            }
        }
        if (status != null) {
            Text(status, color = Color(0xFF6D6D6D), modifier = Modifier.padding(top = 4.dp))
        }
        if (!enabled) {
            Text(
                text = "Consent required to submit feedback.",
                color = Color(0xFF6D6D6D),
                modifier = Modifier.padding(top = 2.dp)
            )
        }
    }
}

@Composable
private fun OfflineBanner(message: String) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp)
            .testTag("offline_banner"),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(text = message, color = Color(0xFF6D6D6D))
    }
}

@Composable
private fun AutoModeSection(
    modules: List<PackModule>,
    killSwitchEnabled: Boolean,
    survivalMode: String,
    offlineMode: Boolean,
    onActionClick: (PackAction) -> Unit
) {
    val autoModules = modules.filter { it.surface == "android_auto" }
    val targetModules = if (autoModules.isNotEmpty()) autoModules else modules
    val actions = targetModules.flatMap { it.actions }
    if (actions.isEmpty()) {
        Text("Android Auto safe-mode: no allowlisted actions", modifier = Modifier.padding(top = 12.dp))
        return
    }
    val restricted = killSwitchEnabled || survivalMode != "normal" || offlineMode
    Text("Android Auto safe-mode", modifier = Modifier.padding(top = 12.dp))
    if (restricted) {
        Text(
            text = "Actions restricted in survival/offline mode.",
            color = Color(0xFF6D6D6D),
            modifier = Modifier.padding(top = 4.dp)
        )
    }
    actions.forEach { action ->
        Card(
            modifier = Modifier
                .padding(top = 8.dp)
                .width(280.dp),
            colors = CardDefaults.cardColors(containerColor = Color(0xFF1B1B1B))
        ) {
            Column(modifier = Modifier.padding(12.dp)) {
                Text(text = action.label, color = Color(0xFFE0E0E0))
                Button(
                    onClick = { onActionClick(action) },
                    enabled = !restricted,
                    modifier = Modifier.padding(top = 8.dp)
                ) {
                    Text(if (restricted) "Disabled" else "Run")
                }
            }
        }
    }
}

@Composable
private fun TraceViewerDialog(traceJson: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text("Close")
            }
        },
        title = { Text("Trace Viewer") },
        text = {
            Column(
                modifier = Modifier
                    .verticalScroll(rememberScrollState())
                    .height(240.dp)
            ) {
                Text(traceJson)
            }
        }
    )
}

