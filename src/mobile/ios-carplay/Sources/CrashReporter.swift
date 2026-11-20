import Foundation

protocol CrashReporting {
    func record(error: Error, context: [String: String])
    func record(event: String, context: [String: String])
}

enum CrashReporter {
    private(set) static var shared: CrashReporting = NoopCrashReporter()

    static func configure(enabled: Bool) {
        shared = enabled ? DebugCrashReporter() : NoopCrashReporter()
    }
}

struct NoopCrashReporter: CrashReporting {
    func record(error: Error, context: [String: String]) {
    }

    func record(event: String, context: [String: String]) {
    }
}

final class DebugCrashReporter: CrashReporting {
    func record(error: Error, context: [String: String]) {
        let redacted = SensitiveDataRedactor.redact(context)
        print("CrashReporter error: \(error.localizedDescription)")
        print("CrashReporter context: \(redacted)")
    }

    func record(event: String, context: [String: String]) {
        let redacted = SensitiveDataRedactor.redact(context)
        print("CrashReporter event: \(event)")
        print("CrashReporter context: \(redacted)")
    }
}

enum SensitiveDataRedactor {
    static func redact(_ context: [String: String]) -> [String: String] {
        var sanitized: [String: String] = [:]
        for (key, value) in context {
            let loweredKey = key.lowercased()
            if loweredKey.contains("token") || loweredKey.contains("authorization") || loweredKey.contains("jwt") {
                sanitized[key] = "[redacted]"
                continue
            }
            sanitized[key] = redactValue(value)
        }
        return sanitized
    }

    private static func redactValue(_ value: String) -> String {
        if value.contains("Bearer ") {
            return value.replacingOccurrences(of: "Bearer ", with: "Bearer [redacted]")
        }
        if value.count > 80 {
            let prefix = value.prefix(12)
            return "\(prefix)...[redacted]"
        }
        return value
    }
}
