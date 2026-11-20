"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import AppShell from "@/components/app-shell";
import { createApiClient } from "@/lib/api/client";
import type {
  OwnershipLineageResponse,
  OwnershipRevocationEntry,
  OwnershipTransferRequest,
  OwnershipTransferRevokeRequest,
} from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";
import { resolveUserId } from "@/lib/auth/user-id";
import { useBreakGlass } from "@/lib/break-glass/context";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useTenant } from "@/lib/tenant/context";
import { useTelemetry } from "@/lib/telemetry/context";

const STATUS_STYLE: Record<string, string> = {
  active: "bg-emerald-50 text-emerald-700 border-emerald-200",
  pending: "bg-amber-50 text-amber-700 border-amber-200",
  revoked: "bg-rose-50 text-rose-700 border-rose-200",
  confirmed: "bg-emerald-50 text-emerald-700 border-emerald-200",
};

export default function OwnershipPage() {
  const { trackEvent } = useTelemetry();
  const { accessToken, user } = useAuth();
  const { tenantId } = useTenant();
  const { state: breakGlassState, isActive: breakGlassActive } = useBreakGlass();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [lineage, setLineage] = useState<OwnershipLineageResponse | null>(null);
  const [revocations, setRevocations] = useState<OwnershipRevocationEntry[]>([]);
  const [delegateName, setDelegateName] = useState("");
  const [delegateScope, setDelegateScope] = useState("");
  const [confirming, setConfirming] = useState<OwnershipRevocationEntry | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    trackEvent("page_view", "ownership", { page: "ownership" });
  }, [trackEvent]);

  const ownerNode = useMemo(
    () => lineage?.nodes?.find((node) => node.role === "Owner"),
    [lineage?.nodes],
  );

  const nodes = lineage?.nodes ?? [];
  const edges = lineage?.edges ?? [];

  const loadOwnership = useCallback(async () => {
    if (!config.config?.apiBaseUrl) {
      setError("Missing API base URL configuration.");
      setIsLoading(false);
      return;
    }

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
      breakGlassTokenProvider: () => breakGlassState?.tokenId,
    });

    setIsLoading(true);
    setError(null);
    try {
      const [lineageResponse, revocationResponse] = await Promise.all([
        client.ownershipLineage(tenantId),
        client.ownershipRevocations(tenantId),
      ]);
      setLineage(lineageResponse);
      setRevocations(revocationResponse);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load ownership data.");
    } finally {
      setIsLoading(false);
    }
  }, [accessToken, breakGlassState?.tokenId, config.config?.apiBaseUrl, tenantId]);

  useEffect(() => {
    void loadOwnership();
  }, [loadOwnership]);

  async function addDelegate() {
    if (!delegateName.trim() || !delegateScope.trim()) {
      return;
    }
    if (!breakGlassActive) {
      setError("Break-glass access is required to delegate ownership.");
      return;
    }

    if (!config.config?.apiBaseUrl) {
      setError("Missing API base URL configuration.");
      return;
    }

    const actorId = resolveUserId(user);
    const payload: OwnershipTransferRequest = {
      tenant_id: tenantId,
      from_user_id: ownerNode?.node_id ?? actorId,
      to_user_id: delegateName.trim(),
      transfer_type: "delegate",
      reason: `scope:${delegateScope.trim()}; requested_by:${actorId}`,
      required_approvals: 1,
      approver_ids: [actorId],
    };

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
      breakGlassTokenProvider: () => breakGlassState?.tokenId,
    });

    setError(null);
    try {
      await client.ownershipDelegate(payload);
      setDelegateName("");
      setDelegateScope("");
      trackEvent("delegate_added", "ownership", { scope: delegateScope.trim() });
      await loadOwnership();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to delegate ownership.");
    }
  }

  async function confirmRevocation(entry: OwnershipRevocationEntry) {
    if (!breakGlassActive) {
      setError("Break-glass access is required to revoke ownership.");
      return;
    }

    if (!config.config?.apiBaseUrl) {
      setError("Missing API base URL configuration.");
      return;
    }

    const payload: OwnershipTransferRevokeRequest = {
      request_id: entry.request_id,
      actor_id: resolveUserId(user),
      reason: `scope:${entry.scope}; revoked_by:${resolveUserId(user)}`,
    };

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
      breakGlassTokenProvider: () => breakGlassState?.tokenId,
    });

    setError(null);
    try {
      await client.ownershipRevoke(payload);
      setConfirming(null);
      trackEvent("delegate_revoked", "ownership", { revocationId: entry.request_id });
      await loadOwnership();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to revoke ownership.");
    }
  }

  return (
    <AppShell
      title="Ownership & succession"
      subtitle="Manage lineage graphs, delegate approvals, and revocation lists with confirmation prompts."
      badge="Ownership"
    >
      <div className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
        <section className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="text-sm font-semibold text-slate-800">Lineage graph</p>
                <p className="mt-1 text-xs text-slate-600">
                  Track owners, delegates, and successors for each operational scope.
                </p>
              </div>
              <span className="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.25em] text-slate-500">
                RBAC
              </span>
            </div>

            <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4">
              <svg viewBox="0 0 520 240" className="h-56 w-full">
                <line x1="260" y1="40" x2="140" y2="120" stroke="#cbd5f5" strokeWidth="2" />
                <line x1="260" y1="40" x2="380" y2="120" stroke="#cbd5f5" strokeWidth="2" />
                <line x1="260" y1="40" x2="260" y2="200" stroke="#cbd5f5" strokeWidth="2" />
                <circle cx="260" cy="40" r="28" fill="#0f172a" />
                <text x="260" y="45" textAnchor="middle" fill="#fff" fontSize="10">
                  Owner
                </text>
                <circle cx="140" cy="120" r="24" fill="#f59e0b" />
                <text x="140" y="125" textAnchor="middle" fill="#fff" fontSize="9">
                  Delegate
                </text>
                <circle cx="380" cy="120" r="24" fill="#22c55e" />
                <text x="380" y="125" textAnchor="middle" fill="#fff" fontSize="9">
                  Delegate
                </text>
                <circle cx="260" cy="200" r="24" fill="#64748b" />
                <text x="260" y="205" textAnchor="middle" fill="#fff" fontSize="9">
                  Successor
                </text>
              </svg>
            </div>

            {isLoading ? (
              <div className="mt-4 rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
                Loading ownership graph...
              </div>
            ) : null}
            {error ? (
              <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
                {error}
              </div>
            ) : null}

            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              {nodes.map((node) => (
                <div key={node.node_id} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-slate-900">{node.label}</p>
                    <span
                      className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-[0.2em] ${
                        STATUS_STYLE[node.status] ?? STATUS_STYLE.active
                      }`}
                    >
                      {node.status}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">Role: {node.role}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-sm font-semibold text-slate-800">Delegate editor</p>
            <p className="mt-1 text-xs text-slate-600">Add delegates and assign scopes before activation.</p>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <input
                value={delegateName}
                onChange={(event) => setDelegateName(event.target.value)}
                placeholder="Delegate name"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <input
                value={delegateScope}
                onChange={(event) => setDelegateScope(event.target.value)}
                placeholder="Scope (e.g., Ops)"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
            </div>
            <button
              className="mt-3 rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
              onClick={addDelegate}
              disabled={!delegateName.trim() || !delegateScope.trim()}
            >
              Add delegate
            </button>

            <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
              {edges.length} relationships tracked across {nodes.length} actors.
            </div>
          </div>
        </section>

        <aside className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-sm font-semibold text-slate-800">Revocation list</p>
            <p className="mt-1 text-xs text-slate-600">Confirm revocations before removing delegated access.</p>
            <div className="mt-4 space-y-3">
              {revocations.map((entry) => (
                <div key={entry.request_id} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-slate-900">{entry.subject}</p>
                    <span
                      className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-[0.2em] ${
                        STATUS_STYLE[entry.status] ?? STATUS_STYLE.pending
                      }`}
                    >
                      {entry.status}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">Scope: {entry.scope}</p>
                  <p className="mt-2 text-xs text-slate-600">Reason: {entry.reason}</p>
                  <p className="mt-2 text-xs text-slate-500">
                    Requested by {entry.requested_by} - {new Date(entry.updated_utc).toLocaleString()}
                  </p>
                  {entry.status !== "revoked" ? (
                    <button
                      className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-3 py-1 text-xs font-semibold text-rose-700"
                      onClick={() => setConfirming(entry)}
                    >
                      Revoke access
                    </button>
                  ) : null}
                </div>
              ))}
            </div>
          </div>
        </aside>
      </div>

      {confirming ? (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-slate-900/30 px-4">
          <div className="w-full max-w-md rounded-3xl border border-slate-200 bg-white p-6 shadow-xl">
            <p className="text-sm font-semibold text-slate-900">Confirm revocation</p>
            <p className="mt-2 text-sm text-slate-600">
              Revoke {confirming.subject} for scope {confirming.scope}? This action is immediate.
            </p>
            <div className="mt-4 flex justify-end gap-3">
              <button
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700"
                onClick={() => setConfirming(null)}
              >
                Cancel
              </button>
              <button
                className="rounded-2xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white"
                onClick={() => confirmRevocation(confirming)}
              >
                Confirm revoke
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </AppShell>
  );
}

