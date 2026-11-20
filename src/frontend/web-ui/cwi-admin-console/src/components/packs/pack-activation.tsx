"use client";

import { useState } from "react";
import { activatePack, rollbackPack } from "@/lib/packs/activation-client";
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

export default function PackActivationPanel() {
  const { accessToken } = useAuth();
  const [tenantId, setTenantId] = useState("");
  const [packName, setPackName] = useState("");
  const [packHash, setPackHash] = useState("");
  const [actorUserId, setActorUserId] = useState("");
  const [reason, setReason] = useState("");
  const [breakGlassToken, setBreakGlassToken] = useState("");
  const [activateConfirm, setActivateConfirm] = useState("");
  const [rollbackConfirm, setRollbackConfirm] = useState("");
  const [activationResult, setActivationResult] = useState<ActionResult | null>(null);
  const [rollbackResult, setRollbackResult] = useState<ActionResult | null>(null);
  const [loading, setLoading] = useState(false);

  const normalizedPackName = packName.trim();
  const activateConfirmValid =
    normalizedPackName.length > 0 && activateConfirm.trim() === normalizedPackName;
  const rollbackConfirmTarget = normalizedPackName ? `ROLLBACK ${normalizedPackName}` : "ROLLBACK";
  const rollbackConfirmValid =
    normalizedPackName.length > 0 && rollbackConfirm.trim() === rollbackConfirmTarget;

  const handleActivate = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setActivationResult(null);
    const response = await activatePack(
      { tenantId, packName, packHash, actorUserId },
      accessToken,
      breakGlassToken || null,
    );
    setActivationResult({
      ok: response.ok,
      status: response.status,
      data: response.data,
      error: response.error,
    });
    setLoading(false);
  };

  const handleRollback = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setRollbackResult(null);
    const response = await rollbackPack(
      { tenantId, packName, actorUserId, reason },
      accessToken,
      breakGlassToken || null,
    );
    setRollbackResult({
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
          <h2 className="text-lg font-semibold text-white">Pack activation & rollback</h2>
          <p className="text-sm text-slate-300">
            Promote a pack hash into active status or rollback for a tenant.
          </p>
        </div>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          admin only
        </span>
      </header>

      <div className="mt-6 grid gap-6 lg:grid-cols-2">
        <form className="space-y-4" onSubmit={handleActivate}>
          <h3 className="text-sm font-semibold text-white">Activate</h3>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack name
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packName}
              onChange={(e) => setPackName(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack anchor hash
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packHash}
              onChange={(e) => setPackHash(e.target.value)}
              required
            />
          </label>
          <div className="rounded-2xl border border-sky-500/40 bg-sky-500/10 p-3 text-xs text-sky-100">
            Activation makes the specified pack hash live for the tenant. Double-check the hash before proceeding.
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
            Confirm pack name
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={activateConfirm}
              onChange={(e) => setActivateConfirm(e.target.value)}
              placeholder={normalizedPackName || "pack-name"}
              required
            />
            {!activateConfirmValid && activateConfirm.length > 0 && (
              <span className="mt-2 block text-xs text-sky-200">
                Confirmation does not match the pack name.
              </span>
            )}
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              type="password"
            />
          </label>
          <button
            className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            type="submit"
            disabled={loading || !activateConfirmValid}
          >
            {loading ? "Submitting..." : "Activate pack"}
          </button>
          <ResultPanel result={activationResult} />
        </form>

        <form className="space-y-4" onSubmit={handleRollback}>
          <h3 className="text-sm font-semibold text-white">Rollback</h3>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack name
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packName}
              onChange={(e) => setPackName(e.target.value)}
              required
            />
          </label>
          <div className="rounded-2xl border border-rose-500/40 bg-rose-500/10 p-3 text-xs text-rose-100">
            Rollback disables the active pack for this tenant. This is a destructive operation.
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
            Rollback reason
            <textarea
              className="mt-2 min-h-[96px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Type "{rollbackConfirmTarget}" to confirm
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={rollbackConfirm}
              onChange={(e) => setRollbackConfirm(e.target.value)}
              placeholder={rollbackConfirmTarget}
              required
            />
            {!rollbackConfirmValid && rollbackConfirm.length > 0 && (
              <span className="mt-2 block text-xs text-rose-200">
                Confirmation phrase does not match.
              </span>
            )}
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              type="password"
            />
          </label>
          <button
            className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            type="submit"
            disabled={loading || !rollbackConfirmValid}
          >
            {loading ? "Submitting..." : "Rollback pack"}
          </button>
          <ResultPanel result={rollbackResult} />
        </form>
      </div>
    </section>
  );
}
