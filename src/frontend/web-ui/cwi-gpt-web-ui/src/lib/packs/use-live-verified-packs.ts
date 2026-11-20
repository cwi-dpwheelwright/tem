"use client";

import { useCallback, useEffect, useState } from "react";
import { resolveAuthToken } from "@/lib/auth";
import type { AuthMode } from "@/lib/config";
import { fetchLiveVerifiedPacks } from "@/lib/packs/client";
import { clearAllVerifiedPacks } from "@/lib/packs/store";
import type { VerifiedPack } from "@/lib/packs/types";

export type LiveVerifiedPacksStatus = "idle" | "loading" | "ready" | "error";

interface UseLiveVerifiedPacksOptions {
  authMode: AuthMode;
  authIdleTimeoutMs: number;
  requestTimeoutMs: number;
  onMissingAuth?: () => void;
  baseUrl?: string;
}

export function useLiveVerifiedPacks({
  authMode,
  authIdleTimeoutMs,
  requestTimeoutMs,
  onMissingAuth,
  baseUrl = "/api",
}: UseLiveVerifiedPacksOptions) {
  const [packs, setPacks] = useState<VerifiedPack[]>([]);
  const [status, setStatus] = useState<LiveVerifiedPacksStatus>("idle");
  const [error, setError] = useState<string | null>(null);
  const [filteredOutPackCount, setFilteredOutPackCount] = useState(0);

  const refresh = useCallback(async () => {
    setStatus("loading");
    setError(null);
    setPacks([]);
    setFilteredOutPackCount(0);
    clearAllVerifiedPacks();

    try {
      const token = authMode === "none" ? null : await resolveAuthToken(authMode, authIdleTimeoutMs);
      if (authMode !== "none" && !token) {
        setStatus("error");
        onMissingAuth?.();
        return;
      }

      const result = await fetchLiveVerifiedPacks(baseUrl, requestTimeoutMs, token);
      setPacks(result.packs);
      setFilteredOutPackCount(result.filteredOutPacks.length);
      setStatus("ready");
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to load live verified packs.";
      setError(message);
      setStatus("error");
    }
  }, [authIdleTimeoutMs, authMode, baseUrl, onMissingAuth, requestTimeoutMs]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return {
    packs,
    status,
    error,
    filteredOutPackCount,
    refresh,
    isLoading: status === "loading",
    isReady: status === "ready",
  };
}
