/* eslint-disable react-hooks/set-state-in-effect */
"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import AppShell from "@/components/app-shell";
import { createApiClient } from "@/lib/api/client";
import type { ReportSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useTenant } from "@/lib/tenant/context";
import { useTelemetry } from "@/lib/telemetry/context";

const STATUS_STYLES: Record<string, string> = {
  approved: "bg-emerald-50 text-emerald-700 border-emerald-200",
  review: "bg-amber-50 text-amber-700 border-amber-200",
  escalated: "bg-rose-50 text-rose-700 border-rose-200",
  draft: "bg-slate-50 text-slate-600 border-slate-200",
};

export default function ReportsPage() {
  const { trackEvent } = useTelemetry();
  const { accessToken } = useAuth();
  const { tenantId } = useTenant();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const configError = !config.config?.apiBaseUrl ? "Missing API base URL configuration." : null;
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");
  const [reports, setReports] = useState<ReportSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    trackEvent("page_view", "reports", { page: "reports" });
  }, [trackEvent]);

  useEffect(() => {
    if (!config.config?.apiBaseUrl) {
      setError(`Missing API base URL configuration.`);
      setIsLoading(false);
      return;
    }

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
    });

    setIsLoading(true);
    setError(null);
    client
      .reportsList(tenantId)
      .then((data) => {
        setReports(data);
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : "Unable to load reports.");
      })
      .finally(() => setIsLoading(false));
  }, [accessToken, config.config?.apiBaseUrl, configError, tenantId]);

  const effectiveLoading = configError ? false : isLoading;
  const effectiveError = configError ?? error;

  const filtered = useMemo(() => {
    return reports.filter((report) => {
      const matchesQuery = `${report.title} ${report.summary}`
        .toLowerCase()
        .includes(query.toLowerCase());
      const matchesStatus = status === "all" || report.status === status;
      return matchesQuery && matchesStatus;
    });
  }, [query, reports, status]);

  return (
    <AppShell
      title="Reports center"
      subtitle="Review structured and narrative reports with coherence indicators and escalation signals."
      badge="Reports"
    >
      <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="text-sm font-semibold text-slate-800">Report index</p>
            <p className="mt-1 text-xs text-slate-600">
              Filter by status and review coherence scores before escalation.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs">
            {["all", "approved", "review", "escalated", "draft"].map((item) => (
              <button
                key={item}
                className={`rounded-full border px-3 py-1 uppercase tracking-[0.2em] ${
                  status === item
                    ? "border-slate-900 bg-slate-900 text-white"
                    : "border-slate-200 bg-white text-slate-600"
                }`}
                onClick={() => {
                  setStatus(item);
                  trackEvent("filter_status", "reports", { status: item });
                }}
              >
                {item}
              </button>
            ))}
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-3">
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search reports"
            className="flex-1 rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          />
          <button
            className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700"
            onClick={() => {
              setQuery("");
              setStatus("all");
            }}
          >
            Reset
          </button>
        </div>

        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          {effectiveLoading ? (
            <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
              Loading reports...
            </div>
          ) : null}
          {effectiveError ? (
            <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-6 text-sm text-rose-700">
              {effectiveError}
            </div>
          ) : null}
          {filtered.map((report) => (
            <Link
              key={report.report_id}
              href={`/reports/${report.report_id}`}
              className="rounded-3xl border border-slate-200 bg-white p-5 transition hover:border-slate-400"
              onClick={() =>
                trackEvent("open_report", "reports", {
                  reportId: report.report_id,
                  status: report.status,
                })
              }
            >
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-slate-900">{report.title}</p>
                <span
                  className={`rounded-full border px-3 py-1 text-[11px] uppercase tracking-[0.2em] ${
                    STATUS_STYLES[report.status]
                  }`}
                >
                  {report.status}
                </span>
              </div>
              <p className="mt-2 text-xs text-slate-600">{report.summary}</p>
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-[11px] text-slate-600">
                  Coherence {(report.coherence_score * 100).toFixed(0)}%
                </span>
                <span className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[11px] text-amber-700">
                  Conflicts {report.conflict_count}
                </span>
                {report.escalation_ticket ? (
                  <span className="rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-[11px] text-rose-700">
                    Ticket {report.escalation_ticket}
                  </span>
                ) : null}
              </div>
              <div className="mt-4">
                <div className="h-2 w-full overflow-hidden rounded-full bg-slate-100">
                  <div
                    className="h-full bg-emerald-500"
                    style={{ width: `${report.coherence_score * 100}%` }}
                  />
                </div>
                <p className="mt-2 text-xs text-slate-500">
                  Created {new Date(report.created_utc).toLocaleString()}
                </p>
              </div>
            </Link>
          ))}
          {!isLoading && !error && filtered.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
              No reports match the current filter.
            </div>
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}
