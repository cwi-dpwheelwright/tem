"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { useBreakGlass } from "@/lib/break-glass/context";
import { createTelemetryReporter } from "@/lib/telemetry";

const DEFAULT_DURATION = 30;

export default function BreakGlassPanel() {
  const { accessToken } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const { state, isActive, isExpired, setToken, clearToken } = useBreakGlass();
  const [isOpen, setIsOpen] = useState(false);
  const [tenantId, setTenantId] = useState("");
  const [userId, setUserId] = useState("");
  const [role, setRole] = useState("admin");
  const [scope, setScope] = useState("admin");
  const [durationMinutes, setDurationMinutes] = useState(DEFAULT_DURATION);
  const [reason, setReason] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const telemetry = useMemo(() => {
    if (!config.config) {
      return null;
    }
    return createTelemetryReporter({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantProvider: () => tenantId,
      surface: "break-glass",
    });
  }, [accessToken, config, tenantId]);

  const reasonRequired = true;
  const invalid =
    !tenantId.trim() ||
    !userId.trim() ||
    !role.trim() ||
    !scope.trim() ||
    durationMinutes <= 0 ||
    (reasonRequired && !reason.trim());

  async function submitIssue() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (invalid) {
      setError("Complete all required fields before submitting.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setStatus(null);
    void telemetry?.report("ui.break_glass.issue.requested", {
      tenant_id: tenantId.trim(),
      scope,
    });

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      const response = await client.breakGlassIssue({
        tenant_id: tenantId.trim(),
        user_id: userId.trim(),
        role: role.trim(),
        scope: scope.trim(),
        duration_minutes: durationMinutes,
        reason: reason.trim() || undefined,
      });
      setToken(response.token_id, response.expires_utc, new Date().toISOString());
      setStatus("Break-glass token issued.");
      setReason("");
      void telemetry?.report("ui.break_glass.issue.succeeded", {
        tenant_id: tenantId.trim(),
        token_id: response.token_id,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Issue failed.");
      void telemetry?.report("ui.break_glass.issue.failed", {
        tenant_id: tenantId.trim(),
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  async function submitRevoke() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!state?.tokenId) {
      setError("No active break-glass token.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setStatus(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      await client.breakGlassRevoke({ token_id: state.tokenId });
      clearToken();
      setStatus("Break-glass token revoked.");
      void telemetry?.report("ui.break_glass.revoke.succeeded", {
        token_id: state.tokenId,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Revoke failed.");
      void telemetry?.report("ui.break_glass.revoke.failed", {
        token_id: state.tokenId,
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Break-glass access</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Admin mode</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Request time-boxed access for emergency operations. All actions are audited.
      </p>
      <div className="mt-4 flex flex-wrap items-center gap-3">
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white"
          data-testid="break-glass-request-open"
          onClick={() => setIsOpen(true)}
        >
          Request access
        </button>
        <button
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
          data-testid="break-glass-revoke"
          onClick={() => void submitRevoke()}
          disabled={!isActive || isLoading}
        >
          Revoke active token
        </button>
        {isExpired ? (
          <span className="text-xs text-rose-600">Token expired. Actions are disabled.</span>
        ) : null}
      </div>
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

      {isOpen ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
          data-testid="break-glass-request-modal"
        >
          <div className="w-full max-w-xl rounded-3xl bg-white p-6 shadow-2xl">
            <div className="flex items-center justify-between">
              <p className="text-base font-semibold text-slate-900">Break-glass request</p>
              <button
                className="rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-600"
                onClick={() => setIsOpen(false)}
              >
                Close
              </button>
            </div>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <input
                value={tenantId}
                onChange={(event) => setTenantId(event.target.value)}
                placeholder="tenant_id"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <input
                value={userId}
                onChange={(event) => setUserId(event.target.value)}
                placeholder="user_id"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <input
                value={role}
                onChange={(event) => setRole(event.target.value)}
                placeholder="role"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <input
                value={scope}
                onChange={(event) => setScope(event.target.value)}
                placeholder="scope"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <input
                type="number"
                min={1}
                value={durationMinutes}
                onChange={(event) => setDurationMinutes(Number(event.target.value))}
                placeholder="duration_minutes"
                className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
            </div>
            <textarea
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="reason (required)"
              className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              rows={3}
            />
            <div className="mt-4 flex flex-wrap gap-3">
              <button
                className="rounded-2xl bg-slate-900 px-5 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
                data-testid="break-glass-issue"
                onClick={() => void submitIssue()}
                disabled={invalid || isLoading}
              >
                {isLoading ? "Submitting..." : "Issue token"}
              </button>
              <button
                className="rounded-2xl border border-slate-200 bg-white px-5 py-2 text-sm font-semibold text-slate-700"
                onClick={() => setIsOpen(false)}
              >
                Cancel
              </button>
            </div>
            {invalid ? (
              <p className="mt-3 text-xs text-amber-600">
                Fill in all required fields. Reason is mandatory by policy.
              </p>
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}
