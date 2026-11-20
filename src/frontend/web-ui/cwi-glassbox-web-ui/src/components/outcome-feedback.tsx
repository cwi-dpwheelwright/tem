"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import type { OutcomeFeedbackRequest } from "@/lib/api/types";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";

export default function OutcomeFeedbackPanel() {
  const { accessToken, user, hasRole } = useAuth();
  const config = useMemo(() => getRuntimeConfigStatus(), []);

  const [tenantId, setTenantId] = useState("");
  const [traceId, setTraceId] = useState("");
  const [actorId, setActorId] = useState(
    user?.profile.preferred_username ?? user?.profile.email ?? "",
  );
  const [feedback, setFeedback] = useState<OutcomeFeedbackRequest["feedback"]>("up");
  const [note, setNote] = useState("");
  const [consent, setConsent] = useState(false);
  const [summary, setSummary] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const canOperate = hasRole("operator");

  async function submitFeedback() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!tenantId.trim() || !traceId.trim() || !actorId.trim()) {
      setError("Tenant, trace, and actor are required.");
      return;
    }
    if (!consent) {
      setError("Consent must be granted.");
      return;
    }
    setIsLoading(true);
    setStatus(null);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
        "X-Roles": "operator,viewer",
      }),
    });
    try {
      const payload: OutcomeFeedbackRequest & {
        actor_id: string;
        consent_granted: boolean;
      } = {
        tenant_id: tenantId.trim(),
        trace_id: traceId.trim(),
        feedback,
        note: note.trim() || undefined,
        actor_id: actorId.trim(),
        consent_granted: consent,
      };
      const response = await client.submitOutcomeFeedback(payload);
      setStatus(`Feedback recorded (${response.recorded_utc}).`);
      setSummary(
        `Up: ${response.summary.up_count} / Down: ${response.summary.down_count}`,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Feedback failed.");
    } finally {
      setIsLoading(false);
    }
  }

  async function loadSummary() {
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }
    if (!tenantId.trim() || !traceId.trim()) {
      setError("Tenant and trace are required.");
      return;
    }
    setIsLoading(true);
    setError(null);
    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      extraHeadersProvider: () => ({
        "X-Tenant-Id": tenantId.trim(),
      }),
    });
    try {
      const response = await client.outcomeFeedbackSummary(tenantId.trim(), traceId.trim());
      setSummary(`Up: ${response.up_count} / Down: ${response.down_count}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Summary lookup failed.");
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Outcome feedback</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Patent #8</span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Capture operator feedback and correlate outcomes with prior traces.
      </p>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={traceId}
          onChange={(event) => setTraceId(event.target.value)}
          placeholder="trace_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={actorId}
          onChange={(event) => setActorId(event.target.value)}
          placeholder="actor_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <select
          value={feedback}
          onChange={(event) =>
            setFeedback(event.target.value as OutcomeFeedbackRequest["feedback"])
          }
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        >
          <option value="up">Positive</option>
          <option value="down">Negative</option>
        </select>
      </div>
      <textarea
        value={note}
        onChange={(event) => setNote(event.target.value)}
        placeholder="context note (optional)"
        className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-4 py-2 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        rows={2}
      />
      <label className="mt-3 flex items-center gap-2 text-xs text-slate-600">
        <input
          type="checkbox"
          checked={consent}
          onChange={(event) => setConsent(event.target.checked)}
        />
        Consent granted for recording outcome feedback.
      </label>
      <div className="mt-4 flex flex-wrap gap-3">
        <button
          className="rounded-2xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
          onClick={() => void submitFeedback()}
          disabled={!canOperate || isLoading}
        >
          {isLoading ? "Recording..." : "Record feedback"}
        </button>
        <button
          className="rounded-2xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 disabled:cursor-not-allowed disabled:text-slate-400"
          onClick={() => void loadSummary()}
          disabled={!traceId.trim() || isLoading}
        >
          Load summary
        </button>
      </div>
      {!canOperate ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          Operator role required to record feedback.
        </div>
      ) : null}
      {status ? (
        <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-700">
          {status}
        </div>
      ) : null}
      {summary ? (
        <div className="mt-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-600">
          {summary}
        </div>
      ) : null}
      {error ? (
        <div className="mt-3 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
          {error}
        </div>
      ) : null}
    </div>
  );
}
