"use client";

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import BreakGlassAudit from "@/components/break-glass-audit";

const auditMock = vi.fn();

let runtimeConfig = { config: { apiBaseUrl: "http://api.local" } } as {
  config: { apiBaseUrl: string } | null;
};

vi.mock("@/lib/api/client", () => ({
  createApiClient: () => ({
    breakGlassAudit: auditMock,
  }),
}));

vi.mock("@/lib/auth/context", () => ({
  useAuth: () => ({ accessToken: "token-1" }),
}));

vi.mock("@/lib/config", () => ({
  getRuntimeConfigStatus: () => runtimeConfig,
}));

describe("BreakGlassAudit", () => {
  beforeEach(() => {
    auditMock.mockReset();
    runtimeConfig = { config: { apiBaseUrl: "http://api.local" } };
  });
  afterEach(() => {
    cleanup();
  });

  it("loads audit results and renders items", async () => {
    auditMock.mockResolvedValue({
      page: 1,
      page_size: 20,
      total: 1,
      items: [
        {
          token_id: "token-1",
          tenant_id: "tenant-1",
          user_id: "user-1",
          role: "admin",
          scope: "all",
          created_utc: "2026-02-01T00:00:00Z",
          expires_utc: "2026-02-02T00:00:00Z",
          revoked_utc: null,
          reason: "incident",
        },
      ],
    });

    render(<BreakGlassAudit />);
    fireEvent.change(screen.getByPlaceholderText("tenant_id"), {
      target: { value: "tenant-1" },
    });

    fireEvent.click(screen.getByText("Load audit"));

    await waitFor(() => {
      expect(auditMock).toHaveBeenCalledWith("tenant-1", 1, 20);
      expect(screen.getByText("token-1")).toBeTruthy();
      expect(screen.getByText("Total events: 1")).toBeTruthy();
    });
  });

  it("exports audit JSON", async () => {
    auditMock.mockResolvedValue({
      page: 1,
      page_size: 20,
      total: 1,
      items: [
        {
          token_id: "token-1",
          tenant_id: "tenant-1",
          user_id: "user-1",
          role: "admin",
          scope: "all",
          created_utc: "2026-02-01T00:00:00Z",
          expires_utc: "2026-02-02T00:00:00Z",
          revoked_utc: null,
          reason: "incident",
        },
      ],
    });

    const urlMock = vi.fn(() => "blob:mock");
    const revokeMock = vi.fn();
    const clickMock = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
    vi.stubGlobal("URL", {
      createObjectURL: urlMock,
      revokeObjectURL: revokeMock,
    });

    render(<BreakGlassAudit />);
    fireEvent.change(screen.getByPlaceholderText("tenant_id"), {
      target: { value: "tenant-1" },
    });
    fireEvent.click(screen.getByText("Load audit"));

    await waitFor(() => {
      expect(screen.getByText("Export JSON")).toBeTruthy();
    });

    fireEvent.click(screen.getByText("Export JSON"));
    expect(urlMock).toHaveBeenCalledTimes(1);
    expect(revokeMock).toHaveBeenCalledTimes(1);
    clickMock.mockRestore();
  });
});
