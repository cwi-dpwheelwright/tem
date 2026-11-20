"use client";

import { useEffect, useState } from "react";
import { resolveAuthToken } from "@/lib/auth";
import type { AuthMode } from "@/lib/config";
import {
  fetchDependencyVerifiedPublicPacks,
  fetchPackAvailabilitySummary,
  type PackAvailabilitySummary,
} from "@/lib/packs/client";

export type PackCatalogAvailabilityStatus = "checking" | "available" | "empty" | "error";

interface UsePackCatalogAvailabilityOptions {
  authMode: AuthMode;
  authIdleTimeoutMs: number;
  requestTimeoutMs: number;
  onMissingAuth?: () => void;
  baseUrl?: string;
}

export function usePackCatalogAvailability({
  authMode,
  authIdleTimeoutMs,
  requestTimeoutMs,
  onMissingAuth,
  baseUrl = "/api",
}: UsePackCatalogAvailabilityOptions) {
  const [status, setStatus] = useState<PackCatalogAvailabilityStatus>("checking");
  const [emptyStateSummary, setEmptyStateSummary] = useState<PackAvailabilitySummary | null>(null);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      setStatus("checking");
      setEmptyStateSummary(null);

      try {
        const token = authMode === "none" ? null : await resolveAuthToken(authMode, authIdleTimeoutMs);
        if (authMode !== "none" && !token) {
          if (!cancelled) {
            setStatus("error");
            setEmptyStateSummary(null);
          }
          onMissingAuth?.();
          return;
        }

        try {
          const summary = await fetchPackAvailabilitySummary(baseUrl, requestTimeoutMs, token);
          const hasAvailablePacks =
            summary.status === "available" || summary.available_pack_count > 0;

          if (!cancelled) {
            setEmptyStateSummary(hasAvailablePacks ? null : summary);
            setStatus(hasAvailablePacks ? "available" : "empty");
          }
          return;
        } catch {
          // Fall back to the client-side dependency verification path during rollout.
        }

        const { packs } = await fetchDependencyVerifiedPublicPacks(baseUrl, requestTimeoutMs, token);
        if (!cancelled) {
          setEmptyStateSummary(null);
          setStatus(packs.length === 0 ? "empty" : "available");
        }
      } catch {
        if (!cancelled) {
          setEmptyStateSummary(null);
          setStatus("error");
        }
      }
    };

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, [authIdleTimeoutMs, authMode, baseUrl, onMissingAuth, requestTimeoutMs]);

  return {
    status,
    isEmpty: status === "empty",
    isReady: status === "available",
    emptyStateSummary,
  };
}
