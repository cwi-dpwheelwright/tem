import { describe, expect, it, vi } from "vitest";

async function loadValidator() {
  vi.resetModules();
  process.env.GATEWAY_BASE_URL ||= "https://gateway.test";
  return import("../auth/botTokenValidator.js");
}

describe("bot token validation", () => {
  it("rejects disallowed tenant", async () => {
    process.env.BOT_TENANT_ALLOWLIST = "tenant-allow";
    process.env.BOT_TOKEN_ISSUER = "issuer";
    process.env.BOT_TOKEN_AUDIENCE = "audience";

    const { validateTokenClaims } = await loadValidator();
    const result = validateTokenClaims({
      tid: "tenant-deny",
      iss: "issuer",
      aud: "audience",
    });

    expect(result.ok).toBe(false);
  });

  it("accepts allowed tenant", async () => {
    process.env.BOT_TENANT_ALLOWLIST = "tenant-allow";
    process.env.BOT_TOKEN_ISSUER = "issuer";
    process.env.BOT_TOKEN_AUDIENCE = "audience";

    const { validateTokenClaims } = await loadValidator();
    const result = validateTokenClaims({
      tid: "tenant-allow",
      iss: "issuer",
      aud: "audience",
    });

    expect(result.ok).toBe(true);
  });
});
