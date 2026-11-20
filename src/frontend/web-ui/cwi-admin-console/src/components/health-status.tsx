"use client";

import { useEffect, useState } from "react";
import { withBasePath } from "@/lib/base-path";

type HealthResponse = {
  ok: boolean;
  ready: boolean;
  checks: {
    config: { status: string; detail: string | null };
    api: { status: string; detail: string | null };
  };
  timestamp: string;
};

interface HealthState {
  loading: boolean;
  data: HealthResponse | null;
  error?: string;
}

export default function HealthStatus() {
  const [state, setState] = useState<HealthState>({ loading: true, data: null });

  useEffect(() => {
    let cancelled = false;
    fetch(withBasePath("/api/health"))
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return (await response.json()) as HealthResponse;
      })
      .then((data) => {
        if (!cancelled) {
          setState({ loading: false, data });
        }
      })
      .catch((error: Error) => {
        if (!cancelled) {
          setState({ loading: false, data: null, error: error.message });
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const ready = state.data?.ready ?? false;
  const badgeClass = ready
    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
    : "border-amber-500/40 bg-amber-500/10 text-amber-100";

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-lg font-semibold text-white">Service readiness</h2>
        <span
          className={`rounded-full border px-3 py-1 text-xs uppercase tracking-[0.2em] ${badgeClass}`}
        >
          {ready ? "ready" : "not ready"}
        </span>
      </div>

      {state.loading ? (
        <p className="mt-4 text-sm text-slate-300">Checking health…</p>
      ) : state.error ? (
        <p className="mt-4 text-sm text-red-200">Failed to load health: {state.error}</p>
      ) : state.data ? (
        <div className="mt-4 space-y-3 text-sm text-slate-200">
          <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
            <span className="text-slate-400">Config</span>
            <span>{state.data.checks.config.status}</span>
          </div>
          <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
            <span className="text-slate-400">API</span>
            <span>
              {state.data.checks.api.status}
              {state.data.checks.api.detail ? ` (${state.data.checks.api.detail})` : ""}
            </span>
          </div>
          <div className="flex flex-wrap justify-between gap-2">
            <span className="text-slate-400">Timestamp</span>
            <span>{new Date(state.data.timestamp).toLocaleString()}</span>
          </div>
        </div>
      ) : null}
    </section>
  );
}
