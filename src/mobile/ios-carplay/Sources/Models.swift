import Foundation

enum JSONValue: Codable, Equatable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case object([String: JSONValue])
    case array([JSONValue])
    case null

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let boolValue = try? container.decode(Bool.self) {
            self = .bool(boolValue)
        } else if let numberValue = try? container.decode(Double.self) {
            self = .number(numberValue)
        } else if let stringValue = try? container.decode(String.self) {
            self = .string(stringValue)
        } else if let objectValue = try? container.decode([String: JSONValue].self) {
            self = .object(objectValue)
        } else if let arrayValue = try? container.decode([JSONValue].self) {
            self = .array(arrayValue)
        } else {
            throw DecodingError.dataCorruptedError(in: container, debugDescription: "Unsupported JSON value")
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .string(let value):
            try container.encode(value)
        case .number(let value):
            try container.encode(value)
        case .bool(let value):
            try container.encode(value)
        case .object(let value):
            try container.encode(value)
        case .array(let value):
            try container.encode(value)
        case .null:
            try container.encodeNil()
        }
    }
}

struct AskRequest: Codable {
    struct PackRef: Codable {
        let name: String
    }

    struct Context: Codable {
        let mode: String
        let topK: Int?

        enum CodingKeys: String, CodingKey {
            case mode
            case topK = "top_k"
        }
    }

    let tenantId: String
    let userId: String
    let question: String
    let pack: PackRef
    let context: Context

    enum CodingKeys: String, CodingKey {
        case tenantId = "tenant_id"
        case userId = "user_id"
        case question
        case pack
        case context
    }
}

struct LoginRequest: Codable {
    let tenantId: String?
    let email: String?
    let username: String?
    let password: String

    enum CodingKeys: String, CodingKey {
        case tenantId = "tenant_id"
        case email
        case username
        case password
    }
}

struct TokenResponse: Codable {
    let accessToken: String
    let refreshToken: String
    let tokenType: String
    let expiresUtc: String

    enum CodingKeys: String, CodingKey {
        case accessToken = "access_token"
        case refreshToken = "refresh_token"
        case tokenType = "token_type"
        case expiresUtc = "expires_utc"
    }
}

struct RefreshRequest: Codable {
    let refreshToken: String

    enum CodingKeys: String, CodingKey {
        case refreshToken = "refresh_token"
    }
}

struct LogoutRequest: Codable {
    let refreshToken: String

    enum CodingKeys: String, CodingKey {
        case refreshToken = "refresh_token"
    }
}

struct AskResponse: Codable {
    struct Citation: Codable {
        let docId: String?
        let chunkId: String?
        let score: Double?
        let source: String?
        let snippet: String?

        enum CodingKeys: String, CodingKey {
            case docId = "doc_id"
            case chunkId = "chunk_id"
            case score
            case source
            case snippet
        }
    }

    struct Trace: Codable {
        let traceId: String?
        let sources: [String]?
        let confidence: Double?
        let policyMode: String?
        let model: String?
        let packHash: String?
        let otelTraceId: String?

        enum CodingKeys: String, CodingKey {
            case traceId = "trace_id"
            case sources
            case confidence
            case policyMode = "policy_mode"
            case model
            case packHash = "pack_hash"
            case otelTraceId = "otel_trace_id"
        }
    }

    let answer: String
    let citations: [Citation]
    let trace: Trace
}

struct AskHistoryItem: Identifiable, Codable {
    let id: UUID
    let question: String
    let answer: String
    let citations: [AskResponse.Citation]
    let trace: AskResponse.Trace
    let timestamp: Date
}

struct ActionRequest: Codable {
    struct ActionPayload: Codable {
        let type: String
        let payload: JSONValue?

        enum CodingKeys: String, CodingKey {
            case type
            case payload
            case args
        }

        init(type: String, payload: JSONValue?) {
            self.type = type
            self.payload = payload
        }

        init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            type = try container.decode(String.self, forKey: .type)
            if let payload = try? container.decode(JSONValue.self, forKey: .payload) {
                self.payload = payload
            } else if let args = try? container.decode([String: String].self, forKey: .args) {
                self.payload = .object(args.mapValues { .string($0) })
            } else {
                self.payload = nil
            }
        }

        func encode(to encoder: Encoder) throws {
            var container = encoder.container(keyedBy: CodingKeys.self)
            try container.encode(type, forKey: .type)
            if let payload {
                try container.encode(payload, forKey: .payload)
            } else {
                try container.encode([String: String](), forKey: .payload)
            }
        }
    }

    let tenantId: String
    let userId: String
    let policyMode: String
    let action: ActionPayload

    enum CodingKeys: String, CodingKey {
        case tenantId = "tenant_id"
        case userId = "user_id"
        case policyMode = "policy_mode"
        case action
    }
}

struct ApprovalTicket: Codable {
    let ticketId: String
    let risk: String?
    let reason: String?

    enum CodingKeys: String, CodingKey {
        case ticketId = "ticket_id"
        case risk
        case reason
    }
}

