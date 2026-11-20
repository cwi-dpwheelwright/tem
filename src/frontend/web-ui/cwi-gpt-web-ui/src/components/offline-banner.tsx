"use client";

import { useEffect, useMemo, useState } from "react";
import { initOfflineListeners, markOffline, markOnline, useOfflineStatus } from "@/lib/offline";
import { retryWithBackoff } from "@/lib/retry";
import { resolveRuntimeConfig } from "@/lib/config";
import { loadOverrides } from "@/lib/storage";

async function checkConnectivity(url: string): Promise<boolean> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);
  try {
    await fetch(url, {
      method: "HEAD",
      mode: "no-cors",
      cache: "no-store",
      signal: controller.signal,
    });
    return true;
  } catch {
    return false;
  } finally {
    clearTimeout(timeout);
  }
}

export function OfflineBanner() {
  const { isOffline, reason, lastChangedAt } = useOfflineStatus();
  const [checking, setChecking] = useState(false);
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);

  useEffect(() => initOfflineListeners(), []);

  const retry = async () => {
    setChecking(true);
    try {
      await retryWithBackoff(
        async () => {
          const ok = await checkConnectivity(runtime.config.gatewayBaseUrl);
          if (!ok) {
            throw new Error("offline");
          }
          return true;
        },
        {
          method: "GET",
          maxAttempts: 3,
          baseDelayMs: 500,
          maxDelayMs: 3000,
          jitterRatio: 0.2,
          shouldRetry: () => true,
        },
      );
      markOnline();
    } catch {
      markOffline("network");
    } finally {
      setChecking(false);
    }
  };

  if (!isOffline) {
    return null;
  }

  return (
    <div className="offline-banner">
      <div className="offline-banner-inner">
        <div className="offline-banner-content">
          <div className="offline-banner-title">Offline mode</div>
          <div className="offline-banner-text">
            Network connectivity is unavailable ({reason ?? "unknown"}). Ask, action, and pack verification are
            disabled until the connection returns.
          </div>
          {lastChangedAt ? (
            <div className="offline-banner-timestamp">Last change: {lastChangedAt}</div>
          ) : null}
        </div>
        <button
          className="offline-banner-button"
          onClick={retry}
          disabled={checking}
        >
          {checking ? "Checking..." : "Retry connection"}
        </button>
      </div>
    </div>
  );
}
