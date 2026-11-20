import XCTest
@testable import CwiIosCarplay

final class APIClientTests: XCTestCase {
    override func tearDown() {
        URLProtocolStub.handler = nil
        super.tearDown()
    }

    func testAskSendsAuthAndBody() async throws {
        let expectedAnswer = "ok"
        let response = AskResponse(
            answer: expectedAnswer,
            citations: [],
            trace: AskResponse.Trace(
                traceId: "t1",
                sources: ["s1"],
                confidence: 0.5,
                policyMode: "observe",
                model: "pro",
                packHash: "hash",
                otelTraceId: nil
            )
        )
        let responseData = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/ask")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer token")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")

            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(AskRequest.self, from: body)
            XCTAssertEqual(decoded.tenantId, "tenant")
            XCTAssertEqual(decoded.userId, "user")
            XCTAssertEqual(decoded.question, "Hello")
            XCTAssertEqual(decoded.pack.name, "pack")
            XCTAssertEqual(decoded.context.mode, "observe")

            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: responseData)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "token",
            session: makeTestSession()
        )
        let result = try await client.ask(
            tenantId: "tenant",
            userId: "user",
            question: "Hello",
            packName: "pack",
            policyMode: "observe"
        )
        XCTAssertEqual(result.answer, expectedAnswer)
    }

    func testLoginSendsUsernamePassword() async throws {
        let response = TokenResponse(
            accessToken: "access",
            refreshToken: "refresh",
            tokenType: "Bearer",
            expiresUtc: "2026-02-01T12:00:00Z"
        )
        let responseData = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/auth/login")
            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(LoginRequest.self, from: body)
            XCTAssertEqual(decoded.tenantId, "tenant-1")
            XCTAssertEqual(decoded.username, "operator")
            XCTAssertEqual(decoded.password, "secret")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: responseData)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.login(
            tenantId: "tenant-1",
            username: "operator",
            email: nil,
            password: "secret"
        )
        XCTAssertEqual(result.accessToken, "access")
    }

    func testActionReturnsResponse() async throws {
        let response = ActionResponse(
            executed: true,
            approvalTicket: ApprovalTicket(ticketId: "ticket-1", risk: nil, reason: "approval required"),
            result: ["ok": .bool(true)],
            trace: AskResponse.Trace(
                traceId: "t2",
                sources: [],
                confidence: nil,
                policyMode: nil,
                model: nil,
                packHash: nil,
                otelTraceId: nil
            )
        )
        let payload = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/action")
            XCTAssertEqual(request.httpMethod, "POST")
            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(ActionRequest.self, from: body)
            XCTAssertEqual(decoded.action.type, "do_thing")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.action(
            tenantId: "tenant",
            userId: "user",
            policyMode: "approve",
            actionType: "do_thing",
            args: ["k": "v"]
        )
        XCTAssertEqual(result.approvalTicket?.ticketId, "ticket-1")
    }

    func testApprovalStatusSendsHeaders() async throws {
        let status = ApprovalTicketStatus(
            ticketId: "ticket-1",
            status: "pending",
            risk: "high",
            reason: "approval required",
            updatedUtc: "2026-02-01T10:00:00Z"
        )
        let payload = try JSONEncoder().encode(status)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/approval/ticket-1")
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.value(forHTTPHeaderField: "x-tenant-id"), "tenant-1")
            XCTAssertEqual(request.value(forHTTPHeaderField: "x-roles"), "approver")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.approvalStatus(ticketId: "ticket-1", tenantId: "tenant-1", roles: "approver")
        XCTAssertEqual(result.status, "pending")
    }

    func testKillSwitchStatusReturnsEnabled() async throws {
        let payload = try JSONEncoder().encode(KillSwitchStatusResponse(enabled: true))

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/admin/kill_switch")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.killSwitchStatus()
        XCTAssertTrue(result.enabled)
    }

    func testRetryBudgetRetriesOnServerError() async throws {
        var attempts = 0
        let response = AskResponse(
            answer: "ok",
            citations: [],
            trace: AskResponse.Trace(
                traceId: "t1",
                sources: [],
                confidence: nil,
                policyMode: nil,
                model: nil,
                packHash: nil,
                otelTraceId: nil
            )
        )
        let payload = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            attempts += 1
            let statusCode = attempts < 3 ? 500 : 200
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: statusCode,
                httpVersion: nil,
                headerFields: nil
            )
            let data = statusCode == 200 ? payload : Data("error".utf8)
            return URLProtocolStub.Stub(response: response!, data: data)
        }

        let config = AppConfig(apiTimeoutSeconds: 1, apiRetryBudget: 2)
        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "token",
            session: makeTestSession(),
            config: config
        )
        let result = try await client.ask(
            tenantId: "tenant",
            userId: "user",
            question: "Hello",
            packName: "pack",
            policyMode: "observe"
        )
        XCTAssertEqual(result.answer, "ok")
        XCTAssertEqual(attempts, 3)
    }

    func testOfflineModeThrows() async {
        let config = AppConfig(simulateOffline: true)
        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "token",
            session: makeTestSession(),
            config: config
        )
        do {
            _ = try await client.ask(
                tenantId: "tenant",
                userId: "user",
                question: "Hello",
                packName: "pack",
                policyMode: "observe"
            )
            XCTFail("Expected offline error")
        } catch {
            XCTAssertTrue(error is URLError)
        }
    }

    func testSendRequestRetriesOnServerError() async throws {
        let response = AskResponse(
            answer: "ok",
            citations: [],
            trace: AskResponse.Trace(
                traceId: "t1",
                sources: [],
                confidence: nil,
                policyMode: nil,
                model: nil,
                packHash: nil,
                otelTraceId: nil
            )
        )
        let responseData = try JSONEncoder().encode(response)
        var callCount = 0

        URLProtocolStub.handler = { request in
            callCount += 1
            let statusCode = callCount == 1 ? 500 : 200
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: statusCode,
                httpVersion: nil,
                headerFields: nil
            )
            let data = statusCode == 200 ? responseData : Data("error".utf8)
            return URLProtocolStub.Stub(response: response!, data: data)
        }

        let config = AppConfig(apiRetryBudget: 1)
        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession(),
            config: config
        )
        let result = try await client.ask(
            tenantId: "tenant",
            userId: "user",
            question: "Hello",
            packName: "pack",
            policyMode: "observe"
        )
        XCTAssertEqual(result.answer, "ok")
        XCTAssertEqual(callCount, 2)
    }

    func testSendRequestReturnsOfflineErrorWhenSimulated() async {
        let config = AppConfig(simulateOffline: true)
        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession(),
            config: config
        )
        await XCTAssertThrowsErrorAsync {
            _ = try await client.ask(
                tenantId: "tenant",
                userId: "user",
                question: "Hello",
                packName: "pack",
                policyMode: "observe"
            )
        } handler: { error in
            XCTAssertEqual((error as? URLError)?.code, .notConnectedToInternet)
        }
    }

    func testNetworkSessionFactoryUsesTimeouts() {
        let config = AppConfig(apiTimeoutSeconds: 7)
        let session = NetworkSessionFactory.makeSession(config: config)
        XCTAssertEqual(session.configuration.timeoutIntervalForRequest, 7)
        XCTAssertEqual(session.configuration.timeoutIntervalForResource, 14)
    }

    func testListReportsAddsTenantQuery() async throws {
        let payload = try JSONEncoder().encode([
            ReportSummary(
                reportId: "rep-1",
                title: "Report",
                createdUtc: "2026-02-01T12:00:00Z",
                status: "approved",
                coherenceScore: 0.9,
                conflictCount: 0,
                summary: "ok",
                escalationTicket: nil
            )
        ])

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/reports")
            XCTAssertEqual(request.url?.query, "tenant_id=tenant-1")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.listReports(tenantId: "tenant-1")
        XCTAssertEqual(result.first?.reportId, "rep-1")
    }

    func testGetReportAddsTenantQuery() async throws {
        let payload = try JSONEncoder().encode(
            ReportDetail(
                reportId: "rep-1",
                title: "Report",
                createdUtc: "2026-02-01T12:00:00Z",
                status: "approved",
                coherenceScore: 0.9,
                conflictCount: 1,
                summary: "ok",
                escalationTicket: "ticket-1",
                narrative: "detail",
                structured: ["key": .string("value")],
                evidence: [ReportEvidence(label: "Trace", value: "trace-1")]
            )
        )

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/reports/rep-1")
            XCTAssertEqual(request.url?.query, "tenant_id=tenant-1")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.getReport(reportId: "rep-1", tenantId: "tenant-1")
        XCTAssertEqual(result.reportId, "rep-1")
    }

    func testListDocumentsAddsTenantQuery() async throws {
        let payload = try JSONEncoder().encode([
            DocumentSummary(
                docId: "doc-1",
                title: "Document",
                type: "policy",
                owner: "ops",
                status: "active",
                versionsCount: 2,
                latestVersion: "1.0"
            )
        ])

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/documents")
            XCTAssertEqual(request.url?.query, "tenant_id=tenant-1")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.listDocuments(tenantId: "tenant-1")
        XCTAssertEqual(result.first?.docId, "doc-1")
    }

    func testGetDocumentAddsTenantQuery() async throws {
        let payload = try JSONEncoder().encode(
            DocumentDetail(
                docId: "doc-1",
                title: "Document",
                type: "policy",
                owner: "ops",
                status: "active",
                versions: [
                    DocumentVersion(
                        version: "1.0",
                        createdUtc: "2026-02-01T12:00:00Z",
                        author: "ops",
                        checksum: "abc",
                        notes: nil,
                        content: "hello"
                    )
                ]
            )
        )

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/documents/doc-1")
            XCTAssertEqual(request.url?.query, "tenant_id=tenant-1")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.getDocument(docId: "doc-1", tenantId: "tenant-1")
        XCTAssertEqual(result.docId, "doc-1")
    }

    func testTelemetryIngestPostsPayload() async throws {
        let response = TelemetryIngestResponse(
            accepted: true,
            ingestId: "ing-1",
            trace: AskResponse.Trace(
                traceId: "t1",
                sources: [],
                confidence: nil,
                policyMode: nil,
                model: nil,
                packHash: nil,
                otelTraceId: nil
            )
        )
        let payload = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/ingest/telemetry")
            XCTAssertEqual(request.httpMethod, "POST")
            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(TelemetryIngestRequest.self, from: body)
            XCTAssertEqual(decoded.tenantId, "tenant-1")
            XCTAssertEqual(decoded.signals.first?.tag, "confirmation")

            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let request = TelemetryIngestRequest(
            tenantId: "tenant-1",
            window: TelemetryWindow(start: "2026-02-01T12:00:00Z", end: "2026-02-01T12:01:00Z"),
            signals: [TelemetrySignal(tag: "confirmation", unit: "count", series: [1])],
            metadata: ["source": .string("ios")]
        )

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let result = try await client.ingestTelemetry(request: request)
        XCTAssertTrue(result.accepted)
    }
}

