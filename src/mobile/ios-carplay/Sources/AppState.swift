import Foundation
import Combine

@MainActor
final class AppState: ObservableObject {
    @Published var baseUrl: String = AppDefaults.baseUrl {
        didSet { AppDefaults.baseUrl = baseUrl }
    }
    @Published var jwtToken: String = ""
    @Published var tenantId: String = AppDefaults.tenantId {
        didSet { AppDefaults.tenantId = tenantId }
    }
    @Published var username: String = ""
    @Published var password: String = ""
    @Published var userId: String = AppDefaults.userId {
        didSet { AppDefaults.userId = userId }
    }
    @Published var packName: String = AppDefaults.packName {
        didSet { AppDefaults.packName = packName }
    }
    @Published var policyMode: String = AppDefaults.policyMode {
        didSet { AppDefaults.policyMode = policyMode }
    }
    @Published var question: String = ""
    @Published var answer: String = ""
    @Published var citations: [AskResponse.Citation] = []
    @Published var trace: AskResponse.Trace?
    @Published var lastAction: ActionResponse?
    @Published var approvalTicket: ApprovalTicket?
    @Published var approvalStatus: ApprovalTicketStatus?
    @Published var trustState: TrustState = .unknown
    @Published var packModules: [PackUiModule] = []
    @Published var offlineMode: Bool = AppDefaults.offlineMode {
        didSet { AppDefaults.offlineMode = offlineMode }
    }
    @Published var offlineMessage: String?
    @Published var survivalMode: String = AppDefaults.survivalMode {
        didSet { AppDefaults.survivalMode = survivalMode }
    }
    @Published var survivalProfile: String = ""
    @Published var survivalLastSignalUtc: String?
    @Published var survivalMessage: String?
    @Published var killSwitchEnabled: Bool = false
    @Published var killSwitchCachedAt: Date?
    @Published var consentGranted: Bool = AppDefaults.consentGranted {
        didSet { AppDefaults.consentGranted = consentGranted }
    }
    @Published var askHistory: [AskHistoryItem] = []
    @Published var feedbackQueueCount: Int = 0
    @Published var feedbackStatus: String?
    @Published var interactionQueueCount: Int = 0
    @Published var interactionStatus: String?
    @Published var lastError: String?
    @Published var lastActionId: String?
    @Published var reports: [ReportSummary] = []
    @Published var documents: [DocumentSummary] = []
    @Published var selectedReport: ReportDetail?
    @Published var selectedDocument: DocumentDetail?

    private let store = PackStore()
    private let sessionStore = SessionStore()
    private let killSwitchStore = KillSwitchStore()
    private let askHistoryStore = AskHistoryStore()
    private let feedbackUploader = FeedbackUploader()
    private let interactionUploader = InteractionUploader()
    private let isUiTesting: Bool

    init() {
        RuntimeConfigLoader.applyIfPresent()
        baseUrl = AppDefaults.baseUrl
        tenantId = AppDefaults.tenantId
        userId = AppDefaults.userId
        packName = AppDefaults.packName
        policyMode = AppDefaults.policyMode
        offlineMode = AppDefaults.offlineMode
        survivalMode = AppDefaults.survivalMode
        consentGranted = AppDefaults.consentGranted
        let env = ProcessInfo.processInfo.environment
        isUiTesting = env["UI_TEST_MODE"] == "1"
        CrashReporter.configure(enabled: AppConfig.shared.crashReportingEnabled)
        jwtToken = sessionStore.accessToken ?? ""
        feedbackQueueCount = FeedbackStore().size()
        interactionQueueCount = InteractionStore().size()
        askHistory = askHistoryStore.load()
        if let cached = killSwitchStore.loadCached() {
            killSwitchEnabled = cached.enabled
            killSwitchCachedAt = cached.cachedAt
        }
        if isUiTesting {
            applyUiTestOverrides(env: env)
        } else {
            Task {
                await refreshSurvivalStatus()
                await refreshKillSwitchStatus()
            }
        }
    }

    var actionRestrictionReason: String? {
        if killSwitchEnabled {
            return "Action disabled: kill switch active."
        }
        if survivalMode != "normal" {
            return "Action disabled: survival mode (\(survivalMode))."
        }
        if offlineMode {
            return "Action disabled: offline mode active."
        }
        return nil
    }

