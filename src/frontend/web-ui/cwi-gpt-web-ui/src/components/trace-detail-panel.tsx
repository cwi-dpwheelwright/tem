"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { ApiError, createApiClient, formatApiError, getApiErrorPayloadString } from "@/lib/api/client";
import { logApiError } from "@/lib/logger";
import { reportError } from "@/lib/error-reporting";
import type { TraceLookupResponse } from "@/lib/api/types";
import { AUTH_NOTICE_MESSAGE, handleAuthRedirect, resolveAuthToken } from "@/lib/auth";
import { resolveRuntimeConfig } from "@/lib/config";
import { expireAuthSession, loadOverrides } from "@/lib/storage";
import { useOfflineStatus } from "@/lib/offline";

interface TraceDetailPanelProps {
  traceId: string;
  onBack?: () => void;
}

export function TraceDetailPanel({ traceId, onBack }: TraceDetailPanelProps) {
  const router = useRouter();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const [result, setResult] = useState<TraceLookupResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const { isOffline } = useOfflineStatus();
  const [refreshToken, setRefreshToken] = useState(0);
  const safeTraceId = traceId.trim();

  useEffect(() => {
    let active = true;

    const run = async () => {
      if (!safeTraceId) {
        setError("Trace ID missing.");
        setResult(null);
        setLoading(false);
        return;
      }

      if (isOffline) {
        setError("Offline mode: connect to retry trace lookup.");
        setResult(null);
        setLoading(false);
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

      const client = createApiClient({
        baseUrl: "",
        token,
        requestTimeoutMs: runtime.config.requestTimeoutMs,
      });

      setLoading(true);
      setError(null);
      setResult(null);

      client
        .traceLookup(safeTraceId)
        .then((data) => {
          if (!active) {
            return;
          }
          setResult(data);
        })
        .catch((err) => {
          if (!active) {
            return;
          }
          if (handleAuthRedirect(err, (href) => router.push(href), AUTH_NOTICE_MESSAGE, runtime.config.authMode)) {
            return;
          }
          if (err instanceof ApiError) {
            logApiError(err);
            void reportError({
              category: "api",
              summary: "trace.lookup_failed",
              status: err.status,
              requestId: err.requestId,
              traceId: getApiErrorPayloadString(err, "trace_id"),
            });
            setError(formatApiError(err));
          } else if (err instanceof Error) {
            setError(err.message);
          } else {
            setError("Trace lookup failed.");
          }
        })
        .finally(() => {
          if (active) {
            setLoading(false);
          }
        });
    };

    run();

    return () => {
      active = false;
    };
  }, [safeTraceId, runtime.config.gatewayBaseUrl, runtime.config.requestTimeoutMs, runtime.config.tenantId, runtime.config.authMode, runtime.config.authIdleTimeoutMs, router, refreshToken, isOffline]);

  const retryLookup = () => {
    setRefreshToken((prev) => prev + 1);
  };

  return (
    <div className="page-stack">
      <div className="card-light">
        <h2 className="card-title">Trace details</h2>
        <p className="card-subtitle">
          Trace ID: <span className="trace-id">{safeTraceId || "(missing)"}</span>
        </p>
        {onBack ? (
          <div className="panel-actions">
            <button className="btn-secondary" type="button" onClick={onBack}>
              Back to lookup
            </button>
          </div>
        ) : null}
        {isOffline ? (
          <div
            className="alert alert-warning"
            role="status"
          >
            Offline mode: connect to retry trace lookup.
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
        {error ? (
          <button
            className="btn-secondary-sm"
            onClick={retryLookup}
            disabled={loading || isOffline}
          >
            {loading ? "Retrying..." : "Retry lookup"}
          </button>
        ) : null}
        {loading ? (
          <p className="card-text-muted">Fetching trace mapping...</p>
        ) : result ? (
          <div className="status-grid">
            <span>Trace ID: {result.trace_id}</span>
            <span>Document ID: {result.doc_id || "(not found)"}</span>
          </div>
        ) : (
          <p className="card-text-muted">No trace data loaded yet.</p>
        )}
      </div>
    </div>
  );
}
