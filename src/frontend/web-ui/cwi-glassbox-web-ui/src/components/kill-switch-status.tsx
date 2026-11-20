"use client";

import { useEffect, useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { createTelemetryReporter } from "@/lib/telemetry";
import { useBreakGlass } from "@/lib/break-glass/context";

type StatusState = "loading" | "enabled" | "disabled" | "error";

export default function KillSwitchStatus() {
  const { accessToken, hasRole } = useAuth();
  const breakGlass = useBreakGlass();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [tenantId, setTenantId] = useState("");
  const [killCode, setKillCode] = useState("");
  const [state, setState] = useState<StatusState>(config.config ? "loading" : "error");
  const [detail, setDetail] = useState<string | null>(
    config.config ? null : "Missing API configuration."
  );
  const [success, setSuccess] = useState<string | null>(null);

  const telemetry = useMemo(() => {
    if (!config.config) {
      return null;
    }
    return createTelemetryReporter({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantProvider: () => tenantId,
      surface: "kill-switch",
    });
  }, [accessToken, config, tenantId]);

  const client = useMemo(() => {
    if (!config.config) {
      return null;
    }
    return createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      breakGlassTokenProvider: () => breakGlass.state?.tokenId,
    });
  }, [accessToken, breakGlass.state?.tokenId, config]);

  useEffect(() => {
    if (!client) {
      return;
    }

    client
      .killSwitchStatus()
      .then((payload) => {
        setState(payload.enabled ? "enabled" : "disabled");
        void telemetry?.report("ui.kill_switch.status.succeeded", {
          enabled: payload.enabled,
        });
      })
      .catch((err) => {
        setState("error");
        setDetail(err instanceof Error ? err.message : "Status unavailable.");
        void telemetry?.report("ui.kill_switch.status.failed", {
          error: err instanceof Error ? err.message : "unknown",
        });
      });
  }, [client, telemetry]);

  async function submitToggle(enabled: boolean) {
    if (!client) {
      setState("error");
      setDetail("Missing API configuration.");
      return;
    }

    setDetail(null);
    setSuccess(null);
    setState("loading");
    void telemetry?.report("ui.kill_switch.toggle.requested", { enabled });

    try {
      const result = await client.toggleKillSwitch({
        enabled,
        kill_code: killCode.trim(),
      });
      setState(result.enabled ? "enabled" : "disabled");
      setSuccess(result.enabled ? "Kill-switch enabled." : "Kill-switch disabled.");
      setKillCode("");
      void telemetry?.report("ui.kill_switch.toggle.succeeded", {
        enabled: result.enabled,
      });
    } catch (err) {
      setState("error");
      setDetail(err instanceof Error ? err.message : "Toggle failed.");
      void telemetry?.report("ui.kill_switch.toggle.failed", {
        enabled,
        error: err instanceof Error ? err.message : "unknown",
      });
    }
  }

  const label =
    state === "loading"
      ? "Checking..."
      : state === "enabled"
        ? "Enabled"
        : state === "disabled"
          ? "Disabled"
          : "Unavailable";

  const color =
    state === "enabled"
      ? "text-rose-700"
      : state === "disabled"
        ? "text-emerald-700"
        : "text-slate-500";

  const actionDisabled = !breakGlass.isActive || !hasRole("admin");

  return (
    <div className="rounded-2xl border border-white/70 bg-white/80 p-5">
      <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Kill-switch</p>
      <p className={`mt-3 text-xl font-semibold ${color}`}>{label}</p>
      <p className="mt-2 text-sm text-slate-600">
        {detail ?? "Kill switch state from orchestrator admin API."}
      </p>
      <div className="mt-4 space-y-3">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id (for telemetry)"
          className="w-full rounded-2xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={killCode}
          onChange={(event) => setKillCode(event.target.value)}
          placeholder="kill code"
          className="w-full rounded-2xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 focus:border-slate-400 focus:outline-none"
          type="password"
        />
        <div className="flex flex-wrap gap-3">
          <button
            className="rounded-2xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-rose-300"
            onClick={() => void submitToggle(true)}
            disabled={!killCode.trim() || actionDisabled}
          >
            Enable
          </button>
          <button
            className="rounded-2xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-emerald-300"
            onClick={() => void submitToggle(false)}
            disabled={!killCode.trim() || actionDisabled}
          >
            Disable
          </button>
        </div>
        {actionDisabled ? (
          <div className="rounded-2xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700">
            Admin role + break-glass token required to toggle kill-switch.
          </div>
        ) : null}
        {success ? (
          <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
            {success}
          </div>
        ) : null}
      </div>
    </div>
  );
}
