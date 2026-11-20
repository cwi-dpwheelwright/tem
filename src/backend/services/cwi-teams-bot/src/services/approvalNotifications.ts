import { config } from "../config.js";
import { recordApprovalNotification } from "../observability/metrics.js";
import { buildApprovalCard } from "./approvalService.js";

export type ApprovalNotification = {
  ticket_id: string;
  status: string;
  risk: string;
  reason: string;
  tenant_id?: string;
  ticket_type?: string;
  trace_id?: string;
  trace_url?: string;
  details?: Array<{ title: string; value: string }>;
};

type DedupeEntry = { expiresAt: number };

const dedupeCache = new Map<string, DedupeEntry>();

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
  const ttlMs = config.approvalDedupeTtlSeconds * 1000;
  dedupeCache.set(key, { expiresAt: nowMs() + ttlMs });
}

function buildTeamsPayload(payload: ApprovalNotification) {
  const card = buildApprovalCard({
    ticketId: payload.ticket_id,
    risk: payload.risk,
    reason: payload.reason,
    ticketType: payload.ticket_type,
    traceId: payload.trace_id,
    traceUrl: payload.trace_url,
    details: payload.details,
  });

  return {
    type: "message",
    attachments: [
      {
        contentType: "application/vnd.microsoft.card.adaptive",
        content: card,
      },
    ],
  };
}

export async function notifyApproval(
  payload: ApprovalNotification
): Promise<{ sent: boolean; reason?: string }> {
  if (!config.approvalWebhookUrl) {
    return { sent: false, reason: "missing_webhook" };
  }

  const key = `${payload.ticket_id}:${payload.status}`;
  if (isDuplicate(key)) {
    return { sent: false, reason: "duplicate" };
  }

  const response = await fetch(config.approvalWebhookUrl, {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify(buildTeamsPayload(payload)),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Approval notify failed: ${response.status} ${body}`);
  }

  markDuplicate(key);
  recordApprovalNotification(payload.status);
  return { sent: true };
}
