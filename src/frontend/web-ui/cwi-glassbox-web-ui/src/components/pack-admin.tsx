"use client";

import { useEffect, useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { useBreakGlass } from "@/lib/break-glass/context";
import { useTelemetry } from "@/lib/telemetry/context";
import type { SelectedPack } from "@/components/pack-list";

type PackAdminProps = {
  selection: SelectedPack | null;
};

export default function PackAdmin({ selection }: PackAdminProps) {
  const { accessToken, user, hasRole } = useAuth();
  const breakGlass = useBreakGlass();
  const { trackEvent } = useTelemetry();
  const config = useMemo(() => getRuntimeConfigStatus(), []);

  const [tenantId, setTenantId] = useState("");
  const [packName, setPackName] = useState(selection?.packId ?? "");
  const [packHash, setPackHash] = useState(selection?.packHash ?? "");
  const [actorUserId, setActorUserId] = useState(
    user?.profile.preferred_username ?? user?.profile.email ?? "",
  );
  const [rollbackReason, setRollbackReason] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadPackName, setUploadPackName] = useState("");
  const [uploadPackVersion, setUploadPackVersion] = useState("");
  const [uploadNotes, setUploadNotes] = useState("");
  const [uploadDryRun, setUploadDryRun] = useState(true);
  const [uploadStatus, setUploadStatus] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [isUploading, setIsUploading] = useState(false);

  useEffect(() => {
    if (selection?.packId) {
      setPackName(selection.packId);
      setUploadPackName(selection.packId);
    }
    if (selection?.packHash) {
      setPackHash(selection.packHash);
    }
    if (selection?.packVersion) {
      setUploadPackVersion(selection.packVersion);
    }
  }, [selection]);

  const canAdmin = hasRole("admin");
  const breakGlassReady = breakGlass.isActive;

  const disabled =
    !config.config ||
    !canAdmin ||
    !breakGlassReady ||
    !tenantId.trim() ||
    !packName.trim() ||
    !actorUserId.trim() ||
    isLoading;

  const activationDisabled = disabled || !packHash.trim();
  const rollbackDisabled = disabled || !rollbackReason.trim();

  async function activatePack() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    setIsLoading(true);
    setStatus(null);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      breakGlassTokenProvider: () => breakGlass.state?.tokenId,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": "admin",
      }),
    });

    try {
      const response = await client.activatePack({
        tenant_id: tenantId.trim(),
        pack_name: packName.trim(),
        pack_hash: packHash.trim(),
        actor_user_id: actorUserId.trim(),
      });
      setStatus(`Activated ${response.pack_name} (${response.pack_hash.slice(0, 12)}...)`);
      setLastUpdated(response.updated_utc);
      trackEvent("pack_activate", "packs", { pack: response.pack_name });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Activation failed.");
    } finally {
      setIsLoading(false);
    }
  }

  async function rollbackPack() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    setIsLoading(true);
    setStatus(null);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      breakGlassTokenProvider: () => breakGlass.state?.tokenId,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": "admin",
      }),
    });

    try {
      const response = await client.rollbackPack({
        tenant_id: tenantId.trim(),
        pack_name: packName.trim(),
        actor_user_id: actorUserId.trim(),
        reason: rollbackReason.trim(),
      });
      setStatus(`Rolled back ${response.pack_name}`);
      setLastUpdated(response.updated_utc);
      trackEvent("pack_rollback", "packs", { pack: response.pack_name });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Rollback failed.");
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Pack activation control</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Admin</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Activate or roll back a pack set. Break-glass admin scope required.
      </p>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={actorUserId}
          onChange={(event) => setActorUserId(event.target.value)}
          placeholder="actor_user_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={packName}
          onChange={(event) => setPackName(event.target.value)}
          placeholder="pack_name"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={packHash}
          onChange={(event) => setPackHash(event.target.value)}
          placeholder="pack_hash"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
      </div>
      <textarea
        value={rollbackReason}
        onChange={(event) => setRollbackReason(event.target.value)}
        placeholder="rollback reason"
        className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        rows={2}
      />
      <div className="mt-4 flex flex-wrap gap-3">
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          onClick={() => void activatePack()}
          disabled={activationDisabled}
        >
          {isLoading ? "Activating..." : "Activate pack"}
        </button>
        <button
          className="rounded-2xl border border-slate-300 bg-white px-4 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
          onClick={() => void rollbackPack()}
          disabled={rollbackDisabled}
        >
          {isLoading ? "Rolling back..." : "Rollback pack"}
        </button>
      </div>
      {!canAdmin ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          Admin role required for pack activation.
        </div>
      ) : null}
      {!breakGlassReady ? (
        <div className="mt-3 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-700">
          Break-glass token required to activate or rollback packs.
        </div>
      ) : null}
      {status ? (
        <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-700">
          {status}
        </div>
      ) : null}
      {lastUpdated ? (
        <div className="mt-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
          Last change recorded at {new Date(lastUpdated).toLocaleString()} by{" "}
          {actorUserId || "unknown"}.
        </div>
      ) : null}
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      {selection ? (
        <div className="mt-3 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs text-slate-600">
          Prefilled from pack catalog: {selection.packId}
          {selection.packVersion ? `@${selection.packVersion}` : ""}.
        </div>
      ) : null}
      <div className="mt-6 border-t border-slate-200/80 pt-6">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-semibold text-slate-800">Pack upload & publish</p>
            <p className="mt-1 text-xs text-slate-600">
              Upload a new pack bundle and publish to the registry (/api/ingest/skillpack).
            </p>
          </div>
          <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Ingest</span>
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <input
            value={uploadPackName}
            onChange={(event) => setUploadPackName(event.target.value)}
            placeholder="pack_name"
            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          />
          <input
            value={uploadPackVersion}
            onChange={(event) => setUploadPackVersion(event.target.value)}
            placeholder="pack_version"
            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          />
          <input
            type="file"
            accept=".zip,.json"
            onChange={(event) => setUploadFile(event.target.files?.[0] ?? null)}
            className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800"
          />
          <label className="flex items-center gap-2 text-xs font-semibold text-slate-600">
            <input
              type="checkbox"
              checked={uploadDryRun}
              onChange={(event) => setUploadDryRun(event.target.checked)}
            />
            Dry-run ingest
          </label>
        </div>
        <textarea
          value={uploadNotes}
          onChange={(event) => setUploadNotes(event.target.value)}
          placeholder="release notes"
          className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
          rows={2}
        />
        <div className="mt-4 flex flex-wrap gap-3">
          <button
            className="rounded-2xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-emerald-300"
            onClick={async () => {
              if (!config.config) {
                setUploadError("Missing API configuration.");
                return;
              }
              if (!uploadFile) {
                setUploadError("Select a pack bundle to upload.");
                return;
              }
              setIsUploading(true);
              setUploadError(null);
              setUploadStatus(null);
              try {
                const form = new FormData();
                form.append("file", uploadFile);
                form.append("pack_name", uploadPackName.trim());
                form.append("pack_version", uploadPackVersion.trim());
                form.append("notes", uploadNotes.trim());
                form.append("dry_run", uploadDryRun ? "true" : "false");

                const headers: Record<string, string> = {
                  "X-Tenant-Id": tenantId.trim(),
                  "X-Roles": "admin",
                };

                if (accessToken) {
                  headers.Authorization = `Bearer ${accessToken}`;
                }

                if (breakGlass.state?.tokenId) {
                  headers["X-Break-Glass-Token"] = breakGlass.state.tokenId;
                }

                const response = await fetch(
                  `${(config.config?.apiBaseUrl ?? "").replace(/\/$/, "")}/api/ingest/skillpack`,
                  {
                    method: "POST",
                    headers,
                    body: form,
                  },
                );

                if (!response.ok) {
                  throw new Error(`Upload failed: ${response.status}`);
                }

                setUploadStatus(
                  uploadDryRun
                    ? "Dry-run ingest completed."
                    : "Pack uploaded and queued for publish.",
                );
                trackEvent("pack_upload", "packs", {
                  pack: uploadPackName.trim(),
                  version: uploadPackVersion.trim(),
                  dryRun: uploadDryRun,
                });
              } catch (err) {
                setUploadError(err instanceof Error ? err.message : "Upload failed.");
              } finally {
                setIsUploading(false);
              }
            }}
            disabled={
              !uploadFile ||
              !uploadPackName.trim() ||
              !tenantId.trim() ||
              !canAdmin ||
              !breakGlassReady ||
              isUploading
            }
          >
            {isUploading ? "Uploading..." : "Upload pack"}
          </button>
        </div>
        {uploadStatus ? (
          <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-700">
            {uploadStatus}
          </div>
        ) : null}
        {uploadError ? (
          <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
            {uploadError}
          </div>
        ) : null}
      </div>
    </div>
  );
}