struct ActionResponse: Codable {
    let executed: Bool?
    let approvalTicket: ApprovalTicket?
    let result: [String: JSONValue]?
    let trace: AskResponse.Trace?

    enum CodingKeys: String, CodingKey {
        case executed
        case approvalTicket = "approval_ticket"
        case result
        case trace
    }
}

struct ApprovalTicketStatus: Codable {
    let ticketId: String
    let status: String
    let risk: String
    let reason: String
    let updatedUtc: String

    enum CodingKeys: String, CodingKey {
        case ticketId = "ticket_id"
        case status
        case risk
        case reason
        case updatedUtc = "updated_utc"
    }
}

struct KillSwitchStatus: Codable {
    let enabled: Bool
    let cachedAt: Date
}

struct KillSwitchStatusResponse: Codable {
    let enabled: Bool
}

struct OutcomeFeedbackRequest: Codable {
    let tenantId: String
    let traceId: String
    let ticketId: String?
    let actionId: String?
    let actorId: String
    let idempotencyKey: String?
    let feedback: String
    let consentGranted: Bool
    let reasonCodes: [String]
    let note: String?
    let confidenceBefore: Double?
    let confidenceAfter: Double?

    enum CodingKeys: String, CodingKey {
        case tenantId = "tenant_id"
        case traceId = "trace_id"
        case ticketId = "ticket_id"
        case actionId = "action_id"
        case actorId = "actor_id"
        case idempotencyKey = "idempotency_key"
        case feedback
        case consentGranted = "consent_granted"
        case reasonCodes = "reason_codes"
        case note
        case confidenceBefore = "confidence_before"
        case confidenceAfter = "confidence_after"
    }
}

struct OutcomeFeedbackSummary: Codable {
    let upCount: Int
    let downCount: Int
    let lastFeedback: String?
    let lastFeedbackUtc: String?

    enum CodingKeys: String, CodingKey {
        case upCount = "up_count"
        case downCount = "down_count"
        case lastFeedback = "last_feedback"
        case lastFeedbackUtc = "last_feedback_utc"
    }
}

struct OutcomeFeedbackResponse: Codable {
    let recordedUtc: String
    let feedback: String
    let traceId: String
    let summary: OutcomeFeedbackSummary

    enum CodingKeys: String, CodingKey {
        case recordedUtc = "recorded_utc"
        case feedback
        case traceId = "trace_id"
        case summary
    }
}

struct OutcomeFeedbackEvent: Codable {
    let eventId: String
    let request: OutcomeFeedbackRequest
}

struct SurvivalStatusResponse: Codable {
    let mode: String
    let profile: String
    let lastSignalUtc: String?
    let offlineMessage: String

    enum CodingKeys: String, CodingKey {
        case mode
        case profile
        case lastSignalUtc = "last_signal_utc"
        case offlineMessage = "offline_message"
    }
}

struct OfflineResponse: Codable {
    let mode: String
    let message: String
}

struct TelemetryWindow: Codable {
    let start: String
    let end: String
}

struct TelemetrySignal: Codable {
    let tag: String
    let unit: String
    let series: [Double]
}

struct TelemetryIngestRequest: Codable {
    let tenantId: String
    let window: TelemetryWindow
    let signals: [TelemetrySignal]
    let metadata: [String: JSONValue]

    enum CodingKeys: String, CodingKey {
        case tenantId = "tenant_id"
        case window
        case signals
        case metadata
    }
}

struct TelemetryIngestResponse: Codable {
    let accepted: Bool
    let ingestId: String
    let trace: AskResponse.Trace

    enum CodingKeys: String, CodingKey {
        case accepted
        case ingestId = "ingest_id"
        case trace
    }
}

struct PackDocument: Codable {
    let name: String
    let rawJson: String
    let anchor: String
    let hash: String
}

struct PackUiModule: Codable, Identifiable {
    let id: String
    let type: String
    let title: String?
    let surface: String?
    let actions: [PackAction]?
    let data: [String: JSONValue]?
    let children: [PackUiModule]?

    enum CodingKeys: String, CodingKey {
        case id
        case type
        case title
        case surface
        case actions
        case data
        case children
        case modules
        case items
    }

    init(
        id: String,
        type: String,
        title: String?,
        surface: String?,
        actions: [PackAction]?,
        data: [String: JSONValue]?,
        children: [PackUiModule]?
    ) {
        self.id = id
        self.type = type
        self.title = title
        self.surface = surface
        self.actions = actions
        self.data = data
        self.children = children
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        type = try container.decode(String.self, forKey: .type)
        title = try container.decodeIfPresent(String.self, forKey: .title)
        surface = try container.decodeIfPresent(String.self, forKey: .surface)
        actions = try container.decodeIfPresent([PackAction].self, forKey: .actions)
        data = try container.decodeIfPresent([String: JSONValue].self, forKey: .data)
        if let direct = try container.decodeIfPresent([PackUiModule].self, forKey: .children) {
            children = direct
        } else if let modules = try container.decodeIfPresent([PackUiModule].self, forKey: .modules) {
            children = modules
        } else {
            children = try container.decodeIfPresent([PackUiModule].self, forKey: .items)
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(type, forKey: .type)
        try container.encodeIfPresent(title, forKey: .title)
        try container.encodeIfPresent(surface, forKey: .surface)
        try container.encodeIfPresent(actions, forKey: .actions)
        try container.encodeIfPresent(data, forKey: .data)
        try container.encodeIfPresent(children, forKey: .children)
    }
}

struct PackAction: Codable, Identifiable {
    let id: String
    let label: String
    let actionType: String
    let args: [String: String]?
    let payload: JSONValue?

