import SwiftUI

struct ApprovalStatusView: View {
    let approvalTicket: ApprovalTicket
    let approvalStatus: ApprovalTicketStatus?
    let onRefresh: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Approval Ticket")
                .font(.headline)
            Text("ticket_id: \(approvalTicket.ticketId)")
                .font(.caption)
            if let reason = approvalTicket.reason, !reason.isEmpty {
                Text("reason: \(reason)")
                    .font(.caption)
            }
            if let risk = approvalTicket.risk, !risk.isEmpty {
                Text("risk: \(risk)")
                    .font(.caption)
            }
            if let status = approvalStatus {
                Text("status: \(status.status)")
                    .font(.caption)
                Text("updated: \(status.updatedUtc)")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            } else {
                Text("status: pending")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Button("Refresh Approval Status") {
                onRefresh()
            }
            .buttonStyle(.bordered)
        }
        .padding()
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}
