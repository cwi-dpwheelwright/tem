import SwiftUI

struct GlassboxView: View {
    let trace: AskResponse.Trace
    let killSwitchEnabled: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Glassbox Trace")
                .font(.headline)
            Text("trace_id: \(trace.traceId ?? "unknown")")
                .font(.caption)
            if let confidence = trace.confidence {
                Text(String(format: "confidence: %.2f", confidence))
                    .font(.caption)
            }
            if let sources = trace.sources, !sources.isEmpty {
                Text("sources: \(sources.joined(separator: ", "))")
                    .font(.caption)
            }
            Text("policy_mode: \(trace.policyMode ?? "unknown")")
                .font(.caption)
            Text("model: \(trace.model ?? "unknown")")
                .font(.caption)
            Text("pack_hash: \(trace.packHash ?? "unknown")")
                .font(.caption)
            Text("kill_switch: \(killSwitchEnabled ? "active" : "inactive")")
                .font(.caption)
            if let otel = trace.otelTraceId {
                Text("otel_trace_id: \(otel)")
                    .font(.caption)
            }
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}
