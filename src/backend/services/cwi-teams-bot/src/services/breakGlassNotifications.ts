import { config } from "../config.js";
import { recordBreakGlassNotification } from "../observability/metrics.js";

type BreakGlassNotification = {
  event_type: string;
  token_id: string;
  tenant_id: string;
  user_id: string;
  role: string;
  scope: string;
  occurred_utc: string;
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
  const ttlMs = config.breakGlassDedupeTtlSeconds * 1000;
  dedupeCache.set(key, { expiresAt: nowMs() + ttlMs });
}

function isThrottled(tenantId: string): boolean {
  const windowMs = config.breakGlassThrottleWindowSeconds * 1000;
  const last = throttleCache.get(tenantId) ?? 0;
  if (nowMs() - last < windowMs) {
    return true;
  }
  throttleCache.set(tenantId, nowMs());
  return false;
}

export async function notifyBreakGlass(
  payload: BreakGlassNotification
): Promise<{ sent: boolean; reason?: string }> {
  const key = `${payload.event_type}:${payload.token_id}`;
  if (isDuplicate(key)) {
    return { sent: false, reason: "duplicate" };
  }

  if (isThrottled(payload.tenant_id)) {
    return { sent: false, reason: "throttled" };
  }

  if (!config.breakGlassWebhookUrl) {
    return { sent: false, reason: "missing_webhook" };
  }

  const card = {
    type: "message",
    attachments: [
      {
        contentType: "application/vnd.microsoft.card.adaptive",
        content: {
          type: "AdaptiveCard",
          version: "1.5",
          body: [
            { type: "TextBlock", text: "Break-glass event", weight: "Bolder" },
            {
              type: "FactSet",
              facts: [
                { title: "Event", value: payload.event_type },
                { title: "Tenant", value: payload.tenant_id },
                { title: "User", value: payload.user_id },
                { title: "Role", value: payload.role },
                { title: "Scope", value: payload.scope },
                { title: "Token", value: payload.token_id },
                { title: "Occurred", value: payload.occurred_utc },
              ],
            },
          ],
        },
      },
    ],
  };

  const response = await fetch(config.breakGlassWebhookUrl, {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify(card),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Break-glass notify failed: ${response.status} ${body}`);
  }

  markDuplicate(key);
  recordBreakGlassNotification(payload.event_type);
  return { sent: true };
}
