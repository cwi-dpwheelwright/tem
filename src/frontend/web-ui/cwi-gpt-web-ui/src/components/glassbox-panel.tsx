import type { Trace } from "@/lib/api/types";
import { formatNumber } from "@/lib/format";

type GlassboxPanelProps = {
  trace?: Trace | null;
  citationsCount?: number;
  loading?: boolean;
};

type ConfidenceMeta = {
  percent: number;
  label: string;
  rawLabel: string;
};

const MAX_PERCENT = 100;
const RING_SIZE = 64;
const RING_STROKE = 6;
const INLINE_RING_SIZE = 44;
const INLINE_RING_STROKE = 4;

function createRingMetrics(size: number, stroke: number) {
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  return { radius, circumference };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function normalizeConfidence(confidence?: number | null): ConfidenceMeta | null {
  if (typeof confidence !== "number" || !Number.isFinite(confidence)) {
    return null;
  }
  const raw = confidence;
  const percent = raw <= 1 ? raw * 100 : raw;
  const clamped = clamp(percent, 0, MAX_PERCENT);
  const rawLabel =
    raw <= 1
      ? formatNumber(raw, { maximumFractionDigits: 3 })
      : formatNumber(raw, { maximumFractionDigits: 1 });
  return {
    percent: clamped,
    label: `${Math.round(clamped)}%`,
    rawLabel,
  };
}

function resolveRingClass(percent: number | null): string {
  if (percent === null) {
    return "glassbox-ring-progress--neutral";
  }
  if (percent >= 80) {
    return "glassbox-ring-progress--high";
  }
  if (percent >= 55) {
    return "glassbox-ring-progress--mid";
  }
  return "glassbox-ring-progress--low";
}

export function GlassboxPanel({ trace, citationsCount, loading }: GlassboxPanelProps) {
  const confidence = loading ? normalizeConfidence(0) : normalizeConfidence(trace?.confidence);
  const ringClass = resolveRingClass(confidence?.percent ?? null);
  const ringMetrics = createRingMetrics(RING_SIZE, RING_STROKE);
  const dashOffset = confidence
    ? ringMetrics.circumference * (1 - confidence.percent / 100)
    : ringMetrics.circumference;

  const chips: Array<{ label: string; value: string }> = [];
  if (trace?.policy_mode) {
    chips.push({ label: "Policy", value: trace.policy_mode });
  }

  if (trace?.route) {
    chips.push({ label: "Route", value: trace.route });
  }

  if (trace?.model) {
    chips.push({ label: "Model", value: trace.model });
  }
  if (typeof citationsCount === "number") {
    chips.push({ label: "Citations", value: String(citationsCount) });
  }
  if (Array.isArray(trace?.sources)) {
    chips.push({ label: "Sources", value: String(trace?.sources.length ?? 0) });
  }

  return (
    <div className="glassbox-panel">
      <div className="glassbox-header">
        <div>
          <div className="glassbox-title">
            Glass Box Explainability
          </div>
          <p className="glassbox-subtitle">Live trace signals</p>
        </div>
        <span
          className={`glassbox-status ${trace ? "glassbox-status--active" : "glassbox-status--idle"}`}
        >
          {trace ? "Active" : "Awaiting"}
        </span>
      </div>

      {chips.length > 0 ? (
        <div className="glassbox-chips">
          {chips.map((chip) => (
            <span
              key={`${chip.label}-${chip.value}`}
              className="glassbox-chip"
            >
              {chip.label}: {chip.value}
            </span>
          ))}
        </div>
      ) : (
        <p className="glassbox-empty">
          Submit a question to populate trace metadata.
        </p>
      )}

      <div className="glassbox-confidence">
        <div className="glassbox-ring">
          <svg
            className="glassbox-ring-svg"
            viewBox={`0 0 ${RING_SIZE} ${RING_SIZE}`}
            aria-hidden="true"
          >
            <circle
              cx={RING_SIZE / 2}
              cy={RING_SIZE / 2}
              r={ringMetrics.radius}
              strokeWidth={RING_STROKE}
              className="glassbox-ring-track"
              stroke="currentColor"
              fill="none"
            />
            <circle
              cx={RING_SIZE / 2}
              cy={RING_SIZE / 2}
              r={ringMetrics.radius}
              strokeWidth={RING_STROKE}
              className={`glassbox-ring-progress ${ringClass}`}
              stroke="currentColor"
              fill="none"
              strokeDasharray={ringMetrics.circumference}
              strokeDashoffset={dashOffset}
              strokeLinecap="round"
            />
          </svg>
          <div className="glassbox-ring-center" />
          {loading ? <div className="glassbox-spinner" aria-hidden="true" /> : null}
        </div>
        <div>
          <div className="glassbox-score">
            {confidence ? confidence.label : "n/a"}
          </div>
          <div className="glassbox-score-label">Confidence</div>
          {confidence ? (
            <div className="glassbox-score-raw">Raw: {confidence.rawLabel}</div>
          ) : (
            <div className="glassbox-score-raw">No confidence metric reported.</div>
          )}
        </div>
      </div>

      {trace?.trace_id ? (
        <div className="glassbox-trace">
          Trace ID: <span className="glassbox-trace-id">{trace.trace_id}</span>
        </div>
      ) : null}
      {trace?.request_id ? (
        <div className="glassbox-trace">
          Request ID: <span className="glassbox-trace-id">{trace.request_id}</span>
        </div>
      ) : null}
    </div>
  );
}

type GlassboxInlineProps = {
  trace?: Trace | null;
  loading?: boolean;
  compact?: boolean;
};

export function GlassboxInline({ trace, loading, compact = false }: GlassboxInlineProps) {
  const confidence = loading ? normalizeConfidence(0) : normalizeConfidence(trace?.confidence);
  const ringClass = resolveRingClass(confidence?.percent ?? null);
  const ringSize = compact ? 34 : INLINE_RING_SIZE;
  const ringStroke = compact ? 3.5 : INLINE_RING_STROKE;
  const ringMetrics = createRingMetrics(ringSize, ringStroke);
  const dashOffset = confidence
    ? ringMetrics.circumference * (1 - confidence.percent / 100)
    : ringMetrics.circumference;

  return (
    <div
      className={compact ? "glassbox-inline glassbox-inline--compact" : "glassbox-inline"}
      aria-label="Confidence"
    >
      <div className="glassbox-inline-ring" style={{ width: ringSize, height: ringSize }}>
        <svg
          className="glassbox-inline-ring-svg"
          viewBox={`0 0 ${ringSize} ${ringSize}`}
          aria-hidden="true"
        >
          <circle
            cx={ringSize / 2}
            cy={ringSize / 2}
            r={ringMetrics.radius}
            strokeWidth={ringStroke}
            className="glassbox-ring-track"
            stroke="currentColor"
            fill="none"
          />
          <circle
            cx={ringSize / 2}
            cy={ringSize / 2}
            r={ringMetrics.radius}
            strokeWidth={ringStroke}
            className={`glassbox-ring-progress ${ringClass}`}
            stroke="currentColor"
            fill="none"
            strokeDasharray={ringMetrics.circumference}
            strokeDashoffset={dashOffset}
            strokeLinecap="round"
          />
        </svg>
        <div className="glassbox-ring-center glassbox-ring-center--compact" />
        {loading ? <div className="glassbox-spinner glassbox-spinner--compact" aria-hidden="true" /> : null}
      </div>
      <div className="glassbox-inline-meta">
        <div className={compact ? "glassbox-inline-score glassbox-inline-score--compact" : "glassbox-inline-score"}>
          {confidence ? confidence.label : "n/a"}
        </div>
        <div className={compact ? "glassbox-inline-label glassbox-inline-label--compact" : "glassbox-inline-label"}>
          Confidence
        </div>
      </div>
    </div>
  );
}