final class UploaderTests: XCTestCase {
    override func tearDown() {
        URLProtocolStub.handler = nil
        super.tearDown()
    }

    func testFeedbackUploaderFlushClearsQueue() async throws {
        removeQueueFile(named: "feedback.queue.json")
        let request = OutcomeFeedbackRequest(
            tenantId: "tenant-1",
            traceId: "trace-1",
            ticketId: nil,
            actionId: nil,
            actorId: "user-1",
            idempotencyKey: "idem-1",
            feedback: "up",
            consentGranted: true,
            reasonCodes: ["helpful"],
            note: "ok",
            confidenceBefore: nil,
            confidenceAfter: nil
        )
        let event = OutcomeFeedbackEvent(eventId: "event-1", request: request)
        let store = FeedbackStore()
        _ = store.enqueue(event, limit: 5)

        let response = OutcomeFeedbackResponse(
            recordedUtc: "2026-02-01T12:00:00Z",
            feedback: "up",
            traceId: "trace-1",
            summary: OutcomeFeedbackSummary(
                upCount: 1,
                downCount: 0,
                lastFeedback: "up",
                lastFeedbackUtc: "2026-02-01T12:00:00Z"
            )
        )
        let payload = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/feedback")
            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(OutcomeFeedbackRequest.self, from: body)
            XCTAssertEqual(decoded.traceId, "trace-1")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let uploader = FeedbackUploader(store: store)
        let remaining = await uploader.flush(client: client)
        XCTAssertEqual(remaining, 0)
        XCTAssertEqual(store.size(), 0)
        removeQueueFile(named: "feedback.queue.json")
    }

