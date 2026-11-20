import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { resolveCurrentSessionIdentity, resolveSessionIdentity } from "@/lib/session-identity";

function buildToken(payload: Record<string, unknown>): string {
  const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `header.${encodedPayload}.signature`;
}

describe("session-identity", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("prefers authenticated token claims over stale device overrides", () => {
    const identity = resolveSessionIdentity({
      authMode: "token",
      tenantId: "tenant-device",
      runtimeUserId: "stale-device-user",
      token: buildToken({
        tenant_id: "tenant-token",
        sub: "user-token",
      }),
    });

    expect(identity.tenantId).toBe("tenant-token");
    expect(identity.userId).toBe("user-token");
    expect(identity.storageScope).toBe("tenant:tenant-token:user:user-token");
  });

  it("falls back to anonymous scope only when auth is disabled", () => {
    const identity = resolveSessionIdentity({
      authMode: "none",
      tenantId: null,
      runtimeUserId: null,
      token: null,
    });

    expect(identity.tenantId).toBe("public");
    expect(identity.userId).toBe("anonymous");
  });

  it("builds the current scope from stored auth state", () => {
    window.localStorage.setItem(
      "cwi.user.web.overrides",
      JSON.stringify({ authMode: "token", tenantId: "tenant-1", userId: "stale-device-user" }),
    );
    window.sessionStorage.setItem(
      "cwi.user.web.token.access",
      buildToken({ tenant_id: "tenant-1", sub: "user-1" }),
    );

    const identity = resolveCurrentSessionIdentity();

    expect(identity.tenantId).toBe("tenant-1");
    expect(identity.userId).toBe("user-1");
    expect(identity.storageScope).toBe("tenant:tenant-1:user:user-1");
  });
});
