"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import type { LedgerExportPage, LedgerVerificationResult } from "@/lib/api/types";

export default function LedgerViewer() {
  const { accessToken } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [traceId, setTraceId] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [exportPage, setExportPage] = useState<LedgerExportPage | null>(null);
  const [verification, setVerification] = useState<LedgerVerificationResult | null>(null);
  const [eventFilter, setEventFilter] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  async function loadLedger(nextPage: number) {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!traceId.trim()) {
      setError("Provide trace_id to query ledger.");
      return;
    }

    setIsLoading(true);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      const [pageResult, verifyResult] = await Promise.all([
        client.ledgerExport(traceId.trim(), nextPage, pageSize),
        client.ledgerVerify(traceId.trim()),
      ]);
      setExportPage(pageResult);
      setVerification(verifyResult);
      setPage(nextPage);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Ledger lookup failed.");
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Ledger viewer</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">PQC audit</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Inspect hash-chained events and verify PQC signature metadata.
      </p>
      <div className="mt-4 flex flex-wrap items-center gap-3">
        <input
          value={traceId}
          onChange={(event) => setTraceId(event.target.value)}
          placeholder="trace_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={eventFilter}
          onChange={(event) => setEventFilter(event.target.value)}
          placeholder="filter event_type"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          onClick={() => void loadLedger(1)}
          disabled={!traceId.trim() || isLoading}
        >
          {isLoading ? "Loading..." : "Load ledger"}
        </button>
        {exportPage ? (
          <button
            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700"
            onClick={() => {
              const blob = new Blob([JSON.stringify(exportPage.items, null, 2)], {
                type: "application/json",
              });
              const url = URL.createObjectURL(blob);
              const anchor = document.createElement("a");
              anchor.href = url;
              anchor.download = `ledger-${traceId.trim()}-${page}.json`;
              anchor.click();
              URL.revokeObjectURL(url);
            }}
          >
            Download JSON
          </button>
        ) : null}
      </div>
      {verification ? (
        <div
          className={`mt-3 rounded-2xl border px-4 py-2 text-xs ${
            verification.is_valid
              ? "border-emerald-200 bg-emerald-50 text-emerald-700"
              : "border-rose-200 bg-rose-50 text-rose-700"
          }`}
        >
          {verification.is_valid ? "Ledger verified." : `Verification failed: ${verification.error ?? "unknown"}`}
        </div>
      ) : null}
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      {exportPage ? (
        <div className="mt-4 space-y-3">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>
              Page {exportPage.page} of{" "}
              {Math.max(1, Math.ceil(exportPage.total / exportPage.page_size))}
            </span>
            <span>Total events: {exportPage.total}</span>
          </div>
          <div className="space-y-2">
            {exportPage.items
              .filter((item) =>
                eventFilter
                  ? item.event_type.toLowerCase().includes(eventFilter.toLowerCase())
                  : true,
              )
              .map((item) => (
              <div
                key={item.id}
                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs text-slate-700"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">{item.event_type}</span>
                  <span className="text-[10px] uppercase tracking-[0.2em] text-slate-400">
                    {item.created_utc}
                  </span>
                </div>
                <div className="mt-2 grid gap-2 text-[11px] text-slate-600 sm:grid-cols-2">
                  <div>Hash: {item.hash}</div>
                  <div>Prev: {item.prev_hash || "n/a"}</div>
                  <div>Algorithm: {item.signature_algorithm ?? "n/a"}</div>
                  <div>Key ID: {item.key_id ?? "n/a"}</div>
                </div>
                <pre className="mt-2 max-h-40 overflow-auto rounded-2xl bg-slate-900/95 p-3 text-[11px] text-slate-100">
                  {item.payload_json}
                </pre>
              </div>
            ))}
          </div>
          <div className="flex items-center justify-between">
            <button
              className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
              disabled={page <= 1 || isLoading}
              onClick={() => void loadLedger(page - 1)}
            >
              Previous
            </button>
            <button
              className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
              disabled={!exportPage || page * exportPage.page_size >= exportPage.total || isLoading}
              onClick={() => void loadLedger(page + 1)}
            >
              Next
            </button>
          </div>
        </div>
      ) : (
        <p className="mt-3 text-xs text-slate-500">
          Provide a trace_id to review ledger events and verify signatures.
        </p>
      )}
    </div>
  );
}