    func testInteractionUploaderFlushBuildsTelemetry() async throws {
        removeQueueFile(named: "interaction.queue.json")
        let event = InteractionCaptureEvent(
            eventId: "event-1",
            tenantId: "tenant-1",
            userId: "user-1",
            traceId: "trace-1",
            actionId: nil,
            eventType: "confirmation",
            occurredUtc: "2026-02-01T12:00:00Z",
            delaySeconds: nil,
            correctionNote: nil
        )
        let store = InteractionStore()
        _ = store.enqueue(event, limit: 5)

        let response = TelemetryIngestResponse(
            accepted: true,
            ingestId: "ing-1",
            trace: AskResponse.Trace(
                traceId: "t1",
                sources: [],
                confidence: nil,
                policyMode: nil,
                model: nil,
                packHash: nil,
                otelTraceId: nil
            )
        )
        let payload = try JSONEncoder().encode(response)

        URLProtocolStub.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/ingest/telemetry")
            let body = try XCTUnwrap(request.httpBody ?? readBody(from: request))
            let decoded = try JSONDecoder().decode(TelemetryIngestRequest.self, from: body)
            XCTAssertEqual(decoded.tenantId, "tenant-1")
            XCTAssertEqual(decoded.metadata["source"], .string("ios-carplay"))
            XCTAssertEqual(decoded.metadata["event_count"], .number(1))
            XCTAssertEqual(decoded.signals.first?.tag, "confirmation")
            XCTAssertEqual(decoded.signals.first?.unit, "count")
            let response = HTTPURLResponse(
                url: try XCTUnwrap(request.url),
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            return URLProtocolStub.Stub(response: response!, data: payload)
        }

        let client = APIClient(
            baseUrl: "http://localhost:8080",
            jwtToken: "",
            session: makeTestSession()
        )
        let uploader = InteractionUploader(store: store)
        let remaining = await uploader.flush(client: client)
        XCTAssertEqual(remaining, 0)
        XCTAssertEqual(store.size(), 0)
        removeQueueFile(named: "interaction.queue.json")
    }

