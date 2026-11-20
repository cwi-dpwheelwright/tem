import Foundation

struct AppConfig {
    static let shared = AppConfig()

    let certPinHost: String
    let certPinSHA256: [String]
    let certPinEnabled: Bool
    let simulateOffline: Bool
    let simulateBackpressureMs: UInt64
    let feedbackQueueLimit: Int
    let interactionQueueLimit: Int
    let interactionBatchSize: Int
    let interactionRateLimitMs: UInt64
    let crashReportingEnabled: Bool
    let apiTimeoutSeconds: TimeInterval
    let apiRetryBudget: Int
    let killSwitchCacheMinutes: Int

    init(bundle: Bundle = .main) {
        let info = bundle.infoDictionary ?? [:]
        certPinHost = info["CertPinHost"] as? String ?? ""
        let pins = info["CertPinSHA256"] as? String ?? ""
        certPinSHA256 = pins.split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        certPinEnabled = info["CertPinEnabled"] as? Bool ?? false
        simulateOffline = info["SimulateOffline"] as? Bool ?? false
        let backpressure = info["SimulateBackpressureMs"] as? NSNumber
        simulateBackpressureMs = backpressure?.uint64Value ?? 0
        let feedbackLimit = info["FeedbackQueueLimit"] as? NSNumber
        feedbackQueueLimit = feedbackLimit?.intValue ?? 100
        let interactionLimit = info["InteractionQueueLimit"] as? NSNumber
        interactionQueueLimit = interactionLimit?.intValue ?? 200
        let interactionBatch = info["InteractionBatchSize"] as? NSNumber
        interactionBatchSize = interactionBatch?.intValue ?? 20
        let interactionRate = info["InteractionRateLimitMs"] as? NSNumber
        interactionRateLimitMs = interactionRate?.uint64Value ?? 500
        crashReportingEnabled = info["CrashReporterEnabled"] as? Bool ?? false
        let timeout = info["ApiTimeoutSeconds"] as? NSNumber
        apiTimeoutSeconds = timeout?.doubleValue ?? 15
        let retryBudget = info["ApiRetryBudget"] as? NSNumber
        apiRetryBudget = retryBudget?.intValue ?? 1
        let killSwitchCache = info["KillSwitchCacheMinutes"] as? NSNumber
        killSwitchCacheMinutes = killSwitchCache?.intValue ?? 10
    }

    init(
        certPinHost: String = "",
        certPinSHA256: [String] = [],
        certPinEnabled: Bool = false,
        simulateOffline: Bool = false,
        simulateBackpressureMs: UInt64 = 0,
        feedbackQueueLimit: Int = 100,
        interactionQueueLimit: Int = 200,
        interactionBatchSize: Int = 20,
        interactionRateLimitMs: UInt64 = 500,
        crashReportingEnabled: Bool = false,
        apiTimeoutSeconds: TimeInterval = 15,
        apiRetryBudget: Int = 1,
        killSwitchCacheMinutes: Int = 10
    ) {
        self.certPinHost = certPinHost
        self.certPinSHA256 = certPinSHA256
        self.certPinEnabled = certPinEnabled
        self.simulateOffline = simulateOffline
        self.simulateBackpressureMs = simulateBackpressureMs
        self.feedbackQueueLimit = feedbackQueueLimit
        self.interactionQueueLimit = interactionQueueLimit
        self.interactionBatchSize = interactionBatchSize
        self.interactionRateLimitMs = interactionRateLimitMs
        self.crashReportingEnabled = crashReportingEnabled
        self.apiTimeoutSeconds = apiTimeoutSeconds
        self.apiRetryBudget = apiRetryBudget
        self.killSwitchCacheMinutes = killSwitchCacheMinutes
    }
}
