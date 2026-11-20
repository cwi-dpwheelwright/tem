"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { useBreakGlass } from "@/lib/break-glass/context";

export default function RollbackPanel() {
  const { accessToken, hasRole } = useAuth();
  const breakGlass = useBreakGlass();
  const config = useMemo(() => getRuntimeConfigStatus(), []);

  const [tenantId, setTenantId] = useState("");
  const [ticketId, setTicketId] = useState("");
  const [reason, setReason] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const disabled =
    !config.config ||
    !tenantId.trim() ||
    !ticketId.trim() ||
    isLoading ||
    !breakGlass.isActive ||
    !hasRole("admin");

  async function submitRollback() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setStatus(null);

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      breakGlassTokenProvider: () => breakGlass.state?.tokenId,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": "approver,admin",
      }),
    });

    try {
      await client.approvalComplete({
        ticket_id: ticketId.trim(),
        decision: "approve",
        approver_id: "rollback-admin",
        reason: reason.trim() || "Rollback approved from UI.",
      });
      setStatus("Rollback approval recorded.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Rollback approval failed.");
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Rollback approval</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Documents</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Use an approval ticket to record a governed rollback decision with audit context.
      </p>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={ticketId}
          onChange={(event) => setTicketId(event.target.value)}
          placeholder="approval ticket_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
      </div>
      <textarea
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        placeholder="rollback reason / audit note"
        className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        rows={3}
      />
      <button
        className="mt-3 rounded-2xl bg-slate-900 px-5 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
        onClick={() => void submitRollback()}
        disabled={disabled}
      >
        {isLoading ? "Recording..." : "Approve rollback"}
      </button>
      {!breakGlass.isActive ? (
        <div className="mt-3 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-700">
          Break-glass token required to approve rollback.
        </div>
      ) : null}
      {!hasRole("admin") ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          Admin role required to approve rollback.
        </div>
      ) : null}
      {status ? (
        <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-700">
          {status}
        </div>
      ) : null}
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
    </div>
  );
}
