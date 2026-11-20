import type { Trace } from "@/lib/api/types";
import { formatNumber } from "@/lib/format";
import { sanitizeExternalUrl } from "@/lib/link-sanitizer";

export function TracePanel({ trace }: { trace?: Trace | null }) {
  const sources = trace?.sources ?? [];

  return (
    <div className="trace-panel">
      <h3 className="trace-panel-title">
        Trace
      </h3>
      {trace ? (
        <div className="trace-panel-body">
          <div className="trace-panel-row">
            <span className="trace-panel-label">Trace ID</span>
            <span className="trace-panel-mono">{trace.trace_id}</span>
          </div>

          <div className="trace-panel-row">
            <span className="trace-panel-label">Pack Hash</span>
            <span className="trace-panel-mono">{trace.pack_hash}</span>
          </div>
          <div className="trace-panel-row">
            <span className="trace-panel-label">Policy Mode</span>
            <span>{trace.policy_mode}</span>
          </div>
          <div className="trace-panel-row">
            <span className="trace-panel-label">Model Tier</span>
            <span>{trace.model}</span>
          </div>

          <div className="trace-panel-row">
            <span className="trace-panel-label">Confidence</span>
            <span>{formatNumber(trace.confidence, { maximumFractionDigits: 3 })}</span>
          </div>
          <div className="trace-panel-row">
            <span className="trace-panel-label">Graph Hits</span>
            <span>{trace.graph_hits ?? 0}</span>
          </div>
          <div className="trace-panel-row">
            <span className="trace-panel-label">Document Hits</span>
            <span>{trace.document_hits ?? 0}</span>
          </div>
          {trace.reasoning_receipt?.graph_query_template ? (
            <div className="trace-panel-row">
              <span className="trace-panel-label">Graph Template</span>
              <span>{trace.reasoning_receipt.graph_query_template}</span>
            </div>
          ) : null}
          {trace.reasoning_receipt?.graph_fallback_used ? (
            <div className="trace-panel-row">
              <span className="trace-panel-label">Graph Fallback</span>
              <span>{trace.reasoning_receipt.graph_failure_reason ?? "enabled"}</span>
            </div>
          ) : null}
          <div className="trace-panel-row">
            <span className="trace-panel-label">Sources</span>
            {sources.length > 0 ? (
              <ul className="trace-panel-sources">
                {sources.map((source, index) => {
                  const safeUrl = sanitizeExternalUrl(source);
                  return (
                    <li key={`${source}-${index}`} className="trace-panel-source-item">
                      {safeUrl ? (
                        <a
                          href={safeUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="trace-panel-link"
                        >
                          {source}
                        </a>
                      ) : (
                        <span>{source}</span>
                      )}
                    </li>
                  );
                })}
              </ul>
            ) : (
              <span className="trace-panel-empty">No sources reported.</span>
            )}
          </div>
          {trace.otel_trace_id ? (
            <div className="trace-panel-row">
              <span className="trace-panel-label">OTel Trace</span>
              <span className="trace-panel-mono">{trace.otel_trace_id}</span>
            </div>
          ) : null}
        </div>
      ) : (
        <p className="trace-panel-empty-state">No trace available yet.</p>
      )}
    </div>
  );
}
