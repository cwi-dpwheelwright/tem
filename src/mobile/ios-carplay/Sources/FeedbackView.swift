import SwiftUI

struct FeedbackView: View {
    @ObservedObject var state: AppState
    @State private var reason: String = ""

    var body: some View {
        let canSend = state.consentGranted && !(state.trace?.traceId ?? "").isEmpty
        VStack(alignment: .leading, spacing: 8) {
            Text("Outcome Feedback")
                .font(.headline)

            Toggle("Consent to share feedback", isOn: Binding(
                get: { state.consentGranted },
                set: { state.toggleConsent($0) }
            ))

            HStack(spacing: 12) {
                Button("Thumbs Up") {
                    state.submitFeedback(isPositive: true, reason: reason)
                }
                .buttonStyle(.borderedProminent)
                .disabled(!canSend)

                Button("Thumbs Down") {
                    state.submitFeedback(isPositive: false, reason: reason)
                }
                .buttonStyle(.bordered)
                .disabled(!canSend)
            }

            TextField("Optional reason", text: $reason)
                .textFieldStyle(.roundedBorder)

            if let status = state.feedbackStatus {
                Text(status)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            if !canSend {
                Text("Trace and consent required before sending feedback.")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }

            Text("Queued feedback: \(state.feedbackQueueCount)")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}
