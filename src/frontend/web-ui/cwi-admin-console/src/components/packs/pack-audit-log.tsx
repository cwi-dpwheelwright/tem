"use client";

import { useMemo, useState } from "react";
import { getPackAuditLog, type PackAuditQuery } from "@/lib/packs/audit-client";
import { useAuth } from "@/lib/auth/context";

type AuditEvent = Record<string, unknown>;

type ActionResult = {
  ok: boolean;
  status: number;
  data?: AuditEvent[];
  error?: string;
};

type AuditExportRow = {
  eventType: string;
  packId: string;
  packVersion: string;
  actor: string;
  reason: string;
  timestamp: string;
  traceId: string;
  hash: string;
};

function normalizeEvents(payload: unknown): AuditEvent[] {
  if (Array.isArray(payload)) {
    return payload as AuditEvent[];
  }
  if (payload && typeof payload === "object") {
    const record = payload as Record<string, unknown>;
    const candidates = [record.events, record.items, record.data];
    for (const candidate of candidates) {
      if (Array.isArray(candidate)) {
        return candidate as AuditEvent[];
      }
    }
  }
  return [];
}

function parsePayload(event: AuditEvent): Record<string, unknown> | null {
  const raw =
    event.payload ??
    event.payload_json ??
    event.payloadJson ??
    event.event_payload ??
    event.eventPayload;

  if (!raw) {
    return null;
  }

  if (typeof raw === "string") {
    try {
      const parsed = JSON.parse(raw);
      return parsed && typeof parsed === "object" ? (parsed as Record<string, unknown>) : null;
    } catch {
      return null;
    }
  }

  if (typeof raw === "object") {
    return raw as Record<string, unknown>;
  }

  return null;
}

function getString(value: unknown): string | null {
  if (typeof value === "string" && value.trim().length > 0) {
    return value;
  }
  return null;
}

function resolveField(event: AuditEvent, payload: Record<string, unknown> | null, keys: string[]): string | null {
  for (const key of keys) {
    const direct = getString(event[key]);
    if (direct) {
      return direct;
    }
    const nested = payload ? getString(payload[key]) : null;
    if (nested) {
      return nested;
    }
  }
  return null;
}

function formatTimestamp(value: string | null): string {
  if (!value) {
    return "n/a";
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }
  return parsed.toLocaleString();
}

