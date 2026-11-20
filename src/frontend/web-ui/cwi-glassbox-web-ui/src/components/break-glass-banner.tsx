"use client";

import { useEffect, useMemo, useState } from "react";
import { useBreakGlass, formatCountdown } from "@/lib/break-glass/context";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { createTelemetryReporter } from "@/lib/telemetry";

export default function BreakGlassBanner() {
  const { accessToken } = useAuth();
  const { state, isActive, expiresAt, clearToken } = useBreakGlass();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [now, setNow] = useState(() => new Date());
  const [message, setMessage] = useState<string | null>(null);
  const tokenId = state?.tokenId;

  useEffect(() => {
    const interval = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(interval);
  }, []);

  const telemetry = useMemo(() => {
    if (!config.config || !tokenId) {
      return null;
    }
    return createTelemetryReporter({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantProvider: () => tokenId,
      surface: "break-glass",
    });
  }, [accessToken, config, tokenId]);

  if (!isActive || !state) {
    return null;
  }

  const countdown = formatCountdown(expiresAt, now);

  async function revokeToken() {
    if (!tokenId) {
      setMessage("No active break-glass token.");
      return;
    }
    if (!config.config) {
      setMessage("Missing API configuration.");
      return;
    }
    setMessage(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });
    try {
      await client.breakGlassRevoke({ token_id: tokenId });
      clearToken();
      setMessage("Break-glass revoked.");
      void telemetry?.report("ui.break_glass.revoke.succeeded", {
        token_id: tokenId,
      });
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Revoke failed.");
      void telemetry?.report("ui.break_glass.revoke.failed", {
        token_id: tokenId,
        error: err instanceof Error ? err.message : "unknown",
      });
    }
  }

  return (
    <div className="sticky top-0 z-50 border-b border-amber-200 bg-amber-50/90 px-6 py-3 backdrop-blur">
      <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center justify-between gap-3 text-sm text-amber-900">
        <div className="flex items-center gap-3">
          <span className="rounded-full bg-amber-200 px-3 py-1 text-xs font-semibold uppercase tracking-[0.2em] text-amber-900">
            Break-glass active
          </span>
          <span className="text-xs text-amber-800">
            Token ends in <span className="font-semibold">{countdown}</span>
          </span>
        </div>
        <div className="flex items-center gap-3">
          <span className="hidden text-xs text-amber-700 md:inline">
            Token: {state.tokenId.slice(0, 8)}...
          </span>
          <button
            className="rounded-full bg-amber-900 px-4 py-2 text-xs font-semibold text-white"
            onClick={() => void revokeToken()}
          >
            Revoke access
          </button>
        </div>
      </div>
      {message ? (
        <div className="mx-auto mt-2 w-full max-w-6xl text-xs text-amber-800">
          {message}
        </div>
      ) : null}
    </div>
  );
}
