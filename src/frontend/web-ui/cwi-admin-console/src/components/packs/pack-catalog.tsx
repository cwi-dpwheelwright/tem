"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getPackAnchor, getPackDetails, getPackIndex, type PackCatalogItem } from "@/lib/packs/client";

export default function PackCatalog() {
  const [items, setItems] = useState<PackCatalogItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const loadIndex = useCallback(async () => {
    setLoading(true);
    const response = await getPackIndex();
    if (response.ok && response.data) {
      const nextItems = response.data.packs.map((entry) => ({ entry }));
      setItems(nextItems);
      setError(null);
      if (nextItems.length > 0) {
        setSelectedId((current) => current ?? nextItems[0].entry.pack_id);
      }
    } else {
      setError(response.error || "Failed to load packs index.");
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    void loadIndex();
  }, [loadIndex]);

  const filtered = useMemo(() => {
    if (!query.trim()) {
      return items;
    }
    const needle = query.trim().toLowerCase();
    return items.filter((item) => {
      const name = item.entry.name ?? "";
      return (
        item.entry.pack_id.toLowerCase().includes(needle) ||
        name.toLowerCase().includes(needle)
      );
    });
  }, [items, query]);

  const selected = items.find((item) => item.entry.pack_id === selectedId) ?? null;

  const loadDetails = useCallback(
    async (packId: string) => {
      const [detailsResponse, anchorResponse] = await Promise.all([
        getPackDetails(packId),
        getPackAnchor(packId),
      ]);

      setItems((prev) =>
        prev.map((item) => {
          if (item.entry.pack_id !== packId) {
            return item;
          }
          return {
            ...item,
            details: detailsResponse.ok ? detailsResponse.data : item.details,
            anchor: anchorResponse.ok ? anchorResponse.data?.trim() : item.anchor,
          };
        }),
      );
    },
    [],
  );

  useEffect(() => {
    if (selectedId) {
      void loadDetails(selectedId);
    }
  }, [selectedId, loadDetails]);

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Pack catalog</h2>
          <p className="text-sm text-slate-300">Read-only view of published packs and versions.</p>
        </div>
        <button
          className="rounded-full border border-white/10 bg-white/10 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200"
          type="button"
          onClick={() => void loadIndex()}
        >
          Refresh
        </button>
      </header>

      <div className="mt-6 grid gap-6 lg:grid-cols-[1.1fr_1.4fr]">
        <div className="space-y-4">
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Search packs
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search by pack ID or name"
            />
          </label>

          {error && (
            <div className="rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
              {error}
            </div>
          )}

          {loading ? (
            <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
              Loading pack index...
            </div>
          ) : filtered.length === 0 ? (
            <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
              No packs found.
            </div>
          ) : (
            <div className="space-y-2">
              {filtered.map((item) => {
                const isActive = item.entry.pack_id === selectedId;
                return (
                  <button
                    key={item.entry.pack_id}
                    className={`w-full rounded-2xl border px-4 py-3 text-left text-sm transition ${
                      isActive
                        ? "border-sky-400/60 bg-sky-400/10 text-white"
                        : "border-white/10 bg-white/5 text-slate-200 hover:border-sky-400/40"
                    }`}
                    type="button"
                    onClick={() => setSelectedId(item.entry.pack_id)}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <span className="font-semibold text-white">{item.entry.pack_id}</span>
                      <span className="text-xs uppercase tracking-[0.2em] text-slate-400">
                        {item.entry.visibility ?? "unknown"}
                      </span>
                    </div>
                    <div className="mt-2 text-xs text-slate-400">
                      {item.entry.name ?? "Unnamed pack"}
                    </div>
                    {item.entry.latest && (
                      <div className="mt-2 text-xs text-slate-300">
                        Latest: {item.entry.latest}
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          )}
        </div>

        <div className="rounded-2xl border border-white/10 bg-white/5 p-5 text-sm text-slate-200">
          {selected ? (
            <div className="space-y-4">
              <div>
                <h3 className="text-xl font-semibold text-white">{selected.entry.name ?? selected.entry.pack_id}</h3>
                <p className="text-xs uppercase tracking-[0.2em] text-slate-400">{selected.entry.pack_id}</p>
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Visibility</p>
                  <p className="mt-1 text-sm">{selected.entry.visibility ?? "unknown"}</p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Latest version</p>
                  <p className="mt-1 text-sm">{selected.entry.latest ?? selected.details?.pack_version ?? "N/A"}</p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Anchor hash</p>
                  <p className="mt-1 break-all text-xs text-slate-300">
                    {selected.anchor ?? "N/A"}
                  </p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Modules</p>
                  <p className="mt-1 text-sm">
                    {selected.details?.modules ? selected.details.modules.length : "N/A"}
                  </p>
                </div>
              </div>

              <div>
                <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Versions</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {(selected.entry.versions ?? []).length > 0 ? (
                    selected.entry.versions?.map((version) => (
                      <span
                        key={version}
                        className="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs text-slate-200"
                      >
                        {version}
                      </span>
                    ))
                  ) : (
                    <span className="text-xs text-slate-400">No versions listed.</span>
                  )}
                </div>
              </div>
            </div>
          ) : (
            <p className="text-sm text-slate-300">Select a pack to view details.</p>
          )}
        </div>
      </div>
    </section>
  );
}
