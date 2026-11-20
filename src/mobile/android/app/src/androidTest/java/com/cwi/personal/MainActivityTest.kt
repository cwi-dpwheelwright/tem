package com.cwi.personal

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import org.junit.Rule
import org.junit.Test
import com.cwi.personal.pack.TrustState

class MainActivityTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun trustBannerShowsUnknownByDefault() {
        composeRule.setContent {
            HomeScreenContent(
                uiState = MainUiState(),
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
        composeRule.onNodeWithText("Pack trust: Unknown").assertIsDisplayed()
    }

    @Test
    fun trustBannerShowsVerifiedAndOffline() {
        val state = MainUiState(trustState = TrustState.VERIFIED, offlineMode = true)
        composeRule.setContent {
            HomeScreenContent(
                uiState = state,
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
        composeRule.onNodeWithText("Pack trust: Verified").assertIsDisplayed()
        composeRule.onNodeWithText("Offline mode active. Using cached pack.").assertIsDisplayed()
    }

    @Test
    fun trustBannerShowsUnverified() {
        val state = MainUiState(trustState = TrustState.UNVERIFIED)
        composeRule.setContent {
            HomeScreenContent(
                uiState = state,
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
        composeRule.onNodeWithText("Pack trust: Unverified").assertIsDisplayed()
    }

    @Test
    fun autoModeHidesAskInputs() {
        val state = MainUiState(autoModeEnabled = true)
        composeRule.setContent {
            HomeScreenContent(
                uiState = state,
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
        composeRule.onNodeWithText("Android Auto safe-mode: no allowlisted actions").assertIsDisplayed()
        composeRule.onAllNodesWithText("Ask question").assertCountEquals(0)
    }

    @Test
    fun traceViewerShowsRawJson() {
        val trace = com.cwi.personal.ask.AskResponse.Trace(traceId = "trace-123", policyMode = "approve")
        val state = MainUiState(trace = trace, traceJson = "{\"trace_id\":\"trace-123\"}")
        composeRule.setContent {
            HomeScreenContent(
                uiState = state,
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
        composeRule.onNodeWithText("Open Trace Viewer").performClick()
        composeRule.onNodeWithText("Trace Viewer").assertIsDisplayed()
        composeRule.onNodeWithText("{\"trace_id\":\"trace-123\"}").assertIsDisplayed()
    }
}
