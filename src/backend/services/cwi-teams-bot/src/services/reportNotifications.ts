import { config } from "../config.js";
import { recordReportNotification } from "../observability/metrics.js";

export type ReportNotification = {
  event_type: string;
  tenant_id: string;
  report_id?: string;
  document_id?: string;
  title?: string;
  summary?: string;
  artifact_url?: string;
  trace_id?: string;
  trace_url?: string;
  occurred_utc?: string;
};

type DedupeEntry = { expiresAt: number };

const dedupeCache = new Map<string, DedupeEntry>();
const throttleCache = new Map<string, number>();

function nowMs() {
  return Date.now();
}

function isDuplicate(key: string): boolean {
  const entry = dedupeCache.get(key);
  if (!entry) {
    return false;
  }
  if (entry.expiresAt <= nowMs()) {
    dedupeCache.delete(key);
    return false;
  }
  return true;
}

function markDuplicate(key: string) {
  const ttlMs = config.reportDedupeTtlSeconds * 1000;
  dedupeCache.set(key, { expiresAt: nowMs() + ttlMs });
}

function isThrottled(tenantId: string): boolean {
  const windowMs = config.reportThrottleWindowSeconds * 1000;
  const last = throttleCache.get(tenantId) ?? 0;
  if (nowMs() - last < windowMs) {
    return true;
  }
  throttleCache.set(tenantId, nowMs());
  return false;
}

function buildReportCard(payload: ReportNotification) {
  const title = payload.event_type.replace(/_/g, " ");
  const facts = [
    { title: "Event", value: payload.event_type },
    { title: "Tenant", value: payload.tenant_id },
  ];

  if (payload.report_id) {
    facts.push({ title: "Report", value: payload.report_id });
  }
  if (payload.document_id) {
    facts.push({ title: "Document", value: payload.document_id });
  }
  if (payload.trace_id) {
    facts.push({ title: "Trace", value: payload.trace_id });
  }
  if (payload.occurred_utc) {
    facts.push({ title: "Occurred", value: payload.occurred_utc });
  }

  const body: Array<Record<string, unknown>> = [
    { type: "TextBlock", text: title, weight: "Bolder", size: "Medium" },
    { type: "FactSet", facts },
  ];

  if (payload.title) {
    body.splice(1, 0, { type: "TextBlock", text: payload.title, wrap: true });
  }
  if (payload.summary) {
    body.push({ type: "TextBlock", text: payload.summary, wrap: true });
  }

  const actions: Array<Record<string, unknown>> = [];
  if (payload.artifact_url) {
    actions.push({ type: "Action.OpenUrl", title: "Open artifact", url: payload.artifact_url });
  }
  if (payload.trace_url) {
    actions.push({ type: "Action.OpenUrl", title: "View trace", url: payload.trace_url });
  }

  return {
    type: "message",
    attachments: [
      {
        contentType: "application/vnd.microsoft.card.adaptive",
        content: {
          type: "AdaptiveCard",
          version: "1.5",
          body,
          actions,
        },
      },
    ],
  };
}

export async function notifyReport(
  payload: ReportNotification
): Promise<{ sent: boolean; reason?: string }> {
  const key = `${payload.event_type}:${payload.report_id ?? payload.document_id ?? "unknown"}`;
  if (isDuplicate(key)) {
    return { sent: false, reason: "duplicate" };
  }

  if (isThrottled(payload.tenant_id)) {
    return { sent: false, reason: "throttled" };
  }

  if (!config.reportWebhookUrl) {
    return { sent: false, reason: "missing_webhook" };
  }

  const response = await fetch(config.reportWebhookUrl, {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify(buildReportCard(payload)),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Report notify failed: ${response.status} ${body}`);
  }

  markDuplicate(key);
  recordReportNotification(payload.event_type);
  return { sent: true };
}
