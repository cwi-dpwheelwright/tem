"use client";

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import BreakGlassBanner from "@/components/break-glass-banner";

const revokeMock = vi.fn();
const clearTokenMock = vi.fn();

const breakGlassState = {
  tokenId: "token-abc123",
  expiresUtc: "2026-02-02T00:00:00Z",
};

let runtimeConfig = { config: { apiBaseUrl: "http://api.local" } } as {
  config: { apiBaseUrl: string } | null;
};

vi.mock("@/lib/api/client", () => ({
  createApiClient: () => ({
    breakGlassRevoke: revokeMock,
  }),
}));

vi.mock("@/lib/auth/context", () => ({
  useAuth: () => ({ accessToken: "token-1" }),
}));

vi.mock("@/lib/config", () => ({
  getRuntimeConfigStatus: () => runtimeConfig,
}));

vi.mock("@/lib/telemetry", () => ({
  createTelemetryReporter: () => ({ report: vi.fn() }),
}));

vi.mock("@/lib/break-glass/context", async () => {
  const actual = await vi.importActual<typeof import("@/lib/break-glass/context")>(
    "@/lib/break-glass/context",
  );
  return {
    ...actual,
    useBreakGlass: () => ({
      state: breakGlassState,
      isActive: true,
      isExpired: false,
      expiresAt: new Date("2026-02-02T00:00:00Z"),
      clearToken: clearTokenMock,
    }),
  };
});

describe("BreakGlassBanner", () => {
  beforeEach(() => {
    revokeMock.mockReset();
    clearTokenMock.mockReset();
    runtimeConfig = { config: { apiBaseUrl: "http://api.local" } };
  });
  afterEach(() => {
    cleanup();
  });

  it("renders active banner and revokes token", async () => {
    revokeMock.mockResolvedValue({});

    render(<BreakGlassBanner />);

    expect(screen.getByText("Break-glass active")).toBeTruthy();
    fireEvent.click(screen.getByText("Revoke access"));

    await waitFor(() => {
      expect(revokeMock).toHaveBeenCalledTimes(1);
      expect(clearTokenMock).toHaveBeenCalledTimes(1);
      expect(screen.getByText("Break-glass revoked.")).toBeTruthy();
    });
  });
});
