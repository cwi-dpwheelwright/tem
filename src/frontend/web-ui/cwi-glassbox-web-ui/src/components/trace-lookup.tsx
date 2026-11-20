"use client";

import { useMemo, useState } from "react";
import { createApiClient } from "@/lib/api/client";
import { getRuntimeConfigStatus } from "@/lib/config";
import { useAuth } from "@/lib/auth/context";
import { createTelemetryReporter } from "@/lib/telemetry";
import type { OutcomeFeedbackSummary, TraceLookupResponse } from "@/lib/api/types";

export default function TraceLookup() {
  const { accessToken } = useAuth();
  const [tenantId, setTenantId] = useState("");
  const [traceId, setTraceId] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [result, setResult] = useState<TraceLookupResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<"up" | "down" | null>(null);
  const [feedbackNote, setFeedbackNote] = useState("");
  const [feedbackStatus, setFeedbackStatus] = useState<string | null>(null);
  const [feedbackSummary, setFeedbackSummary] = useState<OutcomeFeedbackSummary | null>(null);
  const [isFeedbackLoading, setIsFeedbackLoading] = useState(false);

  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const telemetry = useMemo(() => {
    if (!config.config) {
      return null;
    }
    return createTelemetryReporter({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantProvider: () => tenantId,
      surface: "trace-lookup",
    });
  }, [accessToken, config, tenantId]);

  const isDisabled = !config.config || !traceId.trim() || isLoading;

  async function handleLookup(event: React.FormEvent) {
    event.preventDefault();
    if (!config.config) {
      setError("Missing API configuration.");
      return;
    }

    setIsLoading(true);
    setError(null);
    setResult(null);
    setFeedback(null);
    setFeedbackNote("");
    setFeedbackStatus(null);
    setFeedbackSummary(null);
    void telemetry?.report("ui.trace.lookup.requested", {
      trace_id: traceId.trim(),
    });

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      const payload = await client.traceLookup(traceId.trim());
      setResult(payload);
      const trace = (payload as { trace?: Record<string, unknown> }).trace ?? {};
      const guardrails = Array.isArray((trace as { guardrails?: unknown }).guardrails)
        ? (trace as { guardrails?: unknown[] }).guardrails?.length ?? 0
        : 0;
      const validations = Array.isArray((trace as { validations?: unknown }).validations)
        ? (trace as { validations?: unknown[] }).validations?.length ?? 0
        : 0;
      const discrepancies = Array.isArray((trace as { discrepancies?: unknown }).discrepancies)
        ? (trace as { discrepancies?: unknown[] }).discrepancies?.length ?? 0
        : 0;
      const corrections = Array.isArray((trace as { corrections?: unknown }).corrections)
        ? (trace as { corrections?: unknown[] }).corrections?.length ?? 0
        : 0;
      void telemetry?.report("ui.trace.lookup.succeeded", {
        trace_id: traceId.trim(),
        guardrails,
        validations,
        discrepancies,
        corrections,
      });
      if (tenantId.trim()) {
        try {
          const summary = await client.outcomeFeedbackSummary(tenantId.trim(), traceId.trim());
          setFeedbackSummary(summary);
        } catch {
          setFeedbackSummary(null);
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Trace lookup failed.");
      void telemetry?.report("ui.trace.lookup.failed", {
        trace_id: traceId.trim(),
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsLoading(false);
    }
  }

  async function submitFeedback(value: "up" | "down") {
    if (!config.config || !result || !tenantId.trim()) {
      setFeedbackStatus("Provide tenant_id to enable feedback submission.");
      return;
    }

    const trace = (result as { trace?: Record<string, unknown> }).trace ?? {};
    const resolvedTraceId =
      typeof trace.trace_id === "string" && trace.trace_id.trim()
        ? trace.trace_id
        : traceId.trim();

    setFeedback(value);
    setFeedbackStatus(null);
    setIsFeedbackLoading(true);

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
    });

    try {
      const response = await client.submitOutcomeFeedback({
        tenant_id: tenantId.trim(),
        trace_id: resolvedTraceId,
        feedback: value,
        reason_codes: [],
        note: feedbackNote.trim() || undefined,
      });
      setFeedbackSummary(response.summary);
      setFeedbackStatus(value === "up" ? "Feedback recorded: thumbs up." : "Feedback recorded.");
      void telemetry?.report("ui.trace.feedback.submitted", {
        trace_id: resolvedTraceId,
        feedback: value,
      });
    } catch (err) {
      setFeedbackStatus(err instanceof Error ? err.message : "Feedback submission failed.");
      void telemetry?.report("ui.trace.feedback.failed", {
        trace_id: resolvedTraceId,
        feedback: value,
        error: err instanceof Error ? err.message : "unknown",
      });
    } finally {
      setIsFeedbackLoading(false);
    }
  }

  return (
    <div
      className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60"
      data-testid="trace-lookup"
    >
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-slate-800">Trace lookup</p>
        <span className="text-xs uppercase tracking-[0.25em] text-slate-500">Glassbox</span>
      </div>
      <form className="mt-6 grid gap-4 sm:grid-cols-[1fr_1fr_auto]" onSubmit={handleLookup}>
        <input
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
          placeholder="tenant_id (for telemetry)"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <input
          value={traceId}
          onChange={(event) => setTraceId(event.target.value)}
          placeholder="trace_id"
          className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800 focus:border-slate-400 focus:outline-none"
        />
        <button
          type="submit"
          disabled={isDisabled}
          className="rounded-2xl bg-slate-900 px-6 py-3 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:bg-slate-400"
        >
          {isLoading ? "Searching..." : "Lookup"}
        </button>
      </form>
      {error ? (
        <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {error}
        </div>
      ) : null}
      {result ? (
        <div className="mt-4 space-y-4">
          <EvidenceSources payload={result} />
          <GovernanceInsights payload={result} />
          <OutcomeFeedback
            tenantId={tenantId}
            feedback={feedback}
            feedbackNote={feedbackNote}
            feedbackStatus={feedbackStatus}
            summary={feedbackSummary}
            isLoading={isFeedbackLoading}
            onNoteChange={setFeedbackNote}
            onSubmit={submitFeedback}
          />
          <pre className="max-h-56 overflow-auto rounded-2xl bg-slate-900/95 p-4 text-xs text-slate-100">
            {JSON.stringify(result, null, 2)}
          </pre>
        </div>
      ) : (
        <p className="mt-4 text-xs text-slate-500">
          Fetch the full trace record for audits and discrepancy analysis.
        </p>
      )}
    </div>
  );
}

function EvidenceSources({ payload }: { payload: TraceLookupResponse }) {
  const citations = Array.isArray((payload as { citations?: unknown }).citations)
    ? ((payload as { citations?: Array<Record<string, unknown>> }).citations ?? [])
    : [];
  const trace = (payload as { trace?: { sources?: string[] } }).trace;
  const sources = Array.isArray(trace?.sources) ? trace?.sources ?? [] : [];

  if (citations.length === 0 && sources.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-3 text-xs text-slate-500">
        No evidence sources found for this trace.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Evidence sources</p>
      <div className="grid gap-3 sm:grid-cols-2">
        {citations.map((citation, index) => {
          const docId = citation.doc_id as string | undefined;
          const chunkId = citation.chunk_id as string | undefined;
          const label = docId && chunkId ? `${docId}:${chunkId}` : `citation-${index + 1}`;
          const href = docId && chunkId ? `/docs/${docId}?chunk=${encodeURIComponent(chunkId)}` : "#";
          return (
            <a
              key={`citation-${index}-${label}`}
              href={href}
              className="rounded-2xl border border-white/80 bg-white/90 px-4 py-3 text-xs font-semibold text-slate-700 hover:border-slate-300"
            >
              {label}
            </a>
          );
        })}
        {sources.map((source, index) => (
          <span
            key={`source-${index}-${source}`}
            className="rounded-2xl border border-white/80 bg-white/90 px-4 py-3 text-xs font-semibold text-slate-600"
          >
            {source}
          </span>
        ))}
      </div>
    </div>
  );
}

type GuardrailViolation = {
  id?: string;
  name?: string;
  severity?: string;
  message?: string;
};

type ValidationIssue = {
  rule?: string;
  field?: string;
  detail?: string;
};

type DiscrepancyRecord = {
  field?: string;
  expected?: string;
  actual?: string;
  note?: string;
};

type CorrectionDiff = {
  field?: string;
  from?: string;
  to?: string;
  note?: string;
};

function GovernanceInsights({ payload }: { payload: TraceLookupResponse }) {
  const trace = (payload as { trace?: Record<string, unknown> }).trace ?? {};
  const guardrails = Array.isArray((trace as { guardrails?: unknown }).guardrails)
    ? (((trace as { guardrails?: GuardrailViolation[] }).guardrails ?? []) as GuardrailViolation[])
    : [];
  const validations = Array.isArray((trace as { validations?: unknown }).validations)
    ? (((trace as { validations?: ValidationIssue[] }).validations ?? []) as ValidationIssue[])
    : [];
  const discrepancies = Array.isArray((trace as { discrepancies?: unknown }).discrepancies)
    ? (((trace as { discrepancies?: DiscrepancyRecord[] }).discrepancies ?? []) as DiscrepancyRecord[])
    : [];
  const corrections = Array.isArray((trace as { corrections?: unknown }).corrections)
    ? (((trace as { corrections?: CorrectionDiff[] }).corrections ?? []) as CorrectionDiff[])
    : [];

  if (
    guardrails.length === 0 &&
    validations.length === 0 &&
    discrepancies.length === 0 &&
    corrections.length === 0
  ) {
    return (
      <div className="rounded-2xl border border-dashed border-slate-200 px-4 py-3 text-xs text-slate-500">
        No governance discrepancies or guardrail violations reported.
      </div>
    );
  }

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-5 shadow-sm">
      <div className="flex items-center justify-between">
        <p className="text-xs uppercase tracking-[0.22em] text-slate-500">Governance insights</p>
        <span className="text-[10px] uppercase tracking-[0.22em] text-slate-400">
          Discrepancy + validation + guardrails
        </span>
      </div>

      {guardrails.length > 0 ? (
        <div className="mt-4">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-rose-600">
            Guardrail violations
          </p>
          <div className="mt-3 space-y-2">
            {guardrails.map((item, index) => (
              <div
                key={`guardrail-${index}-${item.id ?? item.name ?? "item"}`}
                className="rounded-2xl border border-rose-100 bg-rose-50/60 px-3 py-2 text-xs text-rose-700"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">
                    {item.name ?? item.id ?? `Violation ${index + 1}`}
                  </span>
                  {item.severity ? (
                    <span className="text-[10px] uppercase tracking-[0.2em] text-rose-400">
                      {item.severity}
                    </span>
                  ) : null}
                </div>
                <p className="mt-1 text-xs text-rose-600">
                  {item.message ?? "Violation detail unavailable."}
                </p>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {validations.length > 0 ? (
        <div className="mt-4">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-amber-700">
            Validation findings
          </p>
          <div className="mt-3 space-y-2">
            {validations.map((item, index) => (
              <div
                key={`validation-${index}-${item.rule ?? item.field ?? "item"}`}
                className="rounded-2xl border border-amber-100 bg-amber-50/70 px-3 py-2 text-xs text-amber-800"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">
                    {item.rule ?? item.field ?? `Validation ${index + 1}`}
                  </span>
                  {item.field ? (
                    <span className="text-[10px] uppercase tracking-[0.2em] text-amber-500">
                      {item.field}
                    </span>
                  ) : null}
                </div>
                <p className="mt-1 text-xs text-amber-700">
                  {item.detail ?? "Validation detail unavailable."}
                </p>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {discrepancies.length > 0 ? (
        <div className="mt-4">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-600">
            Discrepancies
          </p>
          <div className="mt-3 space-y-2">
            {discrepancies.map((item, index) => (
              <div
                key={`discrepancy-${index}-${item.field ?? "field"}`}
                className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-700"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">
                    {item.field ?? `Field ${index + 1}`}
                  </span>
                  {item.note ? (
                    <span className="text-[10px] uppercase tracking-[0.2em] text-slate-400">
                      {item.note}
                    </span>
                  ) : null}
                </div>
                <p className="mt-1 text-xs text-slate-600">
                  Expected: {item.expected ?? "n/a"} - Actual: {item.actual ?? "n/a"}
                </p>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {corrections.length > 0 ? (
        <div className="mt-4">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-600">
            Correction diffs
          </p>
          <div className="mt-3 space-y-2">
            {corrections.map((item, index) => (
              <div
                key={`correction-${index}-${item.field ?? "field"}`}
                className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-700"
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold">
                    {item.field ?? `Field ${index + 1}`}
                  </span>
                  {item.note ? (
                    <span className="text-[10px] uppercase tracking-[0.2em] text-slate-400">
                      {item.note}
                    </span>
                  ) : null}
                </div>
                <p className="mt-1 text-xs text-slate-600">
                  From: {item.from ?? "n/a"} - To: {item.to ?? "n/a"}
                </p>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function OutcomeFeedback({
  tenantId,
  feedback,
  feedbackNote,
  feedbackStatus,
  summary,
  isLoading,
  onNoteChange,
  onSubmit,
}: {
  tenantId: string;
  feedback: "up" | "down" | null;
  feedbackNote: string;
  feedbackStatus: string | null;
  summary: OutcomeFeedbackSummary | null;
  isLoading: boolean;
  onNoteChange: (value: string) => void;
  onSubmit: (value: "up" | "down") => Promise<void>;
}) {
  const disabled = !tenantId.trim();

  return (
    <div className="rounded-3xl border border-white/80 bg-white/80 p-5 shadow-sm">
      <div className="flex items-center justify-between">
        <p className="text-xs uppercase tracking-[0.22em] text-slate-500">Outcome feedback</p>
        <span className="text-[10px] uppercase tracking-[0.22em] text-slate-400">
          Trace correlation
        </span>
      </div>
      <p className="mt-2 text-xs text-slate-600">
        Record operator feedback tied to this trace. Tenant ID is required.
      </p>
      <div className="mt-3 flex flex-wrap gap-3">
        <button
          className="rounded-2xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-emerald-300"
          onClick={() => void onSubmit("up")}
          disabled={disabled || isLoading}
          aria-pressed={feedback === "up"}
        >
          {isLoading && feedback === "up" ? "Recording..." : "Thumbs up"}
        </button>
        <button
          className="rounded-2xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:bg-rose-300"
          onClick={() => void onSubmit("down")}
          disabled={disabled || isLoading}
          aria-pressed={feedback === "down"}
        >
          {isLoading && feedback === "down" ? "Recording..." : "Thumbs down"}
        </button>
      </div>
      <textarea
        value={feedbackNote}
        onChange={(event) => onNoteChange(event.target.value)}
        placeholder="optional reason codes / notes"
        className="mt-3 w-full rounded-2xl border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700 focus:border-slate-400 focus:outline-none"
        rows={2}
      />
      {feedbackStatus ? (
        <div className="mt-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-700">
          {feedbackStatus}
        </div>
      ) : null}
      {summary ? (
        <div className="mt-3 grid gap-2 sm:grid-cols-3">
          <SummaryPill label="Up" value={summary.up_count} tone="emerald" />
          <SummaryPill label="Down" value={summary.down_count} tone="rose" />
          <SummaryPill label="Last" value={summary.last_feedback ?? "n/a"} tone="slate" />
        </div>
      ) : null}
      {disabled ? (
        <div className="mt-3 rounded-2xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700">
          Enter a tenant ID above to enable feedback submission.
        </div>
      ) : null}
    </div>
  );
}

function SummaryPill({
  label,
  value,
  tone,
}: {
  label: string;
  value: string | number;
  tone: "emerald" | "rose" | "slate";
}) {
  const toneClass =
    tone === "emerald"
      ? "border-emerald-100 bg-emerald-50 text-emerald-700"
      : tone === "rose"
        ? "border-rose-100 bg-rose-50 text-rose-700"
        : "border-slate-200 bg-slate-50 text-slate-700";

  return (
    <div className={`rounded-2xl border px-3 py-2 text-xs font-semibold ${toneClass}`}>
      <span className="uppercase tracking-[0.18em] text-[10px] text-slate-400">{label}</span>
      <div className="mt-1 text-sm">{value}</div>
    </div>
  );
}
