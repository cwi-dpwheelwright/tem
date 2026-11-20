"use client";

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import BreakGlassPanel from "@/components/break-glass-panel";

const issueMock = vi.fn();
const revokeMock = vi.fn();
const setTokenMock = vi.fn();
const clearTokenMock = vi.fn();

let breakGlassState = {
  state: null as { tokenId: string; expiresUtc: string } | null,
  isActive: false,
  isExpired: false,
};

let runtimeConfig = { config: { apiBaseUrl: "http://api.local" } } as {
  config: { apiBaseUrl: string } | null;
};

vi.mock("@/lib/api/client", () => ({
  createApiClient: () => ({
    breakGlassIssue: issueMock,
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
      state: breakGlassState.state,
      isActive: breakGlassState.isActive,
      isExpired: breakGlassState.isExpired,
      setToken: setTokenMock,
      clearToken: clearTokenMock,
    }),
  };
});

describe("BreakGlassPanel", () => {
  beforeEach(() => {
    issueMock.mockReset();
    revokeMock.mockReset();
    setTokenMock.mockReset();
    clearTokenMock.mockReset();
    breakGlassState = {
      state: null,
      isActive: false,
      isExpired: false,
    };
    runtimeConfig = { config: { apiBaseUrl: "http://api.local" } };
  });
  afterEach(() => {
    cleanup();
  });

  it("issues a break-glass token when inputs are valid", async () => {
    issueMock.mockResolvedValue({
      token_id: "token-123",
      expires_utc: "2026-02-02T00:00:00Z",
    });

    render(<BreakGlassPanel />);
    fireEvent.click(screen.getByText("Request access"));

    fireEvent.change(screen.getByPlaceholderText("tenant_id"), {
      target: { value: "tenant-1" },
    });
    fireEvent.change(screen.getByPlaceholderText("user_id"), {
      target: { value: "user-1" },
    });
    fireEvent.change(screen.getByPlaceholderText("role"), {
      target: { value: "admin" },
    });
    fireEvent.change(screen.getByPlaceholderText("scope"), {
      target: { value: "admin" },
    });
    fireEvent.change(screen.getByPlaceholderText("duration_minutes"), {
      target: { value: "30" },
    });
    fireEvent.change(screen.getByPlaceholderText("reason (required)"), {
      target: { value: "incident" },
    });

    fireEvent.click(screen.getByText("Issue token"));

    await waitFor(() => {
      expect(issueMock).toHaveBeenCalledTimes(1);
      expect(setTokenMock).toHaveBeenCalledWith(
        "token-123",
        "2026-02-02T00:00:00Z",
        expect.any(String),
      );
      expect(screen.getByText("Break-glass token issued.")).toBeTruthy();
    });
  });

  it("disables issue action when required fields are missing", () => {
    render(<BreakGlassPanel />);
    fireEvent.click(screen.getByText("Request access"));

    expect(screen.getByText("Fill in all required fields. Reason is mandatory by policy.")).toBeTruthy();
    const issueButton = screen.getByText("Issue token");
    expect(issueButton).toBeTruthy();
    expect(issueButton).toHaveProperty("disabled", true);
  });

  it("disables revoke and shows expired warning when token is expired", () => {
    breakGlassState = {
      state: { tokenId: "token-1", expiresUtc: "2026-02-01T00:00:00Z" },
      isActive: false,
      isExpired: true,
    };

    render(<BreakGlassPanel />);

    expect(screen.getByText("Token expired. Actions are disabled.")).toBeTruthy();
    const revokeButton = screen.getByText("Revoke active token");
    expect(revokeButton).toBeTruthy();
    expect(revokeButton).toHaveProperty("disabled", true);
  });

  it("shows configuration error when API base URL is missing", async () => {
    runtimeConfig = { config: null };

    render(<BreakGlassPanel />);
    fireEvent.click(screen.getByText("Request access"));

    fireEvent.change(screen.getByPlaceholderText("tenant_id"), {
      target: { value: "tenant-1" },
    });
    fireEvent.change(screen.getByPlaceholderText("user_id"), {
      target: { value: "user-1" },
    });
    fireEvent.change(screen.getByPlaceholderText("role"), {
      target: { value: "admin" },
    });
    fireEvent.change(screen.getByPlaceholderText("scope"), {
      target: { value: "admin" },
    });
    fireEvent.change(screen.getByPlaceholderText("duration_minutes"), {
      target: { value: "30" },
    });
    fireEvent.change(screen.getByPlaceholderText("reason (required)"), {
      target: { value: "incident" },
    });

    fireEvent.click(screen.getByText("Issue token"));

    await waitFor(() => {
      expect(screen.getByText("Missing API configuration.")).toBeTruthy();
    });
  });
});
