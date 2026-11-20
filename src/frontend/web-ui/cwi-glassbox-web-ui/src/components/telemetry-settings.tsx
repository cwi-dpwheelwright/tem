"use client";

import { useEffect, useState } from "react";
import { useTelemetry, type TelemetryPreferences } from "@/lib/telemetry/context";

export default function TelemetrySettings() {
  const {
    preferences,
    setPreferences,
    queueSize,
    lastFlushAt,
    lastError,
    isFlushing,
    stats,
    flushNow,
    clearQueue,
    trackEvent,
  } = useTelemetry();
  const [draft, setDraft] = useState<TelemetryPreferences>(preferences);

  useEffect(() => {
    setDraft(preferences);
  }, [preferences]);

  function update(next: Partial<TelemetryPreferences>) {
    const merged = { ...draft, ...next };
    setDraft(merged);
    setPreferences(merged);
    trackEvent("telemetry_settings_update", "analytics", next);
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm font-semibold text-slate-800">Telemetry consent + controls</p>
          <p className="mt-1 text-xs text-slate-600">
            Client-side event capture is opt-in and sampled. Set tenant ID before enabling.
          </p>
        </div>
        <span className="rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.25em] text-emerald-700">
          Opt-in
        </span>
      </div>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <label className="flex flex-col gap-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
          Tenant ID
          <input
            value={draft.tenantId}
            onChange={(event) => update({ tenantId: event.target.value })}
            placeholder="tenant_id"
            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          />
        </label>
        <label className="flex flex-col gap-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
          Sampling rate ({draft.sampleRate}%)
          <input
            type="range"
            min={0}
            max={100}
            value={draft.sampleRate}
            onChange={(event) => update({ sampleRate: Number(event.target.value) })}
            className="accent-slate-900"
          />
        </label>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-4">
        <button
          className={`rounded-2xl px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] transition ${
            draft.enabled
              ? "bg-slate-900 text-white"
              : "border border-slate-300 bg-white text-slate-700"
          }`}
          onClick={() => update({ enabled: !draft.enabled })}
        >
          {draft.enabled ? "Telemetry enabled" : "Enable telemetry"}
        </button>
        <button
          className={`rounded-2xl px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] transition ${
            draft.shareDetailed
              ? "bg-amber-600 text-white"
              : "border border-amber-200 bg-amber-50 text-amber-700"
          }`}
          onClick={() => update({ shareDetailed: !draft.shareDetailed })}
          disabled={!draft.enabled}
        >
          {draft.shareDetailed ? "Detailed events on" : "Share detailed events"}
        </button>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
          <p className="uppercase tracking-[0.2em] text-slate-500">Queue</p>
          <p className="mt-2 text-lg font-semibold text-slate-900">{queueSize} events</p>
          <p className="mt-1 text-xs text-slate-500">
            Dropped: {stats.dropped ?? 0}
          </p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
          <p className="uppercase tracking-[0.2em] text-slate-500">Last flush</p>
          <p className="mt-2 text-lg font-semibold text-slate-900">
            {lastFlushAt ? new Date(lastFlushAt).toLocaleString() : "Never"}
          </p>
          <p className="mt-1 text-xs text-slate-500">
            {isFlushing ? "Flushing now" : "Idle"}
          </p>
        </div>
      </div>

      {lastError ? (
        <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {lastError}
        </div>
      ) : null}

      {!draft.enabled || !draft.tenantId ? (
        <div className="mt-4 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-700">
          Telemetry is disabled until a tenant ID is provided and consent is enabled.
        </div>
      ) : null}

      <div className="mt-5 flex flex-wrap gap-3">
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          onClick={flushNow}
          disabled={!draft.enabled || !draft.tenantId || isFlushing}
        >
          {isFlushing ? "Flushing..." : "Flush now"}
        </button>
        <button
          className="rounded-2xl border border-slate-300 bg-white px-4 py-2 text-xs font-semibold text-slate-700"
          onClick={clearQueue}
        >
          Clear queue
        </button>
      </div>
    </div>
  );
}
