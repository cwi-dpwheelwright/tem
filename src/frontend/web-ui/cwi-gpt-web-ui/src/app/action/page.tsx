"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError, createApiClient, formatApiError, getApiErrorPayloadString } from "@/lib/api/client";
import { logApiError } from "@/lib/logger";
import { reportError } from "@/lib/error-reporting";
import { formatDateTime } from "@/lib/format";
import type { ActionResponse, ApprovalTicketStatus } from "@/lib/api/types";
import { AUTH_NOTICE_MESSAGE, handleAuthRedirect, resolveAuthToken } from "@/lib/auth";
import { resolveRuntimeConfig } from "@/lib/config";
import { expireAuthSession, loadOverrides } from "@/lib/storage";
import { loadSelectedPackName, saveSelectedPackName } from "@/lib/packs/selection";
import { usePackCatalogAvailability } from "@/lib/packs/use-pack-catalog-availability";
import { findMatchingVerifiedPack, resolveCanonicalVerifiedPackSelection } from "@/lib/packs/reconcile";
import { useLiveVerifiedPacks } from "@/lib/packs/use-live-verified-packs";
import type { PackAction } from "@/lib/packs/types";
import { NoValidPacksModal } from "@/components/no-valid-packs-modal";
import { TracePanel } from "@/components/trace-panel";
import { useOfflineStatus } from "@/lib/offline";
import { resolveSessionIdentity } from "@/lib/session-identity";

interface ActionChoice {
  moduleId: string;
  moduleTitle: string;
  action: PackAction;
}

