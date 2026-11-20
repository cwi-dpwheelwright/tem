import Foundation

actor InteractionUploader {
    private let store: InteractionStore
    private var isFlushing = false
    private var backoffSeconds: Double = 1
    private let maxBackoffSeconds: Double = 30

    init(store: InteractionStore = InteractionStore()) {
        self.store = store
    }

    func enqueue(_ event: InteractionCaptureEvent, limit: Int) -> Int {
        store.enqueue(event, limit: limit)
    }

    func flush(client: APIClient) async -> Int {
        if isFlushing {
            return store.size()
        }
        isFlushing = true
        defer { isFlushing = false }

        let batchSize = max(1, AppConfig.shared.interactionBatchSize)
        let rateLimitDelayMs = AppConfig.shared.interactionRateLimitMs
        var queue = store.load()

        while !queue.isEmpty {
            let batch = Array(queue.prefix(batchSize))
            let request = buildTelemetryRequest(from: batch)
            do {
                _ = try await client.ingestTelemetry(request: request)
                queue.removeFirst(batch.count)
                store.save(queue)
                backoffSeconds = 1
                if !queue.isEmpty && rateLimitDelayMs > 0 {
                    try? await Task.sleep(nanoseconds: rateLimitDelayMs * 1_000_000)
                }
            } catch {
                backoffSeconds = min(backoffSeconds * 2, maxBackoffSeconds)
                let delay = UInt64(backoffSeconds * 1_000_000_000)
                try? await Task.sleep(nanoseconds: delay)
                break
            }
        }

        return queue.count
    }

    private func buildTelemetryRequest(from batch: [InteractionCaptureEvent]) -> TelemetryIngestRequest {
        let window = buildWindow(from: batch)
        let signals = buildSignals(from: batch)
        let metadata: [String: JSONValue] = [
            "source": .string("ios-carplay"),
            "event_count": .number(Double(batch.count)),
            "events": .array(batch.map { $0.toJsonValue() })
        ]

        return TelemetryIngestRequest(
            tenantId: batch.first?.tenantId ?? "unknown",
            window: window,
            signals: signals,
            metadata: metadata
        )
    }

    private func buildWindow(from batch: [InteractionCaptureEvent]) -> TelemetryWindow {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]

        let dates = batch.compactMap { event -> Date? in
            if let date = formatter.date(from: event.occurredUtc) {
                return date
            }
            return ISO8601DateFormatter().date(from: event.occurredUtc)
        }

        let now = Date()
        let startDate = dates.min() ?? now
        let endDate = dates.max() ?? now
        let start = formatter.string(from: startDate)
        let end = formatter.string(from: endDate)
        return TelemetryWindow(start: start, end: end)
    }

    private func buildSignals(from batch: [InteractionCaptureEvent]) -> [TelemetrySignal] {
        let grouped = Dictionary(grouping: batch, by: { $0.eventType })
        return grouped.map { key, events in
            let unit = key == "delay" ? "seconds" : "count"
            let series = events.map { event -> Double in
                if key == "delay", let delay = event.delaySeconds {
                    return delay
                }
                return 1
            }
            return TelemetrySignal(tag: key, unit: unit, series: series)
        }
    }
}

private extension InteractionCaptureEvent {
    func toJsonValue() -> JSONValue {
        var object: [String: JSONValue] = [
            "event_id": .string(eventId),
            "tenant_id": .string(tenantId),
            "user_id": .string(userId),
            "event_type": .string(eventType),
            "occurred_utc": .string(occurredUtc)
        ]
        if let traceId {
            object["trace_id"] = .string(traceId)
        }
        if let actionId {
            object["action_id"] = .string(actionId)
        }
        if let delaySeconds {
            object["delay_seconds"] = .number(delaySeconds)
        }
        if let correctionNote, !correctionNote.isEmpty {
            object["correction_note"] = .string(correctionNote)
        }
        return .object(object)
    }
}
