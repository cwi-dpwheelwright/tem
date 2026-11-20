import SwiftUI

struct AskView: View {
    @ObservedObject var state: AppState
    @State private var showHistory = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            TextField("Question", text: $state.question)
                .textFieldStyle(.roundedBorder)

            Button("Ask") {
                Task {
                    await state.ask()
                }
            }
            .buttonStyle(.borderedProminent)

            if !state.answer.isEmpty {
                Text("Answer")
                    .font(.headline)
                Text(state.answer)
                    .font(.body)
            }

            if !state.citations.isEmpty {
                Text("Citations")
                    .font(.headline)
                ForEach(0..<state.citations.count, id: \.self) { idx in
                    let citation = state.citations[idx]
                    let label = [citation.docId, citation.chunkId].compactMap { $0 }.joined(separator: " / ")
                    Text("- \(label.isEmpty ? (citation.source ?? "source") : label)")
                        .font(.caption)
                    if let snippet = citation.snippet, !snippet.isEmpty {
                        Text(snippet)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    } else if let score = citation.score {
                        Text(String(format: "score: %.2f", score))
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }
            }

            if let trace = state.trace {
                GlassboxView(trace: trace, killSwitchEnabled: state.killSwitchEnabled)
            }

            if let ticket = state.approvalTicket {
                ApprovalStatusView(
                    approvalTicket: ticket,
                    approvalStatus: state.approvalStatus
                ) {
                    Task {
                        await state.refreshApprovalStatus()
                    }
                }
            }

            if let action = state.lastAction {
                ActionStatusView(action: action)
            }

            if !state.askHistory.isEmpty {
                Button(showHistory ? "Hide Ask History" : "Show Ask History") {
                    showHistory.toggle()
                }
                .buttonStyle(.bordered)
            }

            if showHistory {
                AskHistoryView(state: state)
            }

            FeedbackView(state: state)
        }
    }
}

private struct ActionStatusView: View {
    let action: ActionResponse

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Last Action")
                .font(.headline)
            if let executed = action.executed {
                Text("executed: \(executed ? "true" : "false")")
                    .font(.caption)
            }
            if let approval = action.approvalTicket {
                Text("approval_ticket: \(approval.ticketId)")
                    .font(.caption)
            }
        }
        .padding()
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}

private struct AskHistoryView: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Ask History")
                .font(.headline)
            ForEach(state.askHistory) { item in
                VStack(alignment: .leading, spacing: 4) {
                    Text(item.question)
                        .font(.caption)
                    Text(item.timestamp.formatted(date: .abbreviated, time: .shortened))
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Button("Load Trace") {
                        state.loadAskHistoryItem(item)
                    }
                    .buttonStyle(.bordered)
                }
                .padding(8)
                .background(Color(.tertiarySystemBackground))
                .clipShape(RoundedRectangle(cornerRadius: 10))
            }
        }
    }
}
