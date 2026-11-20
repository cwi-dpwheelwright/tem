import SwiftUI

struct InteractionCaptureView: View {
    @ObservedObject var state: AppState
    @State private var correctionNote: String = ""
    @State private var delayMinutes: Double = 1

    var body: some View {
        let canSend = state.consentGranted && !(state.trace?.traceId ?? "").isEmpty
        VStack(alignment: .leading, spacing: 8) {
            Text("Operator Interactions")
                .font(.headline)

            Text("Capture confirmations, corrections, and delays (consent required).")
                .font(.caption)
                .foregroundStyle(.secondary)

            HStack(spacing: 12) {
                Button("Confirm") {
                    state.submitInteraction(eventType: "confirmation", correctionNote: nil, delaySeconds: nil)
                }
                .buttonStyle(.borderedProminent)
                .disabled(!canSend)

                Button("Correct") {
                    state.submitInteraction(eventType: "correction", correctionNote: correctionNote, delaySeconds: nil)
                    correctionNote = ""
                }
                .buttonStyle(.bordered)
                .disabled(!canSend)
            }

            TextField("Correction note", text: $correctionNote)
                .textFieldStyle(.roundedBorder)

            HStack(spacing: 12) {
                Text("Delay: \(Int(delayMinutes)) min")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Slider(value: $delayMinutes, in: 1...30, step: 1)
            }

            Button("Record Delay") {
                let seconds = delayMinutes * 60
                state.submitInteraction(eventType: "delay", correctionNote: nil, delaySeconds: seconds)
            }
            .buttonStyle(.bordered)
            .disabled(!canSend)

            if let status = state.interactionStatus {
                Text(status)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            if !canSend {
                Text("Trace and consent required before capturing interactions.")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }

            Text("Queued interactions: \(state.interactionQueueCount)")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}
