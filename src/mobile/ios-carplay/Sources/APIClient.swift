import Foundation

struct APIClient {
    let baseUrl: String
    let jwtToken: String
    private let config: AppConfig
    private let session: URLSession

    init(baseUrl: String, jwtToken: String, session: URLSession? = nil, config: AppConfig = .shared) {
        self.baseUrl = baseUrl
        self.jwtToken = jwtToken
        self.config = config
        self.session = session ?? NetworkSessionFactory.makeSession(config: config)
    }

    private var headers: [String: String] {
        var result: [String: String] = [
            "Content-Type": "application/json"
        ]
        if !jwtToken.isEmpty {
            result["Authorization"] = "Bearer \(jwtToken)"
        }
        return result
    }

    func ask(
        tenantId: String,
        userId: String,
        question: String,
        packName: String,
        policyMode: String
    ) async throws -> AskResponse {
        let request = AskRequest(
            tenantId: tenantId,
            userId: userId,
            question: question,
            pack: AskRequest.PackRef(name: packName),
            context: AskRequest.Context(mode: policyMode, topK: nil)
        )
        return try await post(path: "/api/ask", body: request)
    }

    func login(
        tenantId: String?,
        username: String?,
        email: String?,
        password: String
    ) async throws -> TokenResponse {
        let request = LoginRequest(
            tenantId: tenantId,
            email: email,
            username: username,
            password: password
        )
        return try await post(path: "/auth/login", body: request)
    }

    func logout(refreshToken: String) async throws {
        let request = LogoutRequest(refreshToken: refreshToken)
        let _: EmptyResponse = try await post(path: "/auth/logout", body: request)
    }

    func action(
        tenantId: String,
        userId: String,
        policyMode: String,
        actionType: String,
        args: [String: String]? = nil,
        payload: JSONValue? = nil
    ) async throws -> ActionResponse {
        let resolvedPayload: JSONValue?
        if let payload {
            resolvedPayload = payload
        } else if let args {
            resolvedPayload = .object(args.mapValues { .string($0) })
        } else {
            resolvedPayload = nil
        }
        let request = ActionRequest(
            tenantId: tenantId,
            userId: userId,
            policyMode: policyMode,
            action: ActionRequest.ActionPayload(type: actionType, payload: resolvedPayload)
        )
        return try await post(path: "/api/action", body: request)
    }

    func approvalStatus(ticketId: String, tenantId: String, roles: String) async throws -> ApprovalTicketStatus {
        let extraHeaders = [
            "x-tenant-id": tenantId,
            "x-roles": roles
        ]
        return try await get(path: "/api/approval/\(ticketId)", extraHeaders: extraHeaders)
    }

    func killSwitchStatus() async throws -> KillSwitchStatusResponse {
        return try await get(path: "/api/admin/kill_switch")
    }

    func survivalStatus() async throws -> SurvivalStatusResponse {
        return try await get(path: "/api/survival/status")
    }

    func offlineResponse() async throws -> OfflineResponse {
        return try await get(path: "/api/survival/offline-response")
    }

    func recordFeedback(request: OutcomeFeedbackRequest) async throws -> OutcomeFeedbackResponse {
        return try await post(path: "/api/feedback", body: request)
    }

    func ingestTelemetry(request: TelemetryIngestRequest) async throws -> TelemetryIngestResponse {
        return try await post(path: "/api/ingest/telemetry", body: request)
    }

    func listReports(tenantId: String) async throws -> [ReportSummary] {
        return try await get(path: "/api/reports", query: [URLQueryItem(name: "tenant_id", value: tenantId)])
    }

    func getReport(reportId: String, tenantId: String) async throws -> ReportDetail {
        return try await get(path: "/api/reports/\(reportId)", query: [URLQueryItem(name: "tenant_id", value: tenantId)])
    }

    func listDocuments(tenantId: String) async throws -> [DocumentSummary] {
        return try await get(path: "/api/documents", query: [URLQueryItem(name: "tenant_id", value: tenantId)])
    }

