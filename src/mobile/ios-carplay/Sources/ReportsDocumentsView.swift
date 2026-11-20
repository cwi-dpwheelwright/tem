import SwiftUI

struct ReportsDocumentsView: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Reports & Documents")
                .font(.headline)

            reportsSection
            documentsSection
        }
    }

    private var reportsSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Reports")
                    .font(.subheadline)
                Spacer()
                Button("Refresh") {
                    Task { await state.refreshReports() }
                }
                .buttonStyle(.bordered)
            }

            if state.reports.isEmpty {
                Text("No reports yet.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(state.reports) { report in
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(report.title)
                                    .font(.subheadline)
                                Text(report.createdUtc)
                                    .font(.caption2)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            coherenceBadge(score: report.coherenceScore)
                            conflictBadge(count: report.conflictCount)
                        }
                        if let summary = report.summary {
                            Text(summary)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        HStack {
                            Text("Status: \(report.status)")
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                            Spacer()
                            Button("View") {
                                Task { await state.loadReport(reportId: report.reportId) }
                            }
                            .buttonStyle(.bordered)
                        }
                    }
                    .padding()
                    .background(Color(.tertiarySystemBackground))
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                }
            }

            if let detail = state.selectedReport {
                ReportDetailView(state: state, report: detail)
            }
        }
    }

    private var documentsSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Documents")
                    .font(.subheadline)
                Spacer()
                Button("Refresh") {
                    Task { await state.refreshDocuments() }
                }
                .buttonStyle(.bordered)
            }

            if state.documents.isEmpty {
                Text("No documents yet.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(state.documents) { document in
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(document.title)
                                    .font(.subheadline)
                                Text("\(document.type) • \(document.owner)")
                                    .font(.caption2)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text(document.status)
                                .font(.caption2)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(Color(.systemGray5))
                                .clipShape(Capsule())
                        }
                        if let latest = document.latestVersion {
                            Text("Latest: \(latest) • Versions: \(document.versionsCount ?? 0)")
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                        }
                        HStack {
                            Spacer()
                            Button("View") {
                                Task { await state.loadDocument(docId: document.docId) }
                            }
                            .buttonStyle(.bordered)
                        }
                    }
                    .padding()
                    .background(Color(.tertiarySystemBackground))
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                }
            }

            if let detail = state.selectedDocument {
                DocumentDetailView(document: detail)
            }
        }
    }

    @ViewBuilder
    private func coherenceBadge(score: Double?) -> some View {
        if let score {
            let color = coherenceColor(for: score)
            Text(String(format: "Coherence %.2f", score))
                .font(.caption2)
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(color.opacity(0.2))
                .foregroundStyle(color)
                .clipShape(Capsule())
        }
    }

    @ViewBuilder
    private func conflictBadge(count: Int?) -> some View {
        if let count {
            let color: Color = count > 0 ? .orange : .green
            Text("Conflicts \(count)")
                .font(.caption2)
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(color.opacity(0.2))
                .foregroundStyle(color)
                .clipShape(Capsule())
        }
    }

    private func coherenceColor(for score: Double) -> Color {
        switch score {
        case 0.9...:
            return .green
        case 0.75..<0.9:
            return .yellow
        default:
            return .red
        }
    }
}

struct ReportDetailView: View {
    @ObservedObject var state: AppState
    let report: ReportDetail
    @State private var approvalStatus: ApprovalTicketStatus?
    @State private var approvalError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Report Detail")
                .font(.headline)
            Text(report.title)
                .font(.subheadline)
            Text("Status: \(report.status)")
                .font(.caption)
                .foregroundStyle(.secondary)
            if let summary = report.summary {
                Text(summary)
                    .font(.caption)
            }
            if let narrative = report.narrative {
                Text(narrative)
                    .font(.caption)
            }
            if let structured = report.structured, !structured.isEmpty {
                Text("Structured Highlights")
                    .font(.caption)
                ForEach(structured.keys.sorted(), id: \.self) { key in
                    let value = structured[key] ?? .string("")
                    Text("\(key): \(value.asText())")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
            if let evidence = report.evidence, !evidence.isEmpty {
                Text("Evidence")
                    .font(.caption)
                ForEach(evidence) { item in
                    Text("\(item.label): \(item.value)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
            if let escalation = report.escalationTicket, !escalation.isEmpty {
                Text("Approval Ticket: \(escalation)")
                    .font(.caption2)
                    .foregroundStyle(.orange)
                Button("Check Approval Status") {
                    Task { await loadApprovalStatus(ticketId: escalation) }
                }
                .buttonStyle(.bordered)
                if let approvalStatus {
                    Text("Status: \(approvalStatus.status)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
                if let approvalError {
                    Text(approvalError)
                        .font(.caption2)
                        .foregroundStyle(.red)
                }
            }
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private func loadApprovalStatus(ticketId: String) async {
        let client = APIClient(baseUrl: state.baseUrl, jwtToken: state.jwtToken)
        do {
            let status = try await client.approvalStatus(
                ticketId: ticketId,
                tenantId: state.tenantId,
                roles: "approver"
            )
            approvalStatus = status
            approvalError = nil
        } catch {
            approvalError = error.localizedDescription
        }
    }
}

struct DocumentDetailView: View {
    let document: DocumentDetail

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Document Detail")
                .font(.headline)
            Text(document.title)
                .font(.subheadline)
            Text("\(document.type) • \(document.owner)")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("Status: \(document.status)")
                .font(.caption)
                .foregroundStyle(.secondary)
            ForEach(document.versions) { version in
                VStack(alignment: .leading, spacing: 4) {
                    Text("v\(version.version)")
                        .font(.caption)
                    Text("Author: \(version.author) • \(version.createdUtc)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    if let notes = version.notes {
                        Text(notes)
                            .font(.caption2)
                    }
                    if let content = version.content {
                        Text(content)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(6)
                .background(Color(.systemBackground))
                .clipShape(RoundedRectangle(cornerRadius: 8))
            }
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}

private extension JSONValue {
    func asText() -> String {
        switch self {
        case .string(let value):
            return value
        case .number(let value):
            return String(format: "%.2f", value)
        case .bool(let value):
            return value ? "true" : "false"
        case .object:
            return "{...}"
        case .array:
            return "[...]"
        case .null:
            return "null"
        }
    }
}
