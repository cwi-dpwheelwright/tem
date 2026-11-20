"use client";

import { useMemo } from "react";
import { useTelemetry } from "@/lib/telemetry/context";

export default function TelemetryDashboard() {
  const { stats, preferences, queueSize } = useTelemetry();
  const totalEvents = useMemo(
    () => Object.values(stats.totals).reduce((sum, value) => sum + value, 0),
    [stats.totals],
  );
  const topSignals = useMemo(() => {
    return Object.entries(stats.totals)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 8);
  }, [stats.totals]);

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm font-semibold text-slate-800">Interaction analytics</p>
          <p className="mt-1 text-xs text-slate-600">
            Aggregated counts captured client-side (no raw content captured).
          </p>
        </div>
        <span className="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.25em] text-slate-500">
          Aggregate
        </span>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
          <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Events captured</p>
          <p className="mt-2 text-2xl font-semibold text-slate-900">{totalEvents}</p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
          <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Sample rate</p>
          <p className="mt-2 text-2xl font-semibold text-slate-900">
            {preferences.sampleRate}%
          </p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
          <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Queue depth</p>
          <p className="mt-2 text-2xl font-semibold text-slate-900">{queueSize}</p>
        </div>
      </div>

      <div className="mt-6">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
          Top interaction signals
        </p>
        <div className="mt-3 grid gap-3 sm:grid-cols-2">
          {topSignals.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-4 text-sm text-slate-500">
              No events captured yet.
            </div>
          ) : (
            topSignals.map(([name, count]) => (
              <div
                key={name}
                className="rounded-2xl border border-slate-200 bg-white px-4 py-3"
              >
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">{name}</p>
                <p className="mt-2 text-lg font-semibold text-slate-900">{count}</p>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