function toUtc(value: string): string | null {
  if (!value) {
    return null;
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  return parsed.toISOString();
}

function toIso(value: string | null): string {
  if (!value) {
    return "";
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }
  return parsed.toISOString();
}

function buildExportRow(entry: AuditEvent): AuditExportRow {
  const payload = parsePayload(entry);
  const pack = resolveField(entry, payload, ["pack_id", "packId", "pack_name", "packName", "pack"]);
  const version = resolveField(entry, payload, ["pack_version", "packVersion", "version"]);
  const actor = resolveField(entry, payload, ["actor_user_id", "actorUserId", "actor_id", "actorId", "user"]);
  const action = resolveField(entry, payload, ["event_type", "eventType", "action", "type"]) ?? "unknown";
  const reason = resolveField(entry, payload, ["reason", "rollback_reason", "notes"]);
  const timestamp =
    resolveField(entry, payload, [
      "timestamp_utc",
      "created_utc",
      "createdUtc",
      "timestamp",
    ]) ?? null;
  const traceId = resolveField(entry, payload, ["trace_id", "traceId"]);
  const hash = resolveField(entry, payload, ["hash"]);

  return {
    eventType: action,
    packId: pack ?? "",
    packVersion: version ?? "",
    actor: actor ?? "",
    reason: reason ?? "",
    timestamp: toIso(timestamp),
    traceId: traceId ?? "",
    hash: hash ?? "",
  };
}

function escapeCsv(value: string): string {
  const escaped = value.replace(/"/g, '""');
  if (/[",\n]/.test(escaped)) {
    return `"${escaped}"`;
  }
  return escaped;
}

function downloadFile(filename: string, contents: string, mimeType: string) {
  const blob = new Blob([contents], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

export default function PackAuditLog() {
  const { accessToken } = useAuth();
  const [tenantId, setTenantId] = useState("");
  const [packId, setPackId] = useState("");
  const [actorUserId, setActorUserId] = useState("");
  const [eventType, setEventType] = useState("");
  const [startUtc, setStartUtc] = useState("");
  const [endUtc, setEndUtc] = useState("");
  const [limit, setLimit] = useState(50);
  const [breakGlassToken, setBreakGlassToken] = useState("");
  const [result, setResult] = useState<ActionResult | null>(null);
  const [loading, setLoading] = useState(false);

  const handleFetch = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    const query: PackAuditQuery = {
      tenantId: tenantId.trim() || undefined,
      packId: packId.trim() || undefined,
      actorUserId: actorUserId.trim() || undefined,
      eventType: eventType.trim() || undefined,
      startUtc: toUtc(startUtc) || undefined,
      endUtc: toUtc(endUtc) || undefined,
      limit: Number.isNaN(limit) ? undefined : limit,
    };
    const response = await getPackAuditLog(query, accessToken, breakGlassToken || null);
    const events = response.ok ? normalizeEvents(response.data) : undefined;
    setResult({
      ok: response.ok,
      status: response.status,
      data: events,
      error: response.error,
    });
    setLoading(false);
  };

  const entries = result?.data ?? [];
  const exportRows = useMemo(() => entries.map(buildExportRow), [entries]);

  const summary = useMemo(() => {
    if (!result) {
      return null;
    }
    if (!result.ok) {
      return `Request failed (${result.status || "n/a"}).`;
    }
    return `Loaded ${entries.length} audit events.`;
  }, [result, entries.length]);

  const handleExportJson = () => {
    const filename = `pack-audit-${new Date().toISOString()}.json`;
    downloadFile(filename, JSON.stringify(exportRows, null, 2), "application/json");
  };

  const handleExportCsv = () => {
    const headers = [
      "event_type",
      "pack_id",
      "pack_version",
      "actor",
      "reason",
      "timestamp",
      "trace_id",
      "hash",
    ];
    const rows = exportRows.map((row) =>
      [
        row.eventType,
        row.packId,
        row.packVersion,
        row.actor,
        row.reason,
        row.timestamp,
        row.traceId,
        row.hash,
      ]
        .map((value) => escapeCsv(value))
        .join(","),
    );
    const csv = [headers.join(","), ...rows].join("\n");
    const filename = `pack-audit-${new Date().toISOString()}.csv`;
    downloadFile(filename, csv, "text/csv");
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Pack audit log</h2>
          <p className="text-sm text-slate-300">
            Review lifecycle events emitted by the orchestrator ledger.
          </p>
        </div>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          audit
        </span>
      </header>

      <form className="mt-6 grid gap-4" onSubmit={handleFetch}>
        <div className="grid gap-4 md:grid-cols-[1fr_1fr_1fr]">
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
            Pack ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={packId}
              onChange={(e) => setPackId(e.target.value)}
              placeholder="enterprise"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Actor user ID (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={actorUserId}
              onChange={(e) => setActorUserId(e.target.value)}
              placeholder="admin@cwi"
            />
          </label>
        </div>

        <div className="grid gap-4 md:grid-cols-[1fr_1fr_1fr]">
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Event type (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={eventType}
              onChange={(e) => setEventType(e.target.value)}
              placeholder="pack.retire"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Start (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              type="datetime-local"
              value={startUtc}
              onChange={(e) => setStartUtc(e.target.value)}
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            End (optional)
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              type="datetime-local"
              value={endUtc}
              onChange={(e) => setEndUtc(e.target.value)}
            />
          </label>
        </div>

        <div className="grid gap-4 md:grid-cols-[1fr_auto_auto]">
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Break-glass token
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={breakGlassToken}
              onChange={(e) => setBreakGlassToken(e.target.value)}
              type="password"
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Limit
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              type="number"
              min={1}
              max={500}
              value={limit}
              onChange={(e) => setLimit(Number(e.target.value))}
            />
          </label>
          <div className="flex items-end">
            <button
              className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white"
              type="submit"
              disabled={loading}
            >
              {loading ? "Loading..." : "Load audit log"}
            </button>
          </div>
        </div>
      </form>

      {summary && (
        <div className="mt-6 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-white/10 bg-white/5 p-3 text-sm text-slate-200">
          <span>{summary}</span>
          <div className="flex flex-wrap gap-2">
            <button
              className="rounded-full border border-white/10 bg-white/10 px-4 py-1 text-xs font-semibold text-slate-100"
              type="button"
              onClick={handleExportJson}
              disabled={entries.length === 0}
            >
              Export JSON
            </button>
            <button
              className="rounded-full border border-white/10 bg-white/10 px-4 py-1 text-xs font-semibold text-slate-100"
              type="button"
              onClick={handleExportCsv}
              disabled={entries.length === 0}
            >
              Export CSV
            </button>
          </div>
        </div>
      )}

      {result?.error && (
        <div className="mt-4 rounded-2xl border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          {result.error}
        </div>
      )}

      <div className="mt-4 text-xs text-slate-400">
        Exports include summary fields only (no raw payload JSON).
      </div>

      <div className="mt-6 space-y-4">
        {entries.length === 0 && result?.ok ? (
          <div className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-300">
            No audit events found.
          </div>
        ) : null}
        {entries.map((entry, index) => {
          const payload = parsePayload(entry);
          const pack = resolveField(entry, payload, ["pack_id", "packId", "pack_name", "packName", "pack"]);
          const version = resolveField(entry, payload, ["pack_version", "packVersion", "version"]);
          const actor = resolveField(entry, payload, ["actor_user_id", "actorUserId", "actor_id", "actorId", "user"]);
          const action = resolveField(entry, payload, ["event_type", "eventType", "action", "type"]) ?? "unknown";
          const reason = resolveField(entry, payload, ["reason", "rollback_reason", "notes"]);
          const timestamp =
            resolveField(entry, payload, [
              "timestamp_utc",
              "created_utc",
              "createdUtc",
              "timestamp",
            ]) ?? null;
          const traceId = resolveField(entry, payload, ["trace_id", "traceId"]);
          const hash = resolveField(entry, payload, ["hash"]);

          return (
            <article
              key={`${action}-${index}`}
              className="rounded-2xl border border-white/10 bg-white/5 p-4 text-sm text-slate-200"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-400">{action}</p>
                  <h3 className="mt-2 text-base font-semibold text-white">
                    {pack || "Unknown pack"}
                  </h3>
                  <p className="mt-1 text-xs text-slate-400">{version ? `Version ${version}` : ""}</p>
                </div>
                <div className="text-right text-xs text-slate-400">
                  <div>{formatTimestamp(timestamp)}</div>
                  {actor && <div className="mt-1">Actor: {actor}</div>}
                </div>
              </div>

              <div className="mt-3 grid gap-2 text-xs text-slate-300 md:grid-cols-2">
                {reason && (
                  <div>
                    <span className="text-slate-400">Reason:</span> {reason}
                  </div>
                )}
                {traceId && (
                  <div>
                    <span className="text-slate-400">Trace:</span> {traceId}
                  </div>
                )}
                {hash && (
                  <div className="md:col-span-2">
                    <span className="text-slate-400">Hash:</span> {hash}
                  </div>
                )}
              </div>

              <details className="mt-3 text-xs text-slate-300">
                <summary className="cursor-pointer text-slate-200">View raw event</summary>
                <pre className="mt-3 max-h-64 overflow-auto rounded-xl border border-white/10 bg-black/30 p-3 text-xs text-slate-100">
{JSON.stringify(payload ?? entry, null, 2)}
                </pre>
              </details>
            </article>
          );
        })}
      </div>
    </section>
  );
}
