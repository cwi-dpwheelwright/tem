"use client";

import { useEffect, useState } from "react";
import type { RuntimeConfigStatus } from "@/lib/config";
import { withBasePath } from "@/lib/base-path";

interface ConfigState {
  loading: boolean;
  status: RuntimeConfigStatus | null;
  error?: string;
}

function formatValue(value?: string): string {
  if (!value) {
    return "—";
  }
  return value;
}

export default function ConfigDiagnostics() {
  const [state, setState] = useState<ConfigState>({ loading: true, status: null });

  useEffect(() => {
    let cancelled = false;
    fetch(withBasePath("/api/config"))
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return (await response.json()) as RuntimeConfigStatus;
      })
      .then((status) => {
        if (!cancelled) {
          setState({ loading: false, status });
        }
      })
      .catch((error: Error) => {
        if (!cancelled) {
          setState({ loading: false, status: null, error: error.message });
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-lg font-semibold text-white">Runtime configuration</h2>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          diagnostics
        </span>
      </div>

      {state.loading ? (
        <p className="mt-4 text-sm text-slate-300">Loading configuration…</p>
      ) : state.error ? (
        <p className="mt-4 text-sm text-red-200">Failed to load config: {state.error}</p>
      ) : state.status ? (
        <div className="mt-4 space-y-4 text-sm">
          {state.status.missing.length > 0 ? (
            <div className="rounded-2xl border border-red-500/40 bg-red-500/10 p-4 text-red-100">
              <p className="font-semibold">Missing required config</p>
              <ul className="mt-2 space-y-1 text-xs">
                {state.status.missing.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </div>
          ) : (
            <div className="rounded-2xl border border-emerald-500/40 bg-emerald-500/10 p-4 text-emerald-100">
              <p className="font-semibold">All required values present</p>
            </div>
          )}

          {Object.keys(state.status.errors).length > 0 && (
            <div className="rounded-2xl border border-amber-500/40 bg-amber-500/10 p-4 text-amber-100">
              <p className="font-semibold">Validation warnings</p>
              <ul className="mt-2 space-y-1 text-xs">
                {Object.entries(state.status.errors).map(([key, value]) => (
                  <li key={key}>
                    {key}: {value}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="grid gap-3 text-xs text-slate-100">
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">API base</span>
              <span>{formatValue(state.status.config?.apiBaseUrl)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">Identity base</span>
              <span>{formatValue(state.status.config?.identityBaseUrl)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">Packs base</span>
              <span>{formatValue(state.status.config?.packsBaseUrl)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">OIDC authority</span>
              <span>{formatValue(state.status.config?.oidcAuthority)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">OIDC client</span>
              <span>{formatValue(state.status.config?.oidcClientId)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2 border-b border-white/5 pb-2">
              <span className="text-slate-400">OIDC redirect</span>
              <span>{formatValue(state.status.config?.oidcRedirectUri)}</span>
            </div>
            <div className="flex flex-wrap justify-between gap-2">
              <span className="text-slate-400">OIDC scopes</span>
              <span>
                {state.status.config?.oidcScopes.length
                  ? state.status.config.oidcScopes.join(" ")
                  : "—"}
              </span>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
