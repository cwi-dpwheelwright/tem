import SwiftUI

enum TrustState: String {
    case verified = "Verified"
    case unverified = "Unverified"
    case unknown = "Unknown"
}

struct TrustBannerView: View {
    let state: TrustState

    private var color: Color {
        switch state {
        case .verified:
            return Color.green.opacity(0.2)
        case .unverified:
            return Color.red.opacity(0.2)
        case .unknown:
            return Color.gray.opacity(0.2)
        }
    }

    private var labelColor: Color {
        switch state {
        case .verified:
            return Color.green
        case .unverified:
            return Color.red
        case .unknown:
            return Color.secondary
        }
    }

    var body: some View {
        HStack {
            Text("Pack trust: \(state.rawValue)")
                .font(.caption)
                .foregroundStyle(labelColor)
            Spacer()
        }
        .padding(.horizontal)
        .padding(.vertical, 6)
        .background(color)
    }
}