    func loadCachedPack() {
        guard !isUiTesting else { return }
        let cached = store.loadVerifiedPack()
        if let pack = cached.pack {
            trustState = cached.trustState
            packModules = PackParser.parseModules(from: pack.rawJson)
            offlineMode = true
            offlineMessage = "Using cached pack while offline."
        } else {
            trustState = cached.trustState
            offlineMode = true
            offlineMessage = cached.error
        }
    }

    func fetchPack() async {
        do {
            let fetcher = PackFetcher(baseUrl: baseUrl)
            let pack = try await fetcher.fetch(packName: packName)
            trustState = .verified
            packModules = PackParser.parseModules(from: pack.rawJson)
            store.savePack(pack)
            offlineMode = false
            offlineMessage = nil
            lastError = nil
            CrashReporter.shared.record(event: "pack_verify", context: [
                "pack_name": packName
            ])
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "fetch_pack",
                "pack_name": packName
            ])
            let cached = store.loadVerifiedPack()
            if let pack = cached.pack {
                trustState = cached.trustState
                packModules = PackParser.parseModules(from: pack.rawJson)
                offlineMode = true
                offlineMessage = "Using cached pack after verification failure."
            } else {
                trustState = cached.trustState
                offlineMode = true
                offlineMessage = cached.error ?? "Offline mode enabled."
            }
        }
    }

    func ask() async {
        do {
            let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
            let response = try await client.ask(
                tenantId: tenantId,
                userId: userId,
                question: question,
                packName: packName,
                policyMode: policyMode
            )
            answer = response.answer
            citations = response.citations
            trace = response.trace
            lastError = nil
            appendAskHistory(from: response)
            CrashReporter.shared.record(event: "ask", context: [
                "trace_id": response.trace.traceId ?? "unknown",
                "pack_name": packName
            ])
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "ask"
            ])
        }
    }

    func login() async {
        guard !username.isEmpty, !password.isEmpty else {
            lastError = "Username and password required."
            return
        }
        let client = APIClient(baseUrl: baseUrl, jwtToken: "")
        let email = username.contains("@") ? username : nil
        let loginName = username.contains("@") ? nil : username
        do {
            let response = try await client.login(
                tenantId: tenantId,
                username: loginName,
                email: email,
                password: password
            )
            jwtToken = response.accessToken
            sessionStore.update(accessToken: response.accessToken, refreshToken: response.refreshToken)
            password = ""
            lastError = nil
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "login"
            ])
        }
    }

    func invokeAction(_ action: PackAction) async {
        if let reason = actionRestrictionReason {
            lastError = reason
            return
        }
        do {
            let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
            let response = try await client.action(
                tenantId: tenantId,
                userId: userId,
                policyMode: policyMode,
                actionType: action.actionType,
                args: action.args,
                payload: action.payloadValue
            )
            lastAction = response
            approvalTicket = response.approvalTicket
            approvalStatus = nil
            lastActionId = action.id
            if let actionTrace = response.trace {
                trace = actionTrace
            }
            lastError = nil
            CrashReporter.shared.record(event: "action", context: [
                "action_type": action.actionType,
                "pack_name": packName
            ])
            if let ticket = response.approvalTicket?.ticketId {
                Task {
                    await pollApprovalStatus(ticketId: ticket)
                }
            }
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "invoke_action",
                "action_type": action.actionType
            ])
        }
    }

    private func pollApprovalStatus(ticketId: String, maxAttempts: Int = 12) async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        for _ in 0..<maxAttempts {
            do {
                let status = try await client.approvalStatus(
                    ticketId: ticketId,
                    tenantId: tenantId,
                    roles: "approver"
                )
                approvalStatus = status
                if !status.status.lowercased().contains("pending") {
                    return
                }
            } catch {
                lastError = error.localizedDescription
                CrashReporter.shared.record(error: error, context: [
                    "action": "approval_status",
                    "ticket_id": ticketId
                ])
            }
            try? await Task.sleep(nanoseconds: 5_000_000_000)
        }
    }

    func refreshApprovalStatus() async {
        guard let ticketId = approvalTicket?.ticketId else {
            return
        }
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let status = try await client.approvalStatus(
                ticketId: ticketId,
                tenantId: tenantId,
                roles: "approver"
            )
            approvalStatus = status
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "approval_status_refresh",
                "ticket_id": ticketId
            ])
        }
    }

    func refreshSurvivalStatus() async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let status = try await client.survivalStatus()
            survivalMode = status.mode
            survivalProfile = status.profile
            survivalLastSignalUtc = status.lastSignalUtc
            survivalMessage = status.offlineMessage
            if status.mode != "normal" {
                if let offline = try? await client.offlineResponse() {
                    survivalMessage = offline.message
                }
            }
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "survival_status"
            ])
        }
    }

    func refreshKillSwitchStatus(force: Bool = false) async {
        if !force,
           !killSwitchStore.isStale(maxAgeMinutes: AppConfig.shared.killSwitchCacheMinutes),
           let cached = killSwitchStore.loadCached() {
            killSwitchEnabled = cached.enabled
            killSwitchCachedAt = cached.cachedAt
            return
        }
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let response = try await client.killSwitchStatus()
            let cachedAt = Date()
            killSwitchStore.save(enabled: response.enabled, cachedAt: cachedAt)
            killSwitchEnabled = response.enabled
            killSwitchCachedAt = cachedAt
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "kill_switch_status"
            ])
        }
    }

    func toggleConsent(_ granted: Bool) {
        consentGranted = granted
        if granted {
            Task {
                let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
                let remaining = await feedbackUploader.flush(client: client)
                await MainActor.run {
                    feedbackQueueCount = remaining
                    feedbackStatus = remaining == 0 ? "Feedback uploaded." : "Feedback queued."
                }
                let remainingInteractions = await interactionUploader.flush(client: client)
                await MainActor.run {
                    interactionQueueCount = remainingInteractions
                    interactionStatus = remainingInteractions == 0 ? "Interactions uploaded." : "Interactions queued."
                }
            }
        }
    }

    func submitFeedback(isPositive: Bool, reason: String?) {
        guard consentGranted else {
            lastError = "Consent required to submit feedback."
            return
        }
        guard let traceId = trace?.traceId, !traceId.isEmpty else {
            lastError = "Trace ID required for feedback."
            return
        }

        let request = OutcomeFeedbackRequest(
            tenantId: tenantId,
            traceId: traceId,
            ticketId: approvalTicket?.ticketId,
            actionId: lastActionId,
            actorId: userId,
            idempotencyKey: UUID().uuidString,
            feedback: isPositive ? "up" : "down",
            consentGranted: true,
            reasonCodes: [isPositive ? "helpful" : "needs_improvement"],
            note: reason,
            confidenceBefore: nil,
            confidenceAfter: nil
        )

        let event = OutcomeFeedbackEvent(eventId: UUID().uuidString, request: request)
        Task {
            let count = await feedbackUploader.enqueue(event, limit: AppConfig.shared.feedbackQueueLimit)
            await MainActor.run {
                feedbackQueueCount = count
                feedbackStatus = "Feedback queued for upload."
            }
            let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
            let remaining = await feedbackUploader.flush(client: client)
            await MainActor.run {
                feedbackQueueCount = remaining
                feedbackStatus = remaining == 0 ? "Feedback uploaded." : "Feedback queued."
            }
        }
    }

    func submitInteraction(eventType: String, correctionNote: String?, delaySeconds: Double?) {
        guard consentGranted else {
            lastError = "Consent required to capture interactions."
            return
        }
        guard let traceId = trace?.traceId, !traceId.isEmpty else {
            lastError = "Trace ID required to capture interactions."
            return
        }

        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let event = InteractionCaptureEvent(
            eventId: UUID().uuidString,
            tenantId: tenantId,
            userId: userId,
            traceId: traceId,
            actionId: lastActionId,
            eventType: eventType,
            occurredUtc: formatter.string(from: Date()),
            delaySeconds: delaySeconds,
            correctionNote: correctionNote
        )

        Task {
            let count = await interactionUploader.enqueue(event, limit: AppConfig.shared.interactionQueueLimit)
            await MainActor.run {
                interactionQueueCount = count
                interactionStatus = "Interaction queued for upload."
            }
            let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
            let remaining = await interactionUploader.flush(client: client)
            await MainActor.run {
                interactionQueueCount = remaining
                interactionStatus = remaining == 0 ? "Interactions uploaded." : "Interactions queued."
            }
        }
    }

    func refreshReports() async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let items = try await client.listReports(tenantId: tenantId)
            reports = items
            lastError = nil
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "list_reports"
            ])
        }
    }

    func loadReport(reportId: String) async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let report = try await client.getReport(reportId: reportId, tenantId: tenantId)
            selectedReport = report
            lastError = nil
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "get_report",
                "report_id": reportId
            ])
        }
    }

    func refreshDocuments() async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let items = try await client.listDocuments(tenantId: tenantId)
            documents = items
            lastError = nil
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "list_documents"
            ])
        }
    }

    func loadDocument(docId: String) async {
        let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken)
        do {
            let document = try await client.getDocument(docId: docId, tenantId: tenantId)
            selectedDocument = document
            lastError = nil
        } catch {
            lastError = error.localizedDescription
            CrashReporter.shared.record(error: error, context: [
                "action": "get_document",
                "doc_id": docId
            ])
        }
    }

    func loadAskHistoryItem(_ item: AskHistoryItem) {
        question = item.question
        answer = item.answer
        citations = item.citations
        trace = item.trace
    }

    func logout() {
        if let refresh = sessionStore.refreshToken {
            Task {
                let client = APIClient(baseUrl: baseUrl, jwtToken: "")
                _ = try? await client.logout(refreshToken: refresh)
            }
        }
        sessionStore.logout()
        jwtToken = ""
        approvalTicket = nil
        approvalStatus = nil
        lastAction = nil
        lastActionId = nil
        trace = nil
        answer = ""
        citations = []
        askHistoryStore.clear()
        askHistory = []
    }

    private func appendAskHistory(from response: AskResponse) {
        let item = AskHistoryItem(
            id: UUID(),
            question: question,
            answer: response.answer,
            citations: response.citations,
            trace: response.trace,
            timestamp: Date()
        )
        askHistory.insert(item, at: 0)
        if askHistory.count > 50 {
            askHistory = Array(askHistory.prefix(50))
        }
        askHistoryStore.save(items: askHistory)
    }

    private func applyUiTestOverrides(env: [String: String]) {
        if let trustRaw = env["UI_TEST_TRUST_STATE"],
           let trust = TrustState(rawValue: trustRaw.capitalized) {
            trustState = trust
        }
        if env["UI_TEST_OFFLINE"] == "1" {
            offlineMode = true
            offlineMessage = "Offline mode active."
        }
        if env["UI_TEST_KILL_SWITCH"] == "1" {
            killSwitchEnabled = true
            killSwitchCachedAt = Date()
        }
        if env["UI_TEST_APPROVAL_TICKET"] == "1" {
            let ticketId = env["UI_TEST_APPROVAL_TICKET_ID"] ?? "ui-test-ticket"
            approvalTicket = ApprovalTicket(
                ticketId: ticketId,
                risk: env["UI_TEST_APPROVAL_RISK"] ?? "high",
                reason: env["UI_TEST_APPROVAL_REASON"] ?? "ui-test"
            )
            if let statusRaw = env["UI_TEST_APPROVAL_STATUS"], !statusRaw.isEmpty {
                let formatter = ISO8601DateFormatter()
                formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
                approvalStatus = ApprovalTicketStatus(
                    ticketId: ticketId,
                    status: statusRaw,
                    risk: env["UI_TEST_APPROVAL_RISK"] ?? "high",
                    reason: env["UI_TEST_APPROVAL_REASON"] ?? "ui-test",
                    updatedUtc: formatter.string(from: Date())
                )
            }
        }
        let sampleAction = PackAction(
            id: "ui-test-action",
            label: "Test Action",
            actionType: "test_action",
            args: nil,
            payload: .object(["mode": .string("test")])
        )
        let module = PackUiModule(
            id: "ui-test-tile",
            type: "tile",
            title: "Test Tile",
            surface: "ios",
            actions: [sampleAction],
            data: nil,
            children: nil
        )
        packModules = [module]
    }
}

final class AskHistoryStore {
    private let url: URL

    init() {
        let base = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first!
        url = base.appendingPathComponent("ask-history.json")
    }

    func load() -> [AskHistoryItem] {
        guard let data = try? Data(contentsOf: url) else { return [] }
        return (try? JSONDecoder().decode([AskHistoryItem].self, from: data)) ?? []
    }

    func save(items: [AskHistoryItem]) {
        guard let data = try? JSONEncoder().encode(items) else { return }
        try? data.write(to: url, options: .atomic)
    }

    func clear() {
        try? FileManager.default.removeItem(at: url)
    }
}
