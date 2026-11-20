"use client";

import { useState } from "react";
import { deletePack, retirePack } from "@/lib/packs/lifecycle-client";
import { useAuth } from "@/lib/auth/context";

type ActionResult = {
  ok: boolean;
  status: number;
  data?: unknown;
  error?: string;
};

function ResultPanel({ result }: { result: ActionResult | null }) {
  if (!result) {
    return null;
  }

  const className = result.ok
    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
    : "border-amber-500/40 bg-amber-500/10 text-amber-100";

  return (
    <div className={`mt-4 rounded-2xl border p-4 text-sm ${className}`}>
      <p className="font-semibold">
        {result.ok ? "Request succeeded" : "Request failed"} (HTTP {result.status || "n/a"})
      </p>
      {result.error && <p className="mt-2 text-xs">{result.error}</p>}
      {result.data != null && (
        <pre className="mt-3 max-h-64 overflow-auto rounded-xl border border-white/10 bg-black/30 p-3 text-xs text-slate-100">
{JSON.stringify(result.data, null, 2)}
        </pre>
      )}
    </div>
  );
}

export default function PackRetireDeletePanel() {
  const { accessToken } = useAuth();
  const [tenantId, setTenantId] = useState("");
  const [packId, setPackId] = useState("");
  const [packVersion, setPackVersion] = useState("");
  const [actorUserId, setActorUserId] = useState("");
  const [reason, setReason] = useState("");
  const [breakGlassToken, setBreakGlassToken] = useState("");
  const [retireConfirm, setRetireConfirm] = useState("");
  const [deleteConfirm, setDeleteConfirm] = useState("");
  const [retireResult, setRetireResult] = useState<ActionResult | null>(null);
  const [deleteResult, setDeleteResult] = useState<ActionResult | null>(null);
  const [loading, setLoading] = useState(false);

  const normalizedPackId = packId.trim();
  const retireConfirmValid =
    normalizedPackId.length > 0 && retireConfirm.trim() === normalizedPackId;
  const deleteConfirmTarget = normalizedPackId ? `DELETE ${normalizedPackId}` : "DELETE";
  const deleteConfirmValid =
    normalizedPackId.length > 0 && deleteConfirm.trim() === deleteConfirmTarget;

  const handleRetire = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setRetireResult(null);
    const response = await retirePack(
      {
        tenantId: tenantId.trim() || undefined,
        packId: packId.trim(),
        actorUserId: actorUserId.trim(),
        reason: reason.trim() || undefined,
      },
      accessToken,
      breakGlassToken || null,
    );
    setRetireResult({
      ok: response.ok,
      status: response.status,
      data: response.data,
      error: response.error,
    });
    setLoading(false);
  };

  const handleDelete = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setDeleteResult(null);
    const response = await deletePack(
      {
        tenantId: tenantId.trim() || undefined,
        packId: packId.trim(),
        packVersion: packVersion.trim() || undefined,
        actorUserId: actorUserId.trim(),
        reason: reason.trim() || undefined,
      },
      accessToken,
      breakGlassToken || null,
    );
    setDeleteResult({
      ok: response.ok,
      status: response.status,
      data: response.data,
      error: response.error,
    });
    setLoading(false);
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Pack retire & delete</h2>
          <p className="text-sm text-slate-300">
            Retire a pack from the catalog or delete a version entirely.
          </p>
        </div>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          destructive
        </span>
      </header>

      <div className="mt-6 grid gap-6 lg:grid-cols-2">
        <form className="space-y-4" onSubmit={handleRetire}>
          <h3 className="text-sm font-semibold text-white">Retire pack (soft hide)</h3>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="tenant-1"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packId}
              onChange={(e) => setPackId(e.target.value)}
              required
            />
          </label>
          <div className="rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-100">
            Retiring hides the pack from the catalog but leaves files intact. You can still delete later.
          </div>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Actor user ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={actorUserId}
              onChange={(e) => setActorUserId(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Reason (optional)
            <textarea
              className="mt-2 min-h-[96px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Why are we retiring this pack?"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Confirm pack ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={retireConfirm}
              onChange={(e) => setRetireConfirm(e.target.value)}
              placeholder={normalizedPackId || "pack-id"}
              required
            />
            {!retireConfirmValid && retireConfirm.length > 0 && (
              <span className="mt-2 block text-xs text-amber-200">
                Confirmation does not match the pack ID.
              </span>
            )}
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              type="password"
              required
            />
          </label>
          <button
            className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            type="submit"
            disabled={loading || !retireConfirmValid}
          >
            {loading ? "Submitting..." : "Retire pack"}
          </button>
          <ResultPanel result={retireResult} />
        </form>

        <form className="space-y-4" onSubmit={handleDelete}>
          <h3 className="text-sm font-semibold text-white">Delete pack (hard)</h3>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="tenant-1"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packId}
              onChange={(e) => setPackId(e.target.value)}
              required
            />
          </label>
          <div className="rounded-2xl border border-rose-500/40 bg-rose-500/10 p-3 text-xs text-rose-100">
            Hard delete removes pack files and index entries. This action cannot be undone.
          </div>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack version (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packVersion}
              onChange={(e) => setPackVersion(e.target.value)}
              placeholder="0.0.1"
            />
            <span className="mt-2 block text-xs text-slate-400">
              Leave empty to delete the entire pack and all versions.
            </span>
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Actor user ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={actorUserId}
              onChange={(e) => setActorUserId(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Reason (optional)
            <textarea
              className="mt-2 min-h-[96px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Why are we deleting this pack?"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Type "{deleteConfirmTarget}" to confirm
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={deleteConfirm}
              onChange={(e) => setDeleteConfirm(e.target.value)}
              placeholder={deleteConfirmTarget}
              required
            />
            {!deleteConfirmValid && deleteConfirm.length > 0 && (
              <span className="mt-2 block text-xs text-rose-200">
                Confirmation phrase does not match.
              </span>
            )}
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              type="password"
              required
            />
          </label>
          <button
            className="rounded-full bg-rose-200 px-5 py-2 text-sm font-semibold text-rose-950"
            type="submit"
            disabled={loading || !deleteConfirmValid}
          >
            {loading ? "Submitting..." : "Delete pack"}
          </button>
          <ResultPanel result={deleteResult} />
        </form>
      </div>
    </section>
  );
}
