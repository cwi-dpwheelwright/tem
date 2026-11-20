import { describe, expect, it, vi } from "vitest";

const loadConfig = async (env: Record<string, string | undefined>) => {
  const original = { ...process.env };
  for (const [key, value] of Object.entries(env)) {
    if (value === undefined) {
      delete process.env[key];
    } else {
      process.env[key] = value;
    }
  }

  vi.resetModules();
  const module = await import("../config.js");
  const config = module.config;

  for (const key of Object.keys(process.env)) {
    if (!(key in original)) {
      delete process.env[key];
    }
  }
  Object.assign(process.env, original);
  return config;
};

describe("config", () => {
  it("uses default values when env is missing", async () => {
    const config = await loadConfig({
      PORT: undefined,
      ALLOW_UNAUTHENTICATED: undefined,
      IDEMPOTENCY_TTL_SECONDS: undefined,
      GATEWAY_BASE_URL: "https://gateway.test",
    });

    expect(config.port).toBe(3978);
    expect(config.allowUnauthenticated).toBe(false);
    expect(config.idempotencyTtlSeconds).toBe(86400);
  });

  it("parses booleans and numbers from env", async () => {
    const config = await loadConfig({
      PORT: "4000",
      ALLOW_UNAUTHENTICATED: "true",
      IDEMPOTENCY_TTL_SECONDS: "120",
      GATEWAY_BASE_URL: "https://gateway.test",
    });

    expect(config.port).toBe(4000);
    expect(config.allowUnauthenticated).toBe(true);
    expect(config.idempotencyTtlSeconds).toBe(120);
  });
});
