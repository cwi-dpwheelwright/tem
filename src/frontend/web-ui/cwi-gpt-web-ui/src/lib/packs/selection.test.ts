import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { loadSelectedPackName, saveSelectedPackName } from "@/lib/packs/selection";
import { saveOverrides } from "@/lib/storage";

function buildToken(payload: Record<string, unknown>): string {
  const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `header.${encodedPayload}.signature`;
}

function setSessionUser(userId: string, tenantId = "tenant-1"): void {
  window.sessionStorage.setItem(
    "cwi.user.web.token.access",
    buildToken({ tenant_id: tenantId, sub: userId }),
  );
}

describe("pack selection", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    saveOverrides({ authMode: "token", tenantId: "tenant-1", userId: "stale-device-user" });
  });

  afterEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it("keeps selected packs scoped to the authenticated user", () => {
    setSessionUser("user-a");
    saveSelectedPackName("Pack Alpha");
    expect(loadSelectedPackName()).toBe("Pack Alpha");

    setSessionUser("user-b");
    expect(loadSelectedPackName()).toBeNull();

    saveSelectedPackName("Pack Beta");
    expect(loadSelectedPackName()).toBe("Pack Beta");

    setSessionUser("user-a");
    expect(loadSelectedPackName()).toBe("Pack Alpha");
  });
});
