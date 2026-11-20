"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";

type TraceInsights = {
  trace_id: string;
  pack_hash?: string;
  policy_mode?: string;
  confidence?: number;
  validations: Array<Record<string, unknown>>;
  discrepancies: Array<Record<string, unknown>>;
  guardrails: Array<Record<string, unknown>>;
  corrections: Array<Record<string, unknown>>;
};

function toInsights(payload: Record<string, unknown>): TraceInsights | null {
  const trace = (payload.trace as Record<string, unknown> | undefined) ?? payload;
  if (!trace) {
    return null;
  }

  return {
    trace_id: String(trace.trace_id ?? payload.trace_id ?? ""),
    pack_hash: trace.pack_hash ? String(trace.pack_hash) : undefined,
    policy_mode: trace.policy_mode ? String(trace.policy_mode) : undefined,
    confidence: typeof trace.confidence === "number" ? trace.confidence : undefined,
    validations: Array.isArray(trace.validations) ? trace.validations : [],
    discrepancies: Array.isArray(trace.discrepancies) ? trace.discrepancies : [],
    guardrails: Array.isArray(trace.guardrails) ? trace.guardrails : [],
    corrections: Array.isArray(trace.corrections) ? trace.corrections : [],
  };
}

export default function GovernanceInsights() {
  const { accessToken, hasRole } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [traceId, setTraceId] = useState("");
  const [insights, setInsights] = useState<TraceInsights | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  async function loadInsights() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!traceId.trim()) {
      setError("Provide trace_id.");
      return;
    }
    setIsLoading(true);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });
    try {
      const payload = (await client.traceLookup(traceId.trim())) as Record<string, unknown>;
      setInsights(toInsights(payload));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load governance insights.");
      setInsights(null);
    } finally {
      setIsLoading(false);
    }
  }

  function exportSummary() {
    if (!insights) {
      return;
    }
    const summary = {
      trace_id: insights.trace_id,
      pack_hash: insights.pack_hash,
      policy_mode: insights.policy_mode,
      confidence: insights.confidence,
      validations: insights.validations,
      discrepancies: insights.discrepancies,
      guardrails: insights.guardrails,
      corrections: insights.corrections,
      exported_utc: new Date().toISOString(),
    };
    const blob = new Blob([JSON.stringify(summary, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `trace-${insights.trace_id || "summary"}.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  const canView = hasRole("viewer");

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Governance insights</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Trace audit</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Review discrepancies, validations, and guardrail annotations tied to a trace.
      </p>
      <div className="mt-4 flex flex-wrap items-center gap-3">
        <input
          value={traceId}
          onChange={(event) => setTraceId(event.target.value)}
          placeholder="trace_id"
          className="flex-1 rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          onClick={() => void loadInsights()}
          disabled={!traceId.trim() || isLoading || !canView}
        >
          {isLoading ? "Loading..." : "Load insights"}
        </button>
        <button
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
          onClick={exportSummary}
          disabled={!insights}
        >
          Export summary
        </button>
      </div>
      {!canView ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          Viewer role required to access trace governance insights.
        </div>
      ) : null}
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      {insights ? (
        <div className="mt-4 space-y-4">
          <div className="flex flex-wrap gap-3 text-xs text-slate-600">
            <span className="rounded-full bg-slate-100 px-3 py-1">
              Pack hash: {insights.pack_hash ? `${insights.pack_hash.slice(0, 12)}...` : "n/a"}
            </span>
            <span className="rounded-full bg-slate-100 px-3 py-1">
              Policy: {insights.policy_mode ?? "unknown"}
            </span>
            <span className="rounded-full bg-slate-100 px-3 py-1">
              Confidence: {typeof insights.confidence === "number" ? insights.confidence.toFixed(2) : "n/a"}
            </span>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            {[
              { title: "Validations", items: insights.validations },
              { title: "Discrepancies", items: insights.discrepancies },
              { title: "Guardrails", items: insights.guardrails },
              { title: "Corrections", items: insights.corrections },
            ].map((section) => (
              <div key={section.title} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">{section.title}</p>
                {section.items.length > 0 ? (
                  <ul className="mt-2 space-y-2 text-xs text-slate-700">
                    {section.items.slice(0, 5).map((item, index) => (
                      <li key={index} className="rounded-xl bg-slate-100 px-3 py-2">
                        <pre className="whitespace-pre-wrap text-[11px]">{JSON.stringify(item, null, 2)}</pre>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-2 text-xs text-slate-400">None detected.</p>
                )}
              </div>
            ))}
          </div>
        </div>
      ) : (
        <p className="mt-3 text-xs text-slate-500">Load a trace to inspect governance annotations.</p>
      )}
    </div>
  );
}
