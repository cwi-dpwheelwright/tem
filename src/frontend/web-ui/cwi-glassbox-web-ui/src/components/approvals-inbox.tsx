"use client";

import { useEffect, useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { resolveUserId } from "@/lib/auth/user-id";
import { createTelemetryReporter } from "@/lib/telemetry";
import type { ApprovalTicketStatus } from "@/lib/api/types";

const DEFAULT_LIMIT = 25;

export default function ApprovalsInbox() {
  const { accessToken, roles: userRoles, hasRole, user } = useAuth();
  const [tenantId, setTenantId] = useState("");
  const [roles, setRoles] = useState(userRoles.join(","));
  const [tickets, setTickets] = useState<ApprovalTicketStatus[]>([]);
  const [selected, setSelected] = useState<ApprovalTicketStatus | null>(null);
  const [decisionReason, setDecisionReason] = useState("");
  const [decisionStatus, setDecisionStatus] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const canOperate = hasRole("operator");

  useEffect(() => {
    if (!roles.trim() && userRoles.length > 0) {
      setRoles(userRoles.join(","));
    }
  }, [roles, userRoles]);

  const telemetry = useMemo(() => {
    if (!config.config) {
      return null;
    }
    return createTelemetryReporter({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantProvider: () => tenantId,
      surface: "approvals-inbox",
    });
  }, [accessToken, config, tenantId]);

  async function loadList() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!tenantId.trim()) {
      setError("Tenant ID is required.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setSelected(null);
    void telemetry?.report("ui.approvals.list.requested", {
      roles: roles.trim(),
    });

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": roles.trim(),
      }),
    });

    try {
      const list = await client.approvalsList(tenantId.trim(), DEFAULT_LIMIT);
      setTickets(list);
      void telemetry?.report("ui.approvals.list.succeeded", {
        count: list.length,
      });
    } catch (err) {
      setTickets([]);
      setError(err instanceof Error ? err.message : "Failed to load approvals.");
      void telemetry?.report("ui.approvals.list.failed", {
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  async function loadDetail(ticketId: string) {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }

    setIsLoading(true);
    setError(null);
    void telemetry?.report("ui.approvals.detail.requested", {
      ticket_id: ticketId,
    });

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": roles.trim(),
      }),
    });

    try {
      const detail = await client.approvalStatus(ticketId);
      setSelected(detail);
      void telemetry?.report("ui.approvals.detail.succeeded", {
        ticket_id: ticketId,
        status: detail.status,
      });
    } catch (err) {
      setSelected(null);
      setError(err instanceof Error ? err.message : "Failed to load approval.");
      void telemetry?.report("ui.approvals.detail.failed", {
        ticket_id: ticketId,
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  async function submitDecision(decision: "approve" | "deny") {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!selected) {
      setError("Select a ticket first.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setDecisionStatus(null);
    void telemetry?.report("ui.approvals.decision.requested", {
      ticket_id: selected.ticket_id,
      decision,
    });

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": roles.trim(),
      }),
    });

    try {
      await client.approvalComplete({
        ticket_id: selected.ticket_id,
        decision,
        approver_id: resolveUserId(user),
        reason: decisionReason.trim() || undefined,
      });
      setDecisionStatus(`Ticket ${decision}d successfully.`);
      void telemetry?.report("ui.approvals.decision.succeeded", {
        ticket_id: selected.ticket_id,
        decision,
      });
      await loadList();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to submit decision.");
      void telemetry?.report("ui.approvals.decision.failed", {
        ticket_id: selected.ticket_id,
        decision,
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div
      className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60"
      data-testid="approvals-inbox"
    >
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Approvals inbox</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Governance</span>
      </div>
      <div className="mt-5 grid gap-3 sm:grid-cols-2">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={roles}
          onChange={(event) => setRoles(event.target.value)}
          placeholder="roles (comma-separated)"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
      </div>
      <button
        className="mt-4 rounded-2xl bg-slate-900 px-5 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
        onClick={() => void loadList()}
        disabled={isLoading || !canOperate}
      >
        {isLoading ? "Loading..." : "Load approvals"}
      </button>
      {!canOperate ? (
        <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          Operator role required to review approvals.
        </div>
      ) : null}
      {error ? (
        <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {error}
        </div>
      ) : null}
      <div className="mt-5 grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="space-y-3">
          {tickets.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-4 text-xs text-slate-500">
              No approvals loaded yet.
            </div>
          ) : (
            tickets.map((ticket) => (
              <button
                key={ticket.ticket_id}
                onClick={() => void loadDetail(ticket.ticket_id)}
                className="w-full rounded-2xl border border-white/80 bg-white/90 px-4 py-3 text-left text-sm text-slate-700 hover:border-slate-300"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">{ticket.ticket_id}</span>
                  <span className="text-xs uppercase tracking-[0.2em] text-slate-500">
                    {ticket.status}
                  </span>
                </div>
                <div className="mt-2 text-xs text-slate-500">
                  {ticket.risk} - {new Date(ticket.updated_utc).toLocaleString()}
                </div>
              </button>
            ))
          )}
        </div>
        <div className="rounded-2xl border border-white/70 bg-white/90 p-4 text-sm text-slate-700">
          {selected ? (
            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Ticket detail</p>
              <p className="text-base font-semibold">{selected.ticket_id}</p>
              <p className="text-xs text-slate-500">Status: {selected.status}</p>
              <p className="text-xs text-slate-500">Risk: {selected.risk}</p>
              <p className="text-xs text-slate-500">
                Updated: {new Date(selected.updated_utc).toLocaleString()}
              </p>
              <p className="text-sm text-slate-700">{selected.reason}</p>
              <div className="pt-2">
                <label className="text-xs uppercase tracking-[0.2em] text-slate-500">
                  Decision reason
                </label>
                <textarea
                  value={decisionReason}
                  onChange={(event) => setDecisionReason(event.target.value)}
                  className="mt-2 w-full rounded-2xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 focus:border-slate-400 focus:outline-none"
                  rows={3}
                />
              </div>
              <div className="flex flex-wrap gap-3 pt-2">
                <button
                  className="rounded-2xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-emerald-300"
                  onClick={() => void submitDecision("approve")}
                  disabled={isLoading || !canOperate}
                >
                  Approve
                </button>
                <button
                  className="rounded-2xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-rose-300"
                  onClick={() => void submitDecision("deny")}
                  disabled={isLoading || !canOperate}
                >
                  Deny
                </button>
              </div>
              {decisionStatus ? (
                <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
                  {decisionStatus}
                </div>
              ) : null}
            </div>
          ) : (
            <p className="text-xs text-slate-500">Select a ticket to see details.</p>
          )}
        </div>
      </div>
    </div>
  );
}
