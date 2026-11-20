"use client";

import { useEffect, useMemo, useState } from "react";
import { getRuntimeConfigStatus } from "@/lib/config";
import { fetchPackAnchor, fetchPackDocument, fetchPackIndex } from "@/lib/packs/client";
import type { PackDocument, PackIndexEntry } from "@/lib/packs/types";

export type SelectedPack = {
  packId: string;
  version?: string;
  packHash?: string;
  packVersion?: string;
};

type PackListProps = {
  onSelect: (selection: SelectedPack) => void;
};

export default function PackList({ onSelect }: PackListProps) {
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const packsBaseUrl = config.config?.packsBaseUrl;
  const [packs, setPacks] = useState<PackIndexEntry[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [packDoc, setPackDoc] = useState<PackDocument | null>(null);
  const [anchor, setAnchor] = useState<string | null>(null);
  const [version, setVersion] = useState<string | null>(null);
  const [filter, setFilter] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!packsBaseUrl) {
      return;
    }
    const baseUrl = packsBaseUrl;
    let cancelled = false;
    async function load() {
      setIsLoading(true);
      setError(null);
      try {
        const index = await fetchPackIndex(baseUrl);
        if (!cancelled) {
          setPacks(index);
          setSelected((current) => current ?? index[0]?.pack_id ?? null);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load packs.");
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, [packsBaseUrl]);

  useEffect(() => {
    if (!packsBaseUrl || !selected) {
      return;
    }
    const baseUrl = packsBaseUrl;
    const selectedId = selected;
    const requestedVersion = version ?? undefined;
    let cancelled = false;
    async function loadDetail() {
      setIsLoading(true);
      setError(null);
      try {
        const doc = await fetchPackDocument(baseUrl, selectedId, requestedVersion);
        const anchorHash = await fetchPackAnchor(baseUrl, selectedId, requestedVersion);
        if (!cancelled) {
          setPackDoc(doc);
          setAnchor(anchorHash);
          onSelect({
            packId: selectedId,
            version: requestedVersion ?? doc.pack_version,
            packHash: anchorHash,
            packVersion: doc.pack_version,
          });
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load pack details.");
          setPackDoc(null);
          setAnchor(null);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }
    void loadDetail();
    return () => {
      cancelled = true;
    };
  }, [packsBaseUrl, onSelect, selected, version]);

  const filtered = packs.filter((pack) =>
    `${pack.pack_id} ${pack.name ?? ""}`.toLowerCase().includes(filter.toLowerCase()),
  );

  if (!packsBaseUrl) {
    return (
      <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
        <p className="text-sm font-semibold text-slate-800">Pack catalog</p>
        <p className="mt-2 text-xs text-slate-600">Missing pack service configuration.</p>
      </div>
    );
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Pack catalog</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Packs</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Browse available policy packs and inspect anchors, versions, and capabilities.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Filter packs"
          className="flex-1 rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <button
          className="rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700"
          onClick={() => setFilter("")}
        >
          Reset
        </button>
      </div>
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
      <div className="mt-4 grid gap-4 lg:grid-cols-[1fr_1.2fr]">
        <div className="space-y-2">
          {filtered.map((pack) => {
            const isActive = pack.pack_id === selected;
            return (
              <button
                key={pack.pack_id}
                onClick={() => {
                  setSelected(pack.pack_id);
                  setVersion(pack.latest ?? pack.versions?.[0] ?? null);
                }}
                className={`w-full rounded-2xl border px-4 py-3 text-left text-sm transition ${
                  isActive
                    ? "border-slate-900 bg-slate-900 text-white"
                    : "border-slate-200 bg-white text-slate-700 hover:border-slate-400"
                }`}
              >
                <p className="font-semibold">{pack.name ?? pack.pack_id}</p>
                <p className="text-xs opacity-70">
                  {pack.description ?? "No description provided."}
                </p>
                {pack.latest ? (
                  <p className="mt-2 text-[11px] uppercase tracking-[0.2em] opacity-70">
                    Latest: {pack.latest}
                  </p>
                ) : null}
              </button>
            );
          })}
          {filtered.length === 0 && !isLoading ? (
            <p className="text-xs text-slate-500">No packs match this filter.</p>
          ) : null}
        </div>
        <div className="rounded-2xl border border-slate-200 bg-white p-4">
          {isLoading && !packDoc ? (
            <p className="text-xs text-slate-500">Loading pack...</p>
          ) : null}
          {packDoc ? (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Pack</p>
                  <p className="text-lg font-semibold text-slate-900">{packDoc.name}</p>
                  <p className="text-xs text-slate-500">{packDoc.pack_id}</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs">
                  <p className="uppercase tracking-[0.2em] text-slate-500">Version</p>
                  <p className="font-semibold text-slate-800">{packDoc.pack_version}</p>
                </div>
              </div>
              {packDoc.description ? (
                <p className="text-sm text-slate-600">{packDoc.description}</p>
              ) : null}
              <div className="flex flex-wrap items-center gap-3 text-xs text-slate-600">
                <span className="rounded-full bg-slate-100 px-3 py-1">
                  Modules: {packDoc.modules?.length ?? 0}
                </span>
                <span className="rounded-full bg-slate-100 px-3 py-1">
                  Dependencies: {packDoc.dependencies?.packs?.length ?? 0}
                </span>
                {anchor ? (
                  <span className="rounded-full bg-slate-100 px-3 py-1">
                    Anchor: {anchor.slice(0, 12)}...
                  </span>
                ) : null}
              </div>
              {(packDoc.dependencies?.packs?.length ?? 0) > 0 ? (
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Dependencies</p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {(packDoc.dependencies?.packs ?? []).map((dep) => (
                      <span
                        key={`${dep.pack_id}-${dep.pack_version ?? "latest"}`}
                        className="rounded-full bg-slate-900/90 px-3 py-1 text-[11px] text-white"
                      >
                        {dep.pack_id}
                        {dep.pack_version ? `@${dep.pack_version}` : ""}
                      </span>
                    ))}
                  </div>
                </div>
              ) : null}
              {packDoc.metadata ? (
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Capabilities</p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {Object.keys(packDoc.metadata)
                      .filter((key) => key.toLowerCase().includes("capability"))
                      .slice(0, 6)
                      .map((key) => (
                        <span
                          key={key}
                          className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[11px] text-amber-700"
                        >
                          {key}
                        </span>
                      ))}
                    {Object.keys(packDoc.metadata).length === 0 ? (
                      <span className="text-xs text-slate-400">No capability metadata.</span>
                    ) : null}
                  </div>
                </div>
              ) : null}
              <div>
                <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Version history</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {(packs.find((pack) => pack.pack_id === selected)?.versions ?? [
                    packDoc.pack_version,
                  ]).map((ver) => (
                    <button
                      key={ver}
                      className={`rounded-full px-3 py-1 text-[11px] ${
                        ver === (version ?? packDoc.pack_version)
                          ? "bg-slate-900 text-white"
                          : "border border-slate-200 bg-white text-slate-700"
                      }`}
                      onClick={() => setVersion(ver)}
                    >
                      {ver}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          ) : (
            <p className="text-xs text-slate-500">Select a pack to view details.</p>
          )}
        </div>
      </div>
    </div>
  );
}
