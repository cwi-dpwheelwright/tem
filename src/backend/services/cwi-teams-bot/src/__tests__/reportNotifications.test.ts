import { describe, expect, it, vi } from "vitest";

const loadNotifier = async (env: Record<string, string | undefined>) => {
  const original = { ...process.env };
  const mergedEnv = { GATEWAY_BASE_URL: "https://gateway.test", ...env };
  for (const [key, value] of Object.entries(mergedEnv)) {
    if (value === undefined) {
      delete process.env[key];
    } else {
      process.env[key] = value;
    }
  }

  vi.resetModules();
  const module = await import("../services/reportNotifications.js");
  const notifier = module.notifyReport;

  for (const key of Object.keys(process.env)) {
    if (!(key in original)) {
      delete process.env[key];
    }
  }
  Object.assign(process.env, original);
  return notifier;
};

describe("report notifications", () => {
  it("dedupes identical events", async () => {
    const notifyReport = await loadNotifier({
      REPORT_WEBHOOK_URL: "http://example.com",
      REPORT_DEDUPE_TTL_SECONDS: "900",
      REPORT_THROTTLE_WINDOW_SECONDS: "0",
    });

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      text: async () => "",
    });
    (globalThis as typeof globalThis & { fetch?: typeof fetch }).fetch = fetchMock;

    const payload = { event_type: "report_ready", tenant_id: "t1", report_id: "r1" };
    const first = await notifyReport(payload);
    const second = await notifyReport(payload);

    expect(first.sent).toBe(true);
    expect(second).toEqual({ sent: false, reason: "duplicate" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("throttles by tenant", async () => {
    const notifyReport = await loadNotifier({
      REPORT_WEBHOOK_URL: "http://example.com",
      REPORT_DEDUPE_TTL_SECONDS: "0",
      REPORT_THROTTLE_WINDOW_SECONDS: "300",
    });

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      text: async () => "",
    });
    (globalThis as typeof globalThis & { fetch?: typeof fetch }).fetch = fetchMock;

    const first = await notifyReport({ event_type: "report_ready", tenant_id: "t1", report_id: "r1" });
    const second = await notifyReport({ event_type: "document_ready", tenant_id: "t1", document_id: "d1" });

    expect(first.sent).toBe(true);
    expect(second).toEqual({ sent: false, reason: "throttled" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("returns missing_webhook when not configured", async () => {
    const notifyReport = await loadNotifier({
      REPORT_WEBHOOK_URL: undefined,
      REPORT_DEDUPE_TTL_SECONDS: "0",
      REPORT_THROTTLE_WINDOW_SECONDS: "0",
    });

    const fetchMock = vi.fn();
    (globalThis as typeof globalThis & { fetch?: typeof fetch }).fetch = fetchMock;

    const result = await notifyReport({ event_type: "report_ready", tenant_id: "t1", report_id: "r1" });
    expect(result).toEqual({ sent: false, reason: "missing_webhook" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
