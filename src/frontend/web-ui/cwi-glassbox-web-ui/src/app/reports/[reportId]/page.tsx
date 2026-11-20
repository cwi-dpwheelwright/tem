/* eslint-disable react-hooks/set-state-in-effect */
"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import AppShell from "@/components/app-shell";
import { ApiError, createApiClient } from "@/lib/api/client";
import type { ReportDetail } from "@/lib/api/types";
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

export default function ReportDetailPage() {
  const params = useParams();
  const reportId = typeof params.reportId === "string" ? params.reportId : "";
  const { accessToken } = useAuth();
  const { tenantId } = useTenant();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const configError = !config.config?.apiBaseUrl ? "Missing API base URL configuration." : null;
  const [report, setReport] = useState<ReportDetail | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const { trackEvent } = useTelemetry();

  useEffect(() => {
    if (reportId) {
      trackEvent("report_view", "reports", { reportId });
    }
  }, [reportId, trackEvent]);

  useEffect(() => {
    if (!reportId) {
      return;
    }
    if (configError) {
      return;
    }

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
    });

    setIsLoading(true);
    setError(null);
    setNotFound(false);
    client
      .reportDetail(reportId, tenantId)
      .then((data) => setReport(data))
      .catch((err) => {
        if (err instanceof ApiError && err.status === 404) {
          setNotFound(true);
        } else {
          setError(err instanceof Error ? err.message : "Unable to load report.");
        }
      })
      .finally(() => setIsLoading(false));
  }, [accessToken, config.config?.apiBaseUrl, configError, reportId, tenantId]);

  const effectiveLoading = configError ? false : isLoading;
  const effectiveError = configError ?? error;

  if (effectiveLoading) {
    return (
      <AppShell title="Loading report" subtitle="Fetching report detail." badge="Reports">
        <div className="rounded-3xl border border-dashed border-slate-200 bg-white px-6 py-8 text-sm text-slate-500">
          Loading report...
        </div>
      </AppShell>
    );
  }

  if (notFound || !report) {
    return (
      <AppShell title="Report not found" subtitle="This report ID does not exist." badge="Reports">
        <div className="rounded-3xl border border-dashed border-slate-200 bg-white px-6 py-8 text-sm text-slate-500">
          {effectiveError ?? "Unknown report ID. Return to the report index to select a valid report."}
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell
      title={report.title}
      subtitle="Structured findings with narrative context, coherence scoring, and escalation links."
      badge="Reports"
    >
      <div className="grid gap-6 lg:grid-cols-[1.2fr_0.8fr]">
        <section className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Report ID</p>
              <p className="text-lg font-semibold text-slate-900">{report.report_id}</p>
            </div>
            <span
              className={`rounded-full border px-3 py-1 text-[11px] uppercase tracking-[0.2em] ${
                STATUS_STYLES[report.status]
              }`}
            >
              {report.status}
            </span>
          </div>
          <p className="mt-4 text-sm text-slate-600">{report.summary}</p>

          <div className="mt-6 grid gap-4 sm:grid-cols-2">
            {Object.entries(report.structured).map(([label, value]) => (
              <div key={label} className="rounded-2xl border border-slate-200 bg-white p-4">
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">{label}</p>
                <p className="mt-2 text-sm font-semibold text-slate-900">{value}</p>
              </div>
            ))}
          </div>

          <div className="mt-6 rounded-2xl border border-slate-200 bg-slate-50 p-5">
            <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Narrative</p>
            <p className="mt-3 text-sm leading-7 text-slate-700">{report.narrative}</p>
          </div>
        </section>

        <aside className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Coherence score</p>
            <p className="mt-2 text-3xl font-semibold text-slate-900">
              {(report.coherence_score * 100).toFixed(0)}%
            </p>
            <div className="mt-3 h-3 w-full overflow-hidden rounded-full bg-slate-100">
              <div
                className="h-full bg-emerald-500"
                style={{ width: `${report.coherence_score * 100}%` }}
              />
            </div>
            <div className="mt-4 flex flex-wrap gap-2 text-xs">
              <span className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-amber-700">
                Conflicts {report.conflict_count}
              </span>
              {report.escalation_ticket ? (
                <Link
                  href={`/?ticket=${report.escalation_ticket}`}
                  className="rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-rose-700"
                >
                  Ticket {report.escalation_ticket}
                </Link>
              ) : null}
            </div>
            <p className="mt-4 text-xs text-slate-500">
              Created {new Date(report.created_utc).toLocaleString()}
            </p>
          </div>

          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Evidence log</p>
            <ul className="mt-3 space-y-2 text-sm text-slate-600">
              {report.evidence.map((item) => (
                <li key={item.label} className="flex items-center justify-between gap-3">
                  <span className="text-slate-500">{item.label}</span>
                  <span className="font-semibold text-slate-900">{item.value}</span>
                </li>
              ))}
            </ul>
          </div>
        </aside>
      </div>
    </AppShell>
  );
}
