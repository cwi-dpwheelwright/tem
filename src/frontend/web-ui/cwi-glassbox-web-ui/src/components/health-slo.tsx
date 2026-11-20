"use client";

import { useEffect, useMemo, useState } from "react";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";

type SloSnapshot = {
  p95LatencyMs: number | null;
  errorRate: number | null;
  updatedUtc: string;
};

function parsePrometheusMetrics(text: string): SloSnapshot {
  let p95LatencyMs: number | null = null;
  let errorRate: number | null = null;
  let totalRequests = 0;
  let errorRequests = 0;

  const lines = text.split("\n");
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      continue;
    }
    const match = trimmed.match(
      /^([a-zA-Z_:][\\w:]*)\\s*(\\{[^}]*\\})?\\s+(-?\\d+(?:\\.\\d+)?(?:e[-+]?\\d+)?)$/,
    );
    if (!match) {
      continue;
    }
    const name = match[1];
    const labels = match[2] ?? "";
    const value = Number.parseFloat(match[3]);

    if (labels.includes('quantile="0.95"') || name.includes("p95")) {
      if (Number.isFinite(value)) {
        const isSeconds = name.includes("_seconds");
        const candidate = isSeconds ? value * 1000 : value;
        if (p95LatencyMs === null || candidate > p95LatencyMs) {
          p95LatencyMs = candidate;
        }
      }
    }

    if (name.includes("requests_total") && labels.includes("status")) {
      totalRequests += value;
      const statusMatch = labels.match(/status="(\\d{3})"/);
      if (statusMatch && statusMatch[1].startsWith("5")) {
        errorRequests += value;
      }
    }
  }

  if (totalRequests > 0) {
    errorRate = (errorRequests / totalRequests) * 100;
  }

  return {
    p95LatencyMs,
    errorRate,
    updatedUtc: new Date().toISOString(),
  };
}

export default function HealthSlo() {
  const { accessToken } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const apiBaseUrl = config.config?.apiBaseUrl;
  const [snapshot, setSnapshot] = useState<SloSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!apiBaseUrl) {
      return;
    }

    let cancelled = false;

    async function load() {
      try {
        const response = await fetch(`${apiBaseUrl}/api/metrics`, {
          method: "GET",
          headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
        });
        if (!response.ok) {
          throw new Error(`Metrics unavailable (${response.status})`);
        }
        const text = await response.text();
        if (!cancelled) {
          setSnapshot(parsePrometheusMetrics(text));
          setError(null);
        }
      } catch (err) {
        if (!cancelled) {
          setSnapshot(null);
          setError(err instanceof Error ? err.message : "Failed to load metrics.");
        }
      }
    }

    void load();
    const interval = window.setInterval(load, 30000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [accessToken, apiBaseUrl]);

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Service SLOs</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Prometheus</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Live latency and error-rate indicators derived from the metrics gateway.
      </p>
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <div className="rounded-2xl border border-slate-200 bg-white px-4 py-4">
          <p className="text-xs uppercase tracking-[0.2em] text-slate-500">p95 latency</p>
          <p className="mt-2 text-2xl font-semibold text-slate-900">
            {snapshot?.p95LatencyMs !== null && snapshot?.p95LatencyMs !== undefined
              ? `${Math.round(snapshot.p95LatencyMs)}ms`
              : "n/a"}
          </p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-white px-4 py-4">
          <p className="text-xs uppercase tracking-[0.2em] text-slate-500">error rate</p>
          <p className="mt-2 text-2xl font-semibold text-slate-900">
            {snapshot?.errorRate !== null && snapshot?.errorRate !== undefined
              ? `${snapshot.errorRate.toFixed(2)}%`
              : "n/a"}
          </p>
        </div>
      </div>
      {snapshot ? (
        <p className="mt-3 text-[11px] uppercase tracking-[0.2em] text-slate-400">
          Updated {new Date(snapshot.updatedUtc).toLocaleTimeString()}
        </p>
      ) : null}
    </div>
  );
}
