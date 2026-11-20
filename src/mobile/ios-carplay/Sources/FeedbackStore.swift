import Foundation

struct FeedbackStore {
    private var queueUrl: URL? {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first?
            .appendingPathComponent("feedback.queue.json")
    }

    func load() -> [OutcomeFeedbackEvent] {
        guard let queueUrl,
              let data = try? Data(contentsOf: queueUrl) else {
            return []
        }
        return (try? JSONDecoder().decode([OutcomeFeedbackEvent].self, from: data)) ?? []
    }

    func save(_ events: [OutcomeFeedbackEvent]) {
        guard let queueUrl else {
            return
        }
        do {
            let data = try JSONEncoder().encode(events)
            try data.write(to: queueUrl, options: .atomic)
        } catch {
            // ignore persistence errors
        }
    }

    func enqueue(_ event: OutcomeFeedbackEvent, limit: Int) -> Int {
        var queue = load()
        queue.append(event)
        if queue.count > limit {
            queue.removeFirst(queue.count - limit)
        }
        save(queue)
        return queue.count
    }

    func size() -> Int {
        load().count
    }
}
