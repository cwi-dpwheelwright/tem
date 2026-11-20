"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import type { BreakGlassAuditPage, BreakGlassToken } from "@/lib/api/types";

export default function BreakGlassAudit() {
  const { accessToken } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [tenantId, setTenantId] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [result, setResult] = useState<BreakGlassAuditPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  async function loadAudit(nextPage: number) {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!tenantId.trim()) {
      setError("Provide tenant_id to query audit history.");
      return;
    }

    setIsLoading(true);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      const payload = await client.breakGlassAudit(tenantId.trim(), nextPage, pageSize);
      setResult(payload);
      setPage(nextPage);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Audit lookup failed.");
    } finally {
      setIsLoading(false);
    }
  }

  function exportJson(item?: BreakGlassToken) {
    const data = item ? item : result;
    if (!data) {
      return;
    }
    const json = JSON.stringify(data, null, 2);
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = item ? `break-glass-${item.token_id}.json` : "break-glass-audit.json";
    anchor.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div
      className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60"
      data-testid="break-glass-audit"
    >
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Break-glass audit</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Audit log</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Review issuance, expiry, and revocation events for emergency access tokens.
      </p>
      <div className="mt-4 flex flex-wrap items-center gap-3">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id"
          data-testid="break-glass-audit-tenant"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          data-testid="break-glass-audit-load"
          onClick={() => void loadAudit(1)}
          disabled={!tenantId.trim() || isLoading}
        >
          {isLoading ? "Loading..." : "Load audit"}
        </button>
        <button
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
          data-testid="break-glass-audit-export"
          onClick={() => exportJson()}
          disabled={!result}
        >
          Export JSON
        </button>
      </div>
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      {result ? (
        <div className="mt-4 space-y-3">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span>
              Page {result.page} of {Math.max(1, Math.ceil(result.total / result.page_size))}
            </span>
            <span>Total events: {result.total}</span>
          </div>
          <div className="space-y-2">
            {result.items.map((item) => (
              <div
                key={item.token_id}
                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs text-slate-700"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">{item.token_id}</span>
                  <button
                    className="rounded-full border border-slate-200 px-3 py-1 text-[10px] uppercase tracking-[0.2em] text-slate-500"
                    data-testid={`break-glass-audit-export-${item.token_id}`}
                    onClick={() => exportJson(item)}
                  >
                    Export
                  </button>
                </div>
                <div className="mt-2 grid gap-2 text-[11px] text-slate-600 sm:grid-cols-2">
                  <div>Tenant: {item.tenant_id}</div>
                  <div>User: {item.user_id}</div>
                  <div>Role: {item.role}</div>
                  <div>Scope: {item.scope}</div>
                  <div>Created: {item.created_utc}</div>
                  <div>Expires: {item.expires_utc}</div>
                  <div>Revoked: {item.revoked_utc ?? "n/a"}</div>
                  <div>Reason: {item.reason || "n/a"}</div>
                </div>
              </div>
            ))}
          </div>
          <div className="flex items-center justify-between">
            <button
              className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
              disabled={page <= 1 || isLoading}
              onClick={() => void loadAudit(page - 1)}
            >
              Previous
            </button>
            <button
              className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
              disabled={!result || page * result.page_size >= result.total || isLoading}
              onClick={() => void loadAudit(page + 1)}
            >
              Next
            </button>
          </div>
        </div>
      ) : (
        <p className="mt-3 text-xs text-slate-500">Query the audit log to review break-glass events.</p>
      )}
    </div>
  );
}
