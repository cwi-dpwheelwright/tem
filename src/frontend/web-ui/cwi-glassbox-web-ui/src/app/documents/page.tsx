/* eslint-disable react-hooks/set-state-in-effect */
"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import AppShell from "@/components/app-shell";
import { createApiClient } from "@/lib/api/client";
import type { DocumentSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useTenant } from "@/lib/tenant/context";
import { useTelemetry } from "@/lib/telemetry/context";

const STATUS_COLORS: Record<string, string> = {
  active: "bg-emerald-50 text-emerald-700 border-emerald-200",
  draft: "bg-amber-50 text-amber-700 border-amber-200",
  deprecated: "bg-slate-50 text-slate-600 border-slate-200",
};

export default function DocumentsPage() {
  const { trackEvent } = useTelemetry();
  const { accessToken } = useAuth();
  const { tenantId } = useTenant();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const configError = !config.config?.apiBaseUrl ? "Missing API base URL configuration." : null;
  const [query, setQuery] = useState("");
  const [type, setType] = useState("all");
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    trackEvent("page_view", "documents", { page: "documents" });
  }, [trackEvent]);

  useEffect(() => {
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
    client
      .documentsList(tenantId)
      .then((data) => setDocuments(data))
      .catch((err) => {
        setError(err instanceof Error ? err.message : "Unable to load documents.");
      })
      .finally(() => setIsLoading(false));
  }, [accessToken, config.config?.apiBaseUrl, configError, tenantId]);

  const effectiveLoading = configError ? false : isLoading;
  const effectiveError = configError ?? error;

  const filtered = useMemo(() => {
    return documents.filter((doc) => {
      const matchesQuery = `${doc.title} ${doc.doc_id}`.toLowerCase().includes(query.toLowerCase());
      const matchesType = type === "all" || doc.type === type;
      return matchesQuery && matchesType;
    });
  }, [documents, query, type]);

  return (
    <AppShell
      title="Documents center"
      subtitle="Browse procedures, policies, and runbooks with version history and rollback controls."
      badge="Documents"
    >
      <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="text-sm font-semibold text-slate-800">Document catalog</p>
            <p className="mt-1 text-xs text-slate-600">
              Review version history and rollback readiness for critical procedures.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs">
            {["all", "procedure", "policy", "runbook"].map((item) => (
              <button
                key={item}
                className={`rounded-full border px-3 py-1 uppercase tracking-[0.2em] ${
                  type === item
                    ? "border-slate-900 bg-slate-900 text-white"
                    : "border-slate-200 bg-white text-slate-600"
                }`}
                onClick={() => {
                  setType(item);
                  trackEvent("filter_type", "documents", { type: item });
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
            placeholder="Search documents"
            className="flex-1 rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          />
          <button
            className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700"
            onClick={() => {
              setQuery("");
              setType("all");
            }}
          >
            Reset
          </button>
        </div>

        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          {effectiveLoading ? (
            <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
              Loading documents...
            </div>
          ) : null}
          {effectiveError ? (
            <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-6 text-sm text-rose-700">
              {effectiveError}
            </div>
          ) : null}
          {filtered.map((doc) => (
            <Link
              key={doc.doc_id}
              href={`/documents/${doc.doc_id}`}
              className="rounded-3xl border border-slate-200 bg-white p-5 transition hover:border-slate-400"
              onClick={() => trackEvent("open_document", "documents", { documentId: doc.doc_id })}
            >
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-slate-900">{doc.title}</p>
                <span
                  className={`rounded-full border px-3 py-1 text-[11px] uppercase tracking-[0.2em] ${
                    STATUS_COLORS[doc.status]
                  }`}
                >
                  {doc.status}
                </span>
              </div>
              <p className="mt-2 text-xs text-slate-600">{doc.doc_id}</p>
              <div className="mt-4 flex flex-wrap items-center gap-3 text-xs">
                <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-slate-600">
                  Type {doc.type}
                </span>
                <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-slate-600">
                  Versions {doc.versions_count}
                </span>
                <span className="rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-emerald-700">
                  Latest {doc.latest_version}
                </span>
              </div>
            </Link>
          ))}
          {!isLoading && !error && filtered.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
              No documents match the current filter.
            </div>
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}
