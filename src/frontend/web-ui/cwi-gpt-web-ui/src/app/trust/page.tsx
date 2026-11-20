"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { formatDateTime } from "@/lib/format";
import { resolveRuntimeConfig } from "@/lib/config";
import { findMatchingVerifiedPack, resolveCanonicalVerifiedPackSelection } from "@/lib/packs/reconcile";
import { loadSelectedPackName, saveSelectedPackName } from "@/lib/packs/selection";
import { loadOverrides } from "@/lib/storage";
import { useLiveVerifiedPacks } from "@/lib/packs/use-live-verified-packs";

export default function TrustPage() {
  const [selectedPackName, setSelectedPackName] = useState<string | null>(() => loadSelectedPackName());
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const router = useRouter();
  const runtime = resolveRuntimeConfig(loadOverrides());
  const handleMissingAuth = useCallback(() => {
    router.replace("/login");
  }, [router]);
  const {
    packs: liveVerifiedPacks,
    status: packListStatus,
    error: packListError,
    filteredOutPackCount,
    refresh,
  } = useLiveVerifiedPacks({
    authMode: runtime.config.authMode,
    authIdleTimeoutMs: runtime.config.authIdleTimeoutMs,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
    onMissingAuth: handleMissingAuth,
  });

  const pack = useMemo(
    () => findMatchingVerifiedPack(selectedPackName, liveVerifiedPacks),
    [liveVerifiedPacks, selectedPackName],
  );

  useEffect(() => {
    if (!selectedPackName || packListStatus !== "ready") {
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
        setSelectionError(`Selected pack "${removedPack}" is not in the live verified catalog. Choose another pack.`);
      });
    }
  }, [liveVerifiedPacks, pack, packListStatus, selectedPackName]);

  const handleSelectPack = (packName: string) => {
    if (packName === selectedPackName) {
      return;
    }
    setSelectedPackName(packName);
    saveSelectedPackName(packName);
    setSelectionError(null);
  };

  const selectedPackLabel = pack?.displayName ?? selectedPackName ?? "Not selected";

  return (
    <div className="page-stack">
      <div className="card-light">
        <h2 className="card-title">Pack trust</h2>
        <p className="card-subtitle">
          Packs below are fetched live and dependency-verified before they are shown. The GPT site no longer uses a cached browser pack list for this view.
        </p>
        <div className="meta-grid">
          <span className="meta-label">Gateway</span>
          <span className="meta-value">{runtime.config.gatewayBaseUrl}</span>
          <span className="meta-label">Pack</span>
          <span className="meta-value">{selectedPackLabel}</span>
        </div>

        <div className="pack-select">
          <div className="pack-select-header">
            <div>
              <div className="card-title-sm">Live verified packs</div>
              <p className="card-text-muted-xs-tight">
                Select a public pack with a fully resolvable dependency chain.
              </p>
            </div>
            <button
              className="btn-secondary-sm"
              type="button"
              onClick={() => void refresh()}
              disabled={packListStatus === "loading"}
            >
              {packListStatus === "loading" ? "Loading..." : "Refresh list"}
            </button>
          </div>
          {packListError ? (
            <div
              className="alert alert-error"
              role="alert"
            >
              {packListError}
            </div>
          ) : null}
          {selectionError ? (
            <div
              className="alert alert-error"
              role="alert"
            >
              {selectionError}
            </div>
          ) : null}
          {packListStatus === "loading" ? (
            <p className="card-text-muted-xs-tight">Loading live verified pack catalog...</p>
          ) : null}
          {packListStatus === "ready" && liveVerifiedPacks.length === 0 ? (
            <p className="card-text-muted-xs-tight">No live verified packs available.</p>
          ) : null}
          {packListStatus === "ready" && filteredOutPackCount > 0 ? (
            <p className="card-text-muted-xs-tight">
              Hidden packs: {filteredOutPackCount} failing live dependency or metadata validation.
            </p>
          ) : null}
          {liveVerifiedPacks.length > 0 ? (
            <div className="pack-list">
              {liveVerifiedPacks.map((packItem) => {
                const label = packItem.displayName?.trim() || packItem.name;
                const isSelected = packItem.name === selectedPackName;
                return (
                  <button
                    key={packItem.name}
                    type="button"
                    className={isSelected ? "pack-option pack-option--active" : "pack-option"}
                    onClick={() => handleSelectPack(packItem.name)}
                  >
                    <div className="pack-option-title">{label}</div>
                    <div className="pack-option-meta">Latest: {packItem.version || "unknown"}</div>
                    <div className="pack-option-id">{packItem.name}</div>
                  </button>
                );
              })}
            </div>
          ) : null}
          <div className="pack-selected">
            Selected pack: <span className="pack-selected-name">{selectedPackLabel}</span>
          </div>
        </div>

        <div className="panel-actions-spacious">
          <button
            className="btn-primary"
            type="button"
            onClick={() => void refresh()}
            disabled={packListStatus === "loading"}
          >
            {packListStatus === "loading" ? "Refreshing..." : "Reload live catalog"}
          </button>
          <Link
            className="btn-secondary"
            href="/settings"
          >
            Update settings
          </Link>
        </div>
      </div>

      <div className="card-light">
        <h3 className="card-title-sm">Verification status</h3>
        {pack ? (
          <div className="status-grid-wide">
            <div className="status-item">
              <span className="meta-label">Status</span>
              <span className="status-verified">Live verified</span>
            </div>

            <div className="status-item">
              <span className="meta-label">Pack version</span>
              <span className="meta-value">{pack.version}</span>
            </div>
            <div className="status-item">
              <span className="meta-label">Dependencies</span>
              <span className="meta-value">
                {pack.verifiedDependencies.length === 0
                  ? "None"
                  : `${pack.verifiedDependencies.length} verified`}
              </span>
            </div>
            <div className="status-item">
              <span className="meta-label">Pack hash</span>
              <span className="meta-value-break">{pack.hash}</span>
            </div>

            <div className="status-item">
              <span className="meta-label">Validated at</span>
              <span className="meta-value">{formatDateTime(pack.verifiedAt)}</span>
            </div>
          </div>
        ) : (
          <p className="card-text-muted-sm">
            No live verified pack selected yet. Choose a pack above to use it in ask and action flows.
          </p>
        )}
      </div>
    </div>
  );
}
