package com.cwi.personal

import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import com.cwi.personal.pack.PackAction
import com.cwi.personal.pack.PackModule
import org.junit.Rule
import org.junit.Test

class KillSwitchUiTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun actionButtonDisabledWhenKillSwitchActive() {
        val module = PackModule(
            id = "module-1",
            type = "module",
            title = "Module",
            surface = null,
            actions = listOf(
                PackAction(id = "action-1", label = "Action", actionType = "act")
            )
        )

        composeRule.setContent {
            HomeScreenContent(
                uiState = MainUiState(
                    killSwitchEnabled = true,
                    modules = listOf(module),
                    survivalMode = "normal",
                    offlineMode = false
                ),
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

        composeRule.onNodeWithTag("invoke_action_button").assertIsNotEnabled()
    }
}
