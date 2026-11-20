import { config } from "./config.js";

export type EventEnvelope = {
  event_id?: string;
  timestamp_utc?: string;
  service?: string;
  environment?: string;
  type: string;
  polarity: "positive" | "negative";
  severity: "info" | "warn" | "error";
  message: string;
  tenant_id?: string;
  trace_id?: string;
  data?: Record<string, unknown>;
};

export async function publishEvent(event: EventEnvelope): Promise<void> {
  if (!config.eventBusEnabled || !config.eventBusUrl) {
    return;
  }

  const payload: EventEnvelope = {
    event_id: event.event_id ?? crypto.randomUUID(),
    timestamp_utc: event.timestamp_utc ?? new Date().toISOString(),
    service: event.service ?? config.eventBusServiceName,
    environment: event.environment ?? config.eventBusEnv,
    type: event.type,
    polarity: event.polarity,
    severity: event.severity,
    message: event.message,
    tenant_id: event.tenant_id,
    trace_id: event.trace_id,
    data: event.data ?? {},
  };

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 1500);
  try {
    await fetch(config.eventBusUrl, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(config.eventBusToken ? { Authorization: `Bearer ${config.eventBusToken}` } : {}),
      },
      body: JSON.stringify(payload),
      signal: controller.signal,
    });
  } catch {
    // best-effort
  } finally {
    clearTimeout(timer);
  }
}