    enum CodingKeys: String, CodingKey {
        case id
        case label
        case actionType = "action_type"
        case args
        case payload
    }

    init(id: String, label: String, actionType: String, args: [String: String]?, payload: JSONValue?) {
        self.id = id
        self.label = label
        self.actionType = actionType
        self.args = args
        self.payload = payload
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        label = try container.decode(String.self, forKey: .label)
        actionType = try container.decode(String.self, forKey: .actionType)
        args = try container.decodeIfPresent([String: String].self, forKey: .args)
        if let payload = try? container.decode(JSONValue.self, forKey: .payload) {
            self.payload = payload
        } else if let args {
            self.payload = .object(args.mapValues { .string($0) })
        } else {
            self.payload = nil
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(label, forKey: .label)
        try container.encode(actionType, forKey: .actionType)
        try container.encodeIfPresent(args, forKey: .args)
        try container.encodeIfPresent(payload, forKey: .payload)
    }

    var payloadValue: JSONValue? {
        if let payload {
            return payload
        }
        if let args {
            return .object(args.mapValues { .string($0) })
        }
        return nil
    }
}

struct PackUi: Codable {
    let modules: [PackUiModule]
}

struct PackRoot: Codable {
    let ui: PackUi?
}

struct ReportSummary: Codable, Identifiable {
    let reportId: String
    let title: String
    let createdUtc: String
    let status: String
    let coherenceScore: Double?
    let conflictCount: Int?
    let summary: String?
    let escalationTicket: String?

    var id: String { reportId }

    enum CodingKeys: String, CodingKey {
        case reportId = "report_id"
        case title
        case createdUtc = "created_utc"
        case status
        case coherenceScore = "coherence_score"
        case conflictCount = "conflict_count"
        case summary
        case escalationTicket = "escalation_ticket"
    }
}

struct ReportEvidence: Codable, Identifiable {
    let label: String
    let value: String

    var id: String { "\(label)-\(value)" }
}

struct ReportDetail: Codable, Identifiable {
    let reportId: String
    let title: String
    let createdUtc: String
    let status: String
    let coherenceScore: Double?
    let conflictCount: Int?
    let summary: String?
    let escalationTicket: String?
    let narrative: String?
    let structured: [String: JSONValue]?
    let evidence: [ReportEvidence]?

    var id: String { reportId }

    enum CodingKeys: String, CodingKey {
        case reportId = "report_id"
        case title
        case createdUtc = "created_utc"
        case status
        case coherenceScore = "coherence_score"
        case conflictCount = "conflict_count"
        case summary
        case escalationTicket = "escalation_ticket"
        case narrative
        case structured
        case evidence
    }
}

struct DocumentSummary: Codable, Identifiable {
    let docId: String
    let title: String
    let type: String
    let owner: String
    let status: String
    let versionsCount: Int?
    let latestVersion: String?

    var id: String { docId }

    enum CodingKeys: String, CodingKey {
        case docId = "doc_id"
        case title
        case type
        case owner
        case status
        case versionsCount = "versions_count"
        case latestVersion = "latest_version"
    }
}

struct DocumentVersion: Codable, Identifiable {
    let version: String
    let createdUtc: String
    let author: String
    let checksum: String
    let notes: String?
    let content: String?

    var id: String { version }

    enum CodingKeys: String, CodingKey {
        case version
        case createdUtc = "created_utc"
        case author
        case checksum
        case notes
        case content
    }
}

struct DocumentDetail: Codable, Identifiable {
    let docId: String
    let title: String
    let type: String
    let owner: String
    let status: String
    let versions: [DocumentVersion]

    var id: String { docId }

    enum CodingKeys: String, CodingKey {
        case docId = "doc_id"
        case title
        case type
        case owner
        case status
        case versions
    }
}

struct InteractionCaptureEvent: Codable, Identifiable {
    let eventId: String
    let tenantId: String
    let userId: String
    let traceId: String?
    let actionId: String?
    let eventType: String
    let occurredUtc: String
    let delaySeconds: Double?
    let correctionNote: String?

    var id: String { eventId }

    enum CodingKeys: String, CodingKey {
        case eventId = "event_id"
        case tenantId = "tenant_id"
        case userId = "user_id"
        case traceId = "trace_id"
        case actionId = "action_id"
        case eventType = "event_type"
        case occurredUtc = "occurred_utc"
        case delaySeconds = "delay_seconds"
        case correctionNote = "correction_note"
    }
}
