import SwiftUI

struct SurvivalBannerView: View {
    let mode: String
    let profile: String
    let message: String?

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text("Survival mode: \(mode)")
                    .font(.caption)
                    .foregroundStyle(.orange)
                if !profile.isEmpty {
                    Text("Profile: \(profile)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
                if let message, !message.isEmpty {
                    Text(message)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
        .padding(.horizontal)
        .padding(.vertical, 6)
        .background(Color.orange.opacity(0.15))
    }
}

struct OfflineBannerView: View {
    let message: String?

    var body: some View {
        HStack {
            Text(message ?? "Offline mode active.")
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer()
        }
        .padding(.horizontal)
        .padding(.vertical, 6)
        .background(Color.gray.opacity(0.15))
    }
}

struct KillSwitchBannerView: View {
    let enabled: Bool
    let cachedAt: Date?

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(enabled ? "Kill switch: ACTIVE" : "Kill switch: inactive")
                    .font(.caption)
                    .foregroundStyle(enabled ? .red : .secondary)
                if let cachedAt {
                    Text("Last checked: \(cachedAt.formatted(date: .abbreviated, time: .shortened))")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
        .padding(.horizontal)
        .padding(.vertical, 6)
        .background((enabled ? Color.red : Color.gray).opacity(0.15))
    }
}