    func testFeedbackStoreRespectsLimit() {
        removeQueueFile(named: "feedback.queue.json")
        let store = FeedbackStore()
        let request = OutcomeFeedbackRequest(
            tenantId: "tenant-1",
            traceId: "trace-1",
            ticketId: nil,
            actionId: nil,
            actorId: "user-1",
            idempotencyKey: "idem-1",
            feedback: "up",
            consentGranted: true,
            reasonCodes: ["helpful"],
            note: nil,
            confidenceBefore: nil,
            confidenceAfter: nil
        )
        _ = store.enqueue(OutcomeFeedbackEvent(eventId: "e1", request: request), limit: 1)
        _ = store.enqueue(OutcomeFeedbackEvent(eventId: "e2", request: request), limit: 1)
        let loaded = store.load()
        XCTAssertEqual(loaded.count, 1)
        XCTAssertEqual(loaded.first?.eventId, "e2")
        removeQueueFile(named: "feedback.queue.json")
    }

    func testInteractionStoreRespectsLimit() {
        removeQueueFile(named: "interaction.queue.json")
        let store = InteractionStore()
        let event = InteractionCaptureEvent(
            eventId: "e1",
            tenantId: "tenant-1",
            userId: "user-1",
            traceId: nil,
            actionId: nil,
            eventType: "confirmation",
            occurredUtc: "2026-02-01T12:00:00Z",
            delaySeconds: nil,
            correctionNote: nil
        )
        _ = store.enqueue(event, limit: 1)
        let event2 = InteractionCaptureEvent(
            eventId: "e2",
            tenantId: "tenant-1",
            userId: "user-1",
            traceId: nil,
            actionId: nil,
            eventType: "confirmation",
            occurredUtc: "2026-02-01T12:01:00Z",
            delaySeconds: nil,
            correctionNote: nil
        )
        _ = store.enqueue(event2, limit: 1)
        let loaded = store.load()
        XCTAssertEqual(loaded.count, 1)
        XCTAssertEqual(loaded.first?.eventId, "e2")
        removeQueueFile(named: "interaction.queue.json")
    }
}

private func readBody(from request: URLRequest) -> Data? {
    guard let stream = request.httpBodyStream else {
        return nil
    }
    stream.open()
    defer { stream.close() }
    var data = Data()
    let bufferSize = 1024
    var buffer = [UInt8](repeating: 0, count: bufferSize)
    while stream.hasBytesAvailable {
        let read = stream.read(&buffer, maxLength: bufferSize)
        if read > 0 {
            data.append(buffer, count: read)
        } else {
            break
        }
    }
    return data
}

private func XCTAssertThrowsErrorAsync(
    _ expression: @escaping () async throws -> Void,
    handler: (Error) -> Void
) async {
    do {
        try await expression()
        XCTFail("Expected error to be thrown.")
    } catch {
        handler(error)
    }
}

private func makeTestSession() -> URLSession {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [URLProtocolStub.self]
    return URLSession(configuration: configuration)
}

private func removeQueueFile(named name: String) {
    let doc = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
    if let url = doc?.appendingPathComponent(name) {
        try? FileManager.default.removeItem(at: url)
    }
}
