import Foundation

actor FeedbackUploader {
    private let store: FeedbackStore
    private var isFlushing = false
    private var backoffSeconds: Double = 1
    private let maxBackoffSeconds: Double = 30

    init(store: FeedbackStore = FeedbackStore()) {
        self.store = store
    }

    func enqueue(_ event: OutcomeFeedbackEvent, limit: Int) -> Int {
        store.enqueue(event, limit: limit)
    }

    func flush(client: APIClient) async -> Int {
        if isFlushing {
            return store.size()
        }
        isFlushing = true
        defer { isFlushing = false }

        var queue = store.load()
        var index = 0
        while index < queue.count {
            do {
                _ = try await client.recordFeedback(request: queue[index].request)
                queue.remove(at: index)
                store.save(queue)
                backoffSeconds = 1
            } catch {
                backoffSeconds = min(backoffSeconds * 2, maxBackoffSeconds)
                let delay = UInt64(backoffSeconds * 1_000_000_000)
                try? await Task.sleep(nanoseconds: delay)
                break
            }
        }

        return queue.count
    }
}
