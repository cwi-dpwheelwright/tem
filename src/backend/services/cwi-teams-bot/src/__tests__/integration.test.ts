import { describe, expect, it } from "vitest";

const baseUrl = process.env.TEAMS_BOT_BASE_URL;

describe("integration (optional)", () => {
  it("health responds", async () => {
    if (!baseUrl) {
      return;
    }
    const response = await fetch(`${baseUrl}/health`);
    expect([200, 503]).toContain(response.status);
  });

  it("metrics responds", async () => {
    if (!baseUrl) {
      return;
    }
    const response = await fetch(`${baseUrl}/metrics`);
    expect(response.status).toBe(200);
  });
});
