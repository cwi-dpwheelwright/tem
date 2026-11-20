/* eslint-disable react-hooks/set-state-in-effect */
"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams } from "next/navigation";
import AppShell from "@/components/app-shell";
import { ApiError, createApiClient } from "@/lib/api/client";
import type { DocumentDetail, DocumentRollbackRequest } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";
import { resolveUserId } from "@/lib/auth/user-id";
import { getRuntimeConfigStatus } from "@/lib/config";
import { buildLineDiff } from "@/lib/documents/diff";
import { useTenant } from "@/lib/tenant/context";
import { useTelemetry } from "@/lib/telemetry/context";

export default function DocumentDetailPage() {
  const params = useParams();
  const documentId = typeof params.documentId === "string" ? params.documentId : "";
  const { accessToken, user } = useAuth();
  const { tenantId } = useTenant();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const configError = !config.config?.apiBaseUrl ? "Missing API base URL configuration." : null;
  const [document, setDocument] = useState<DocumentDetail | null>(null);
  const [leftVersion, setLeftVersion] = useState<string | null>(null);
  const [rightVersion, setRightVersion] = useState<string | null>(null);

  const effectiveLeftVersion = useMemo(() => {
    const versions = document?.versions ?? [];
    const available = new Set(versions.map((item) => item.version));
    if (leftVersion && available.has(leftVersion)) {
      return leftVersion;
    }
    if (versions.length >= 2) {
      return versions[1]?.version ?? null;
    }
    return versions[0]?.version ?? null;
  }, [document?.versions, leftVersion]);

  const effectiveRightVersion = useMemo(() => {
    const versions = document?.versions ?? [];
    const available = new Set(versions.map((item) => item.version));
    if (rightVersion && available.has(rightVersion)) {
      return rightVersion;
    }
    return versions[0]?.version ?? null;
  }, [document?.versions, rightVersion]);
  const [rollbackReason, setRollbackReason] = useState("");
  const [rollbackNotice, setRollbackNotice] = useState<string | null>(null);
  const [rollbackError, setRollbackError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const { trackEvent } = useTelemetry();

  useEffect(() => {
    if (documentId) {
      trackEvent("document_view", "documents", { documentId });
    }
  }, [documentId, trackEvent]);

  useEffect(() => {
    if (!documentId) {
      return;
    }
    if (configError) {
      return;
    }

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
    });

    setIsLoading(true);
    setError(null);
    setNotFound(false);
    client
      .documentDetail(documentId, tenantId)
      .then((data) => setDocument(data))
      .catch((err) => {
        if (err instanceof ApiError && err.status === 404) {
          setNotFound(true);
        } else {
          setError(err instanceof Error ? err.message : "Unable to load document.");
        }
      })
      .finally(() => setIsLoading(false));
  }, [accessToken, config.config?.apiBaseUrl, configError, documentId, tenantId]);

  async function handleRollback(targetVersion: string) {
    if (!document) {
      return;
    }
    if (!config.config?.apiBaseUrl) {
      setRollbackError("Missing API base URL configuration.");
      return;
    }

    const payload: DocumentRollbackRequest = {
      tenant_id: tenantId,
      actor_id: resolveUserId(user),
      target_version: targetVersion,
      reason: rollbackReason.trim(),
    };

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
    });

    setRollbackError(null);
    try {
      const response = await client.documentRollback(document.doc_id, payload);
      setRollbackNotice(response.message ?? `Rollback request staged for ${response.target_version}.`);
      trackEvent("document_rollback", "documents", {
        documentId: document.doc_id,
        version: targetVersion,
      });
    } catch (err) {
      setRollbackError(err instanceof Error ? err.message : "Rollback request failed.");
    }
  }

  const effectiveLoading = configError ? false : isLoading;
  const effectiveError = configError ?? error;

  if (effectiveLoading) {
    return (
      <AppShell title="Loading document" subtitle="Fetching document detail." badge="Documents">
        <div className="rounded-3xl border border-dashed border-slate-200 bg-white px-6 py-8 text-sm text-slate-500">
          Loading document...
        </div>
      </AppShell>
    );
  }

  if (notFound || !document) {
    return (
      <AppShell title="Document not found" subtitle="This document ID does not exist." badge="Documents">
        <div className="rounded-3xl border border-dashed border-slate-200 bg-white px-6 py-8 text-sm text-slate-500">
          {effectiveError ?? "Unknown document ID. Return to the document catalog to select a valid entry."}
        </div>
      </AppShell>
    );
  }

  const left = document.versions.find((ver) => ver.version === leftVersion) ?? document.versions[0];
  const right = document.versions.find((ver) => ver.version === rightVersion) ?? document.versions[0];
  const diff = buildLineDiff(left.content, right.content);

  return (
    <AppShell
      title={document.title}
      subtitle="Version history, diff comparison, and rollback controls."
      badge="Documents"
    >
      <div className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
        <section className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Document ID</p>
                <p className="text-lg font-semibold text-slate-900">{document.doc_id}</p>
                <p className="text-xs text-slate-500">Owner: {document.owner}</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-600">
                {document.type}
              </span>
            </div>
            <p className="mt-4 text-sm text-slate-600">
              Latest version {document.versions[0]?.version} authored by {document.versions[0]?.author}.
            </p>
            <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-4">
              <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Current content</p>
              <pre className="mt-3 whitespace-pre-wrap text-sm text-slate-700">
                {document.versions[0]?.content}
              </pre>
            </div>
          </div>

          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-sm font-semibold text-slate-800">Version diff compare</p>
            <p className="mt-1 text-xs text-slate-600">
              Select two versions to compare before issuing a rollback.
            </p>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                Compare from
                <select
                  className="mt-2 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800"
                  value={effectiveLeftVersion ?? ""}
                  onChange={(event) => {
                    setLeftVersion(event.target.value);
                    trackEvent("doc_compare_select", "documents", { side: "left" });
                  }}
                >
                  {document.versions.map((ver) => (
                    <option key={ver.version} value={ver.version}>
                      {ver.version} ({new Date(ver.created_utc).toLocaleDateString()})
                    </option>
                  ))}
                </select>
              </label>
              <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                Compare to
                <select
                  className="mt-2 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800"
                  value={effectiveRightVersion ?? ""}
                  onChange={(event) => {
                    setRightVersion(event.target.value);
                    trackEvent("doc_compare_select", "documents", { side: "right" });
                  }}
                >
                  {document.versions.map((ver) => (
                    <option key={ver.version} value={ver.version}>
                      {ver.version} ({new Date(ver.created_utc).toLocaleDateString()})
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4">
              <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Diff preview</p>
              <div className="mt-3 space-y-2 text-sm">
                {diff.map((line, index) => (
                  <div
                    key={`${line.type}-${index}`}
                    className={`rounded-xl px-3 py-2 ${
                      line.type === "same"
                        ? "bg-slate-50 text-slate-600"
                        : line.type === "added"
                        ? "bg-emerald-50 text-emerald-700"
                        : line.type === "removed"
                        ? "bg-rose-50 text-rose-700"
                        : "bg-amber-50 text-amber-700"
                    }`}
                  >
                    <p className="text-[11px] uppercase tracking-[0.2em]">{line.type}</p>
                    <p className="mt-1 whitespace-pre-wrap">{line.right ?? line.left ?? ""}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>

        <aside className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-sm font-semibold text-slate-800">Version history</p>
            <ul className="mt-3 space-y-3 text-sm text-slate-600">
              {document.versions.map((ver) => (
                <li key={ver.version} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs uppercase tracking-[0.2em] text-slate-500">
                      {ver.version}
                    </span>
                    <span className="text-xs text-slate-500">
                      {new Date(ver.created_utc).toLocaleString()}
                    </span>
                  </div>
                  <p className="mt-2 text-sm text-slate-800">{ver.notes}</p>
                  <p className="mt-2 text-xs text-slate-500">Checksum {ver.checksum}</p>
                </li>
              ))}
            </ul>
          </div>

          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <p className="text-sm font-semibold text-slate-800">Rollback controls</p>
            <p className="mt-1 text-xs text-slate-600">
              Stage a rollback to the selected version after confirming differences.
            </p>
            <div className="mt-4 rounded-2xl border border-slate-200 bg-white p-4">
              <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Target version</p>
              <p className="mt-2 text-lg font-semibold text-slate-900">{left.version}</p>
              <textarea
                value={rollbackReason}
                onChange={(event) => setRollbackReason(event.target.value)}
                placeholder="Rollback reason"
                rows={3}
                className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
              />
              <button
                className="mt-3 w-full rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
                disabled={!rollbackReason.trim()}
                onClick={() => handleRollback(left.version)}
              >
                Stage rollback
              </button>
              {rollbackNotice ? (
                <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
                  {rollbackNotice}
                </div>
              ) : null}
              {rollbackError ? (
                <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700">
                  {rollbackError}
                </div>
              ) : null}
            </div>
          </div>
        </aside>
      </div>
    </AppShell>
  );
}
