"use client";

import { useMemo, useState } from "react";
import { ingestPack } from "@/lib/packs/ingest-client";
import { useAuth } from "@/lib/auth/context";

type UploadResult = {
  ok: boolean;
  status: number;
  body?: unknown;
  raw?: string;
  error?: string;
};

function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${kb.toFixed(1)} KB`;
  }
  const mb = kb / 1024;
  return `${mb.toFixed(2)} MB`;
}

export default function PackUpload() {
  const { accessToken } = useAuth();
  const [file, setFile] = useState<File | null>(null);
  const [packId, setPackId] = useState("");
  const [packVersion, setPackVersion] = useState("");
  const [notes, setNotes] = useState("");
  const [tenantId, setTenantId] = useState("");
  const [breakGlassToken, setBreakGlassToken] = useState("");
  const [dryRun, setDryRun] = useState(true);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<UploadResult | null>(null);

  const fileMeta = useMemo(() => {
    if (!file) {
      return null;
    }
    return `${file.name} • ${formatBytes(file.size)}`;
  }, [file]);

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!file) {
      setResult({ ok: false, status: 0, error: "Select a pack bundle to upload." });
      return;
    }
    setLoading(true);
    setResult(null);
    const response = await ingestPack(
      {
        file,
        packId: packId.trim() || undefined,
        packVersion: packVersion.trim() || undefined,
        notes: notes.trim() || undefined,
        dryRun,
        tenantId: tenantId.trim() || undefined,
        breakGlassToken: breakGlassToken.trim() || undefined,
      },
      accessToken,
    );

    setResult({
      ok: response.ok,
      status: response.status,
      body: response.data,
      raw: response.raw,
      error: response.error,
    });
    setLoading(false);
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Pack upload</h2>
          <p className="text-sm text-slate-300">
            Upload a skill pack bundle for validation or publish. Dry-run is enabled by default.
          </p>
        </div>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          {dryRun ? "dry run" : "publish"}
        </span>
      </header>

      <form className="mt-6 grid gap-4" onSubmit={onSubmit}>
        <div className="grid gap-4 md:grid-cols-[1.4fr_1fr]">
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack bundle (.zip)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              type="file"
              accept=".zip"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              required
            />
            {fileMeta && <div className="mt-2 text-xs text-slate-400">{fileMeta}</div>}
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Tenant ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="tenant-1"
            />
          </label>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packId}
              onChange={(e) => setPackId(e.target.value)}
              placeholder="enterprise"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Pack version (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packVersion}
              onChange={(e) => setPackVersion(e.target.value)}
              placeholder="0.0.2"
            />
          </label>
        </div>

        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Release notes (optional)
          <textarea
            className="mt-2 min-h-[96px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Describe the pack changes..."
          />
        </label>

        <div className="grid gap-4 md:grid-cols-[1fr_auto]">
          <label className="flex items-center gap-3 text-sm text-slate-200">
            <input
              className="h-4 w-4 rounded border-white/20 bg-slate-950/50"
              type="checkbox"
              checked={dryRun}
              onChange={(e) => setDryRun(e.target.checked)}
            />
            Run validation only (dry run)
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              placeholder="paste token"
              type="password"
            />
          </label>
        </div>

        <div className="flex flex-wrap gap-3">
          <button
            className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
            type="submit"
            disabled={loading}
          >
            {loading ? "Uploading..." : dryRun ? "Run dry run" : "Upload pack"}
          </button>
          <button
            className="rounded-full border border-white/10 bg-white/5 px-5 py-2 text-sm font-semibold text-slate-200"
            type="button"
            onClick={() => {
              setFile(null);
              setPackId("");
              setPackVersion("");
              setNotes("");
              setTenantId("");
              setBreakGlassToken("");
              setResult(null);
            }}
          >
            Clear
          </button>
        </div>
      </form>

      {result && (
        <div
          className={`mt-6 rounded-2xl border p-4 text-sm ${
            result.ok
              ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
              : "border-amber-500/40 bg-amber-500/10 text-amber-100"
          }`}
        >
          <p className="font-semibold">
            {result.ok ? "Upload completed" : "Upload failed"} (HTTP {result.status || "n/a"})
          </p>
          {result.error && <p className="mt-2 text-xs">{result.error}</p>}
          {result.body != null && (
            <pre className="mt-3 max-h-64 overflow-auto rounded-xl border border-white/10 bg-black/30 p-3 text-xs text-slate-100">
{JSON.stringify(result.body, null, 2)}
            </pre>
          )}
          {!result.body && result.raw && (
            <pre className="mt-3 max-h-64 overflow-auto rounded-xl border border-white/10 bg-black/30 p-3 text-xs text-slate-100">
{result.raw}
            </pre>
          )}
        </div>
      )}
    </section>
  );
}