    func getDocument(docId: String, tenantId: String) async throws -> DocumentDetail {
        return try await get(path: "/api/documents/\(docId)", query: [URLQueryItem(name: "tenant_id", value: tenantId)])
    }

    func fetchJson(path: String) async throws -> JSONValue {
        let responseData: Data = try await sendRequest(path: path, method: "GET", data: nil)
        return try JSONDecoder().decode(JSONValue.self, from: responseData)
    }

    private func post<T: Codable, R: Codable>(path: String, body: T) async throws -> R {
        let data = try JSONEncoder().encode(body)
        let responseData: Data = try await sendRequest(path: path, method: "POST", data: data)
        return try JSONDecoder().decode(R.self, from: responseData)
    }

    private func get<R: Codable>(path: String, extraHeaders: [String: String] = [:]) async throws -> R {
        let responseData: Data = try await sendRequest(path: path, method: "GET", data: nil, extraHeaders: extraHeaders)
        return try JSONDecoder().decode(R.self, from: responseData)
    }

    private func get<R: Codable>(
        path: String,
        query: [URLQueryItem],
        extraHeaders: [String: String] = [:]
    ) async throws -> R {
        let responseData: Data = try await sendRequest(
            path: path,
            method: "GET",
            data: nil,
            extraHeaders: extraHeaders,
            queryItems: query
        )
        return try JSONDecoder().decode(R.self, from: responseData)
    }

    private func sendRequest(
        path: String,
        method: String,
        data: Data?,
        extraHeaders: [String: String] = [:],
        queryItems: [URLQueryItem] = []
    ) async throws -> Data {
        if config.simulateOffline {
            throw URLError(.notConnectedToInternet)
        }
        if config.simulateBackpressureMs > 0 {
            try await Task.sleep(nanoseconds: config.simulateBackpressureMs * 1_000_000)
        }

        let maxAttempts = max(1, config.apiRetryBudget + 1)
        var lastError: Error?
        for attempt in 1...maxAttempts {
            do {
                let responseData = try await performRequest(
                    path: path,
                    method: method,
                    data: data,
                    extraHeaders: extraHeaders,
                    queryItems: queryItems
                )
                return responseData
            } catch {
                lastError = error
                if attempt == maxAttempts || !isRetryable(error: error) {
                    break
                }
                let backoffMs = UInt64(200 * attempt)
                try? await Task.sleep(nanoseconds: backoffMs * 1_000_000)
            }
        }
        throw lastError ?? URLError(.unknown)
    }

    private func performRequest(
        path: String,
        method: String,
        data: Data?,
        extraHeaders: [String: String],
        queryItems: [URLQueryItem]
    ) async throws -> Data {
        guard var components = URLComponents(string: baseUrl + path) else {
            throw URLError(.badURL)
        }
        if !queryItems.isEmpty {
            components.queryItems = queryItems
        }
        guard let url = components.url else {
            throw URLError(.badURL)
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.httpBody = data
        headers.forEach { request.setValue($1, forHTTPHeaderField: $0) }
        extraHeaders.forEach { request.setValue($1, forHTTPHeaderField: $0) }

        let (responseData, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw URLError(.badServerResponse)
        }
        guard (200..<300).contains(http.statusCode) else {
            let errorBody = String(data: responseData, encoding: .utf8) ?? ""
            throw NSError(domain: "APIClient", code: http.statusCode, userInfo: [
                NSLocalizedDescriptionKey: errorBody
            ])
        }
        return responseData
    }

    private func isRetryable(error: Error) -> Bool {
        if let urlError = error as? URLError {
            switch urlError.code {
            case .timedOut,
                 .cannotFindHost,
                 .cannotConnectToHost,
                 .networkConnectionLost,
                 .dnsLookupFailed,
                 .notConnectedToInternet:
                return true
            default:
                return false
            }
        }
        let nsError = error as NSError
        if nsError.domain == "APIClient" {
            return nsError.code >= 500 && nsError.code < 600
        }
        return false
    }
}

private struct EmptyResponse: Codable {}
