import SwiftUI

struct ContentView: View {
    @StateObject private var state = AppState()

    var body: some View {
        VStack(spacing: 0) {
            TrustBannerView(state: state.trustState)
            KillSwitchBannerView(enabled: state.killSwitchEnabled, cachedAt: state.killSwitchCachedAt)
            if state.survivalMode != "normal" {
                SurvivalBannerView(
                    mode: state.survivalMode,
                    profile: state.survivalProfile,
                    message: state.survivalMessage
                )
            }
            if state.offlineMode {
                OfflineBannerView(message: state.offlineMessage)
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    configurationSection
                    AskView(state: state)
                    InteractionCaptureView(state: state)
                    if !state.packModules.isEmpty {
                        Text("Pack Modules")
                            .font(.headline)
                        ModuleRenderer(
                            modules: state.packModules,
                            actionDisabled: state.actionRestrictionReason != nil,
                            actionDisabledReason: state.actionRestrictionReason,
                            baseUrl: state.baseUrl,
                            jwtToken: state.jwtToken,
                            fixtureStore: FixtureStore()
                        ) { action in
                            Task {
                                await state.invokeAction(action)
                            }
                        }
                    }
                    ReportsDocumentsView(state: state)
                    if let error = state.lastError {
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
                .padding()
            }
        }
        .onAppear {
            state.loadCachedPack()
        }
    }

    private var configurationSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Operations")
                .font(.headline)
            HStack(spacing: 12) {
                Button("Fetch Pack") {
                    Task {
                        await state.fetchPack()
                    }
                }
                .buttonStyle(.borderedProminent)

                Button("Use Cached Pack") {
                    state.loadCachedPack()
                }
                .buttonStyle(.bordered)
            }

            Text("Status")
                .font(.headline)
            HStack(spacing: 12) {
                Button("Refresh Survival") {
                    Task {
                        await state.refreshSurvivalStatus()
                    }
                }
                .buttonStyle(.bordered)

                Button("Refresh Kill Switch") {
                    Task {
                        await state.refreshKillSwitchStatus(force: true)
                    }
                }
                .buttonStyle(.bordered)
            }

            Text("Session")
                .font(.headline)
            TextField("Username or Email", text: $state.username)
                .textFieldStyle(.roundedBorder)
            SecureField("Password", text: $state.password)
                .textFieldStyle(.roundedBorder)
            HStack(spacing: 12) {
                Button("Login") {
                    Task {
                        await state.login()
                    }
                }
                .buttonStyle(.borderedProminent)

                if !state.jwtToken.isEmpty {
                    Button("Logout") {
                        state.logout()
                    }
                    .buttonStyle(.bordered)
                }
            }
        }
    }
}

#Preview {
    ContentView()
}
