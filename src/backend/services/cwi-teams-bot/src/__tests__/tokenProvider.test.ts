import { afterEach, describe, expect, it, vi } from "vitest";

const loadTokenProvider = async (env: Record<string, string | undefined>) => {
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
  const module = await import("../auth/tokenProvider.js");

  for (const key of Object.keys(process.env)) {
    if (!(key in original)) {
      delete process.env[key];
    }
  }
  Object.assign(process.env, original);
  return module;
};

describe("gateway token provider", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("returns static token without fetching", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_STATIC_TOKEN: "static-token",
      GATEWAY_OAUTH_TOKEN_URL: "https://token.example",
    });

    const token = await getGatewayToken();
    expect(token).toBe("static-token");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returns empty string when no oauth url configured", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_STATIC_TOKEN: "",
      GATEWAY_OAUTH_TOKEN_URL: "",
    });

    const token = await getGatewayToken();
    expect(token).toBe("");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("fetches and caches oauth token", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-02-01T00:00:00Z"));

    const fetchMock = vi.fn(async () => ({
      ok: true,
      json: async () => ({ access_token: "oauth-token", expires_in: 120 }),
    }));
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_OAUTH_TOKEN_URL: "https://token.example",
      GATEWAY_OAUTH_CLIENT_ID: "client",
      GATEWAY_OAUTH_CLIENT_SECRET: "secret",
      GATEWAY_OAUTH_SCOPE: "scope",
    });

    const first = await getGatewayToken();
    const second = await getGatewayToken();

    expect(first).toBe("oauth-token");
    expect(second).toBe("oauth-token");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("refreshes oauth token when near expiry", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: "token-1", expires_in: 1 }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: "token-2", expires_in: 3600 }),
      });
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_OAUTH_TOKEN_URL: "https://token.example",
    });

    const first = await getGatewayToken();
    const second = await getGatewayToken();

    expect(first).toBe("token-1");
    expect(second).toBe("token-2");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("throws when token request fails", async () => {
    const fetchMock = vi.fn(async () => ({ ok: false, status: 500 }));
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_OAUTH_TOKEN_URL: "https://token.example",
    });

    await expect(getGatewayToken()).rejects.toThrow("Token request failed");
  });

  it("throws when access_token is missing", async () => {
    const fetchMock = vi.fn(async () => ({
      ok: true,
      json: async () => ({}),
    }));
    vi.stubGlobal("fetch", fetchMock);

    const { getGatewayToken } = await loadTokenProvider({
      GATEWAY_OAUTH_TOKEN_URL: "https://token.example",
    });

    await expect(getGatewayToken()).rejects.toThrow(
      "Token response missing access_token."
    );
  });
});
