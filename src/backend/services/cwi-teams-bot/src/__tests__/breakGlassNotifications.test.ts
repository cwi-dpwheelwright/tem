import { describe, expect, it, vi } from "vitest";

type ConfigOverride = {
  breakGlassWebhookUrl: string;
  breakGlassDedupeTtlSeconds: number;
  breakGlassThrottleWindowSeconds: number;
};

const basePayload = {
  event_type: "issue",
  token_id: "token-1",
  tenant_id: "tenant-1",
  user_id: "user-1",
  role: "admin",
  scope: "all",
  occurred_utc: "2026-02-01T00:00:00Z",
};

async function loadModule(configOverride: ConfigOverride) {
  vi.resetModules();
  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    text: async () => "",
  });
  vi.stubGlobal("fetch", fetchMock);

  const recordBreakGlassNotification = vi.fn();

  vi.doMock("../config.js", () => ({
    config: configOverride,
  }));
  vi.doMock("../observability/metrics.js", () => ({
    recordBreakGlassNotification,
  }));

  const mod = await import("../services/breakGlassNotifications.js");
  return { notifyBreakGlass: mod.notifyBreakGlass, fetchMock, recordBreakGlassNotification };
}

describe("notifyBreakGlass", () => {
  it("returns missing_webhook when not configured", async () => {
    const { notifyBreakGlass, fetchMock } = await loadModule({
      breakGlassWebhookUrl: "",
      breakGlassDedupeTtlSeconds: 600,
      breakGlassThrottleWindowSeconds: 300,
    });

    const result = await notifyBreakGlass(basePayload);
    expect(result).toEqual({ sent: false, reason: "missing_webhook" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("dedupes repeated events", async () => {
    const { notifyBreakGlass, fetchMock, recordBreakGlassNotification } = await loadModule(
      {
        breakGlassWebhookUrl: "https://example.com/webhook",
        breakGlassDedupeTtlSeconds: 600,
        breakGlassThrottleWindowSeconds: 0,
      }
    );

    const first = await notifyBreakGlass(basePayload);
    const second = await notifyBreakGlass(basePayload);

    expect(first).toEqual({ sent: true });
    expect(second).toEqual({ sent: false, reason: "duplicate" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(recordBreakGlassNotification).toHaveBeenCalledTimes(1);
  });

  it("throttles by tenant", async () => {
    const { notifyBreakGlass, fetchMock } = await loadModule({
      breakGlassWebhookUrl: "https://example.com/webhook",
      breakGlassDedupeTtlSeconds: 600,
      breakGlassThrottleWindowSeconds: 300,
    });

    const first = await notifyBreakGlass(basePayload);
    const second = await notifyBreakGlass({
      ...basePayload,
      token_id: "token-2",
    });

    expect(first).toEqual({ sent: true });
    expect(second).toEqual({ sent: false, reason: "throttled" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