export default function ActionPage() {
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const router = useRouter();
  const handleMissingPackCatalogAuth = useCallback(() => {
    expireAuthSession(AUTH_NOTICE_MESSAGE);
    router.replace("/login");
  }, [router]);
  const [selectedPackName, setSelectedPackName] = useState(() => loadSelectedPackName());
  const [selected, setSelected] = useState<ActionChoice | null>(null);
  const [response, setResponse] = useState<ActionResponse | null>(null);
  const [approvalStatus, setApprovalStatus] = useState<ApprovalTicketStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { isOffline } = useOfflineStatus();
  const { isEmpty: isPackCatalogEmpty, emptyStateSummary } = usePackCatalogAvailability({
    authMode: runtime.config.authMode,
    authIdleTimeoutMs: runtime.config.authIdleTimeoutMs,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });
  const {
    packs: liveVerifiedPacks,
    status: livePackStatus,
    error: livePackError,
  } = useLiveVerifiedPacks({
    authMode: runtime.config.authMode,
    authIdleTimeoutMs: runtime.config.authIdleTimeoutMs,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
    onMissingAuth: handleMissingPackCatalogAuth,
  });

  const pack = useMemo(
    () => findMatchingVerifiedPack(selectedPackName, liveVerifiedPacks),
    [liveVerifiedPacks, selectedPackName],
  );

  useEffect(() => {
    if (!selectedPackName || livePackStatus !== "ready") {
      return;
    }

    const canonical = resolveCanonicalVerifiedPackSelection(selectedPackName, liveVerifiedPacks);
    if (canonical) {
      setSelectedPackName(canonical);
      saveSelectedPackName(canonical);
      return;
    }

    if (!pack) {
      const removedPack = selectedPackName;
      queueMicrotask(() => {
        setSelectedPackName(null);
        saveSelectedPackName(null);
        setSelected(null);
        setError(`Selected pack "${removedPack}" is no longer in the live verified catalog. Choose another pack in Trust.`);
      });
    }
  }, [livePackStatus, liveVerifiedPacks, pack, selectedPackName]);

  useEffect(() => {
    if (isPackCatalogEmpty) {
      document.body.classList.add("modal-open");
      return () => document.body.classList.remove("modal-open");
    }

    document.body.classList.remove("modal-open");
  }, [isPackCatalogEmpty]);

  const actions = useMemo(() => {
    if (!pack) {
      return [] as ActionChoice[];
    }
    const results: ActionChoice[] = [];
    pack.modules.forEach((module) => {
      module.actions.forEach((action) => {
        results.push({
          moduleId: module.id,
          moduleTitle: module.title || module.id,
          action,
        });
      });
    });
    return results;
  }, [pack]);

  useEffect(() => {
    if (!selected) {
      return;
    }
    const stillExists = actions.some((choice) =>
      choice.moduleId === selected.moduleId && choice.action.id === selected.action.id,
    );
    if (!stillExists) {
      setSelected(null);
    }
  }, [actions, selected]);

  const invoke = async () => {
    setError(null);
    setResponse(null);
    setApprovalStatus(null);

    if (isOffline) {
      setError("Offline mode: reconnect to invoke actions.");
      return;
    }
    if (!selectedPackName) {
      setError("Select a pack in Trust before invoking actions.");
      return;
    }
    if (!pack) {
      setError(
        livePackStatus === "loading"
          ? "Loading the selected pack from the live verified catalog. Please try again in a moment."
          : "Selected pack is not available in the live verified catalog. Open Trust to refresh.",
      );
      return;
    }
    if (!selected) {
      setError("Select an action from the live verified pack.");
      return;
    }

    let token: string | null = null;
    if (runtime.config.authMode !== "none") {
      token = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
      if (!token) {
        const notice =
          runtime.config.authMode === "oidc"
            ? "Sign in with Identity to continue."
            : AUTH_NOTICE_MESSAGE;
        expireAuthSession(notice);
        router.replace("/login");
        return;
      }
    }

    const identity = resolveSessionIdentity({
      authMode: runtime.config.authMode,
      tenantId: runtime.config.tenantId,
      runtimeUserId: runtime.config.userId,
      token,
    });
    const resolvedTenantId = identity.tenantId;
    if (!resolvedTenantId) {
      setError("Tenant configuration is missing. Contact your administrator.");
      return;
    }
    const resolvedUserId = identity.userId;
    if (!resolvedUserId) {
      setError("User identity is missing from the authenticated session.");
      return;
    }

    const client = createApiClient({
      baseUrl: "",
      token,
      requestTimeoutMs: runtime.config.requestTimeoutMs,
    });

    setLoading(true);
    try {
      const data = await client.action({
        tenant_id: resolvedTenantId,
        user_id: resolvedUserId,
        policy_mode: runtime.config.policyMode,
        action: {
          type: selected.action.actionType,
          payload: selected.action.args,
        },
        pack_hash: pack.hash,
        pack_version: pack.version,
      });
      setResponse(data);
      if (data.approval_ticket) {
        setApprovalStatus({
          ticket_id: data.approval_ticket.ticket_id,
          status: "pending",
          risk: data.approval_ticket.risk,
          reason: data.approval_ticket.reason,
          updated_utc: new Date().toISOString(),
        });
      }
    } catch (err) {
      if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
        return;
      }
      if (err instanceof ApiError) {
        logApiError(err);
        void reportError({
          category: "api",
          summary: "action.request_failed",
          status: err.status,
          requestId: err.requestId,
          traceId: getApiErrorPayloadString(err, "trace_id"),
        });
        setError(formatApiError(err));
      } else if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Action request failed.");
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    const ticket = response?.approval_ticket ?? null;
    if (!ticket) {
      return;
    }
    let cancelled = false;
    let attempts = 0;

    const poll = async () => {
      if (cancelled) {
        return;
      }
      if (isOffline) {
        setError("Offline mode: reconnect to check approval status.");
        return;
      }
      attempts += 1;
      let pollToken: string | null = null;
      if (runtime.config.authMode !== "none") {
        pollToken = await resolveAuthToken(runtime.config.authMode, runtime.config.authIdleTimeoutMs);
        if (!pollToken) {
          if (!cancelled) {
            setError("Sign in required to check approval status.");
          }
          return;
        }
      }
      const client = createApiClient({
        baseUrl: "",
        token: pollToken,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });
      try {
        const status = await client.approvalStatus(ticket.ticket_id);
        if (!cancelled) {
          setApprovalStatus(status);
        }
        if (!status.status.toLowerCase().includes("pending")) {
          return;
        }
      } catch (err) {
        if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
          cancelled = true;
          return;
        }
        if (!cancelled && err instanceof ApiError) {
          logApiError(err);
          void reportError({
            category: "api",
            summary: "approval.status_failed",
            status: err.status,
            requestId: err.requestId,
            traceId: getApiErrorPayloadString(err, "trace_id"),
          });
          setError(formatApiError(err));
          return;
        }
        if (!cancelled && err instanceof Error) {
          setError(err.message);
        }
      }
      if (!cancelled && attempts < 12) {
        setTimeout(poll, 5000);
      }
    };

    poll();

    return () => {
      cancelled = true;
    };
  }, [response?.approval_ticket, runtime.config.gatewayBaseUrl, runtime.config.tenantId, runtime.config.requestTimeoutMs, runtime.config.authMode, runtime.config.authIdleTimeoutMs, router, isOffline]);

  return (
    <>
      <div className="page-stack">
      <div className="card-light">
        <h2 className="card-title">Action</h2>
        <p className="card-subtitle">
          Actions are pulled from the live verified pack. Free-text actions are disabled.
        </p>
        {!selectedPackName ? (
          <div
            className="alert alert-warning"
            role="status"
          >
            No pack selected. <Link className="link-underline" href="/?modal=trust">Select a pack</Link> to continue.
          </div>
        ) : livePackStatus === "loading" ? (
          <div
            className="alert alert-warning"
            role="status"
          >
            Loading the selected pack from the live verified catalog...
          </div>
        ) : !pack ? (
          <div
            className="alert alert-warning"
            role="status"
          >
            Selected pack is not live verified right now. <Link className="link-underline" href="/?modal=trust">Refresh Trust</Link> to continue.
          </div>
        ) : null}
        {pack && actions.length === 0 ? (
          <p className="card-text-muted">
            No actions defined in the pack modules.
          </p>
        ) : actions.length > 0 ? (
          <fieldset className="action-fieldset">
            <legend className="action-legend">Available actions</legend>
            {actions.map((choice) => {
              const id = `${choice.moduleId}-${choice.action.id}`;
              return (
                <label
                  key={id}
                  className={`action-choice ${
                    selected?.action.id === choice.action.id
                      ? "action-choice--selected"
                      : "action-choice--idle"
                  }`}
                >
                  <div className="action-choice-header">
                    <input
                      type="radio"
                      name="action"
                      checked={selected?.action.id === choice.action.id}
                      onChange={() => setSelected(choice)}
                    />
                    <span className="action-choice-title">{choice.action.label}</span>
                  </div>
                  <span className="action-choice-meta">
                    Module: {choice.moduleTitle} - Type: {choice.action.actionType}
                  </span>
                </label>
              );
            })}
          </fieldset>
        ) : null}
        {selected?.action.args && Object.keys(selected.action.args).length > 0 ? (
          <div className="action-args">
            <div className="action-args-title">Action arguments</div>
            <ul className="action-args-list">
              {Object.entries(selected.action.args).map(([key, value]) => (
                <li key={key}>
                  {key}: {value}
                </li>
              ))}
            </ul>
          </div>
        ) : null}
        {isOffline ? (
          <div
            className="alert alert-warning"
            role="status"
          >
            Offline mode: connect to the network to invoke actions.
          </div>
        ) : null}
        {livePackError ? (
          <div
            className="alert alert-error"
            role="alert"
          >
            {livePackError}
          </div>
        ) : null}
        {error ? (
          <div
            className="alert alert-error"
            role="alert"
          >
            {error}
          </div>
        ) : null}
        <div className="panel-actions">
          <button
            className="btn-primary"
            onClick={invoke}
            disabled={loading || isOffline || livePackStatus === "loading"}
          >
            {loading ? "Invoking..." : "Invoke action"}
          </button>
          <Link
            className="btn-secondary"
            href="/settings"
          >
            Settings
          </Link>
        </div>
      </div>

      {response ? (
        <div className="card-light">
          <h3 className="card-title-sm">Action result</h3>
          <p className="card-subtitle">
            Executed: {response.executed ? "Yes" : "No"}
          </p>
          {response.approval_ticket ? (
            <div className="alert alert-warning">
              Approval required: {response.approval_ticket.ticket_id}
            </div>
          ) : null}
          {response.result ? (
            <pre className="code-block">
{JSON.stringify(response.result, null, 2)}
            </pre>
          ) : (
            <p className="card-text-muted-xs">No result payload returned.</p>
          )}
        </div>
      ) : null}

      {approvalStatus ? (
        <div className="card-light">
          <h3 className="card-title-sm">Approval ticket status</h3>
          <div className="status-grid-tight">
            <span>Ticket ID: {approvalStatus.ticket_id}</span>
            <span>Status: {approvalStatus.status}</span>
            <span>Risk: {approvalStatus.risk}</span>
            <span>Reason: {approvalStatus.reason}</span>
            <span>Updated: {formatDateTime(approvalStatus.updated_utc)}</span>
          </div>
        </div>
      ) : null}

        <TracePanel trace={response?.trace} />
      </div>
      {isPackCatalogEmpty ? <NoValidPacksModal availabilitySummary={emptyStateSummary} /> : null}
    </>
  );
}
