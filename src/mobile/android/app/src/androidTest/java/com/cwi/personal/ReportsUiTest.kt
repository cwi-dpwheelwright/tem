package com.cwi.personal

import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performScrollTo
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.DocumentVersion
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import org.junit.Rule
import org.junit.Test

class ReportsUiTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun reportSummaryShowsBadgesAndApproval() {
        val report = ReportSummary(
            reportId = "report-1",
            title = "Ops Summary",
            createdUtc = "2026-02-01T10:00:00Z",
            status = "open",
            coherenceScore = 0.92,
            conflictCount = 2,
            summary = "Summary",
            escalationTicket = "ticket-1"
        )

        composeRule.setContent {
            HomeScreenContent(
                uiState = MainUiState(reports = listOf(report)),
                onVerifyPack = {},
                onToggleAutoMode = {},
                onUpdatePackName = {},
                onUpdateQuestion = {},
                onAsk = {},
                onInvokeFirstAction = {},
                onInvokeAction = {},
                onRefreshKillSwitch = {},
                onToggleConsent = {},
                onSubmitFeedback = {},
                onRefreshReports = {},
                onOpenReport = {},
                onRefreshDocuments = {},
                onOpenDocument = {},
                onOpenApproval = {}
            )
        }

        composeRule.onNodeWithText("Coherence 0.92").assertIsDisplayed()
        composeRule.onNodeWithText("Conflicts 2").assertIsDisplayed()
        composeRule.onNodeWithText("Approval").assertIsDisplayed()
    }

    @Test
    fun reportDetailShowsStatusAndNarrative() {
        val detail = ReportDetail(
            reportId = "report-2",
            title = "Incident Report",
            createdUtc = "2026-02-01T11:00:00Z",
            status = "open",
            coherenceScore = 0.7,
            conflictCount = 1,
            summary = "Summary",
            narrative = "Detailed narrative"
        )

        composeRule.setContent {
            HomeScreenContent(
                uiState = MainUiState(selectedReport = detail),
                onVerifyPack = {},
                onToggleAutoMode = {},
                onUpdatePackName = {},
                onUpdateQuestion = {},
                onAsk = {},
                onInvokeFirstAction = {},
                onInvokeAction = {},
                onRefreshKillSwitch = {},
                onToggleConsent = {},
                onSubmitFeedback = {},
                onRefreshReports = {},
                onOpenReport = {},
                onRefreshDocuments = {},
                onOpenDocument = {},
                onOpenApproval = {}
            )
        }

        composeRule.onNodeWithText("Report Detail").assertIsDisplayed()
        composeRule.onNodeWithText("Status: open").assertIsDisplayed()
        composeRule.onNodeWithText("Coherence 0.70").assertIsDisplayed()
        composeRule.onNodeWithText("Conflicts 1").assertIsDisplayed()
        composeRule.onNodeWithText("Detailed narrative").assertIsDisplayed()
    }

    @Test
    fun documentSummaryShowsLatestVersion() {
        val summary = DocumentSummary(
            docId = "doc-1",
            title = "Runbook",
            type = "ops",
            owner = "team",
            status = "active",
            versionsCount = 3,
            latestVersion = "v2"
        )
        val detail = DocumentDetail(
            docId = "doc-1",
            title = "Runbook",
            type = "ops",
            owner = "team",
            status = "active",
            versions = listOf(
                DocumentVersion(
                    version = "2",
                    createdUtc = "2026-02-01T09:00:00Z",
                    author = "ops",
                    checksum = "abc",
                    notes = "Signed",
                    content = "Content"
                )
            )
        )

        composeRule.setContent {
            HomeScreenContent(
                uiState = MainUiState(documents = listOf(summary), selectedDocument = detail),
                onVerifyPack = {},
                onToggleAutoMode = {},
                onUpdatePackName = {},
                onUpdateQuestion = {},
                onAsk = {},
                onInvokeFirstAction = {},
                onInvokeAction = {},
                onRefreshKillSwitch = {},
                onToggleConsent = {},
                onSubmitFeedback = {},
                onRefreshReports = {},
                onOpenReport = {},
                onRefreshDocuments = {},
                onOpenDocument = {},
                onOpenApproval = {}
            )
        }

        composeRule.onNodeWithText("Latest: v2 (v3)").performScrollTo()
        composeRule.onAllNodesWithText("Latest: v2 (v3)").assertCountEquals(1)
        composeRule.onAllNodesWithText("Runbook").assertCountEquals(2)
        composeRule.onNodeWithText("v2").performScrollTo().assertIsDisplayed()
        composeRule.onNodeWithText("Signed").performScrollTo().assertIsDisplayed()
    }
}
