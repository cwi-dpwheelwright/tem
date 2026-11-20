"use client";

import { describe, expect, it, beforeEach, afterEach, vi } from "vitest";
import { cleanup, render, screen, act } from "@testing-library/react";
import { BreakGlassProvider, useBreakGlass, formatCountdown } from "@/lib/break-glass/context";

function BreakGlassStatus() {
  const { isActive, isExpired, state } = useBreakGlass();
  return (
    <div>
      <span>{isActive ? "active" : "inactive"}</span>
      <span>{isExpired ? "expired" : "valid"}</span>
      <span>{state?.tokenId ?? "none"}</span>
    </div>
  );
}

describe("break-glass context", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    localStorage.clear();
  });

  it("expires tokens and clears storage after expiry", async () => {
    vi.useFakeTimers();
    const now = new Date("2026-02-01T00:00:00Z");
    vi.setSystemTime(now);
    localStorage.setItem(
      "cwi.break_glass",
      JSON.stringify({
        tokenId: "token-1",
        expiresUtc: "2026-02-01T00:00:05Z",
        issuedUtc: "2026-02-01T00:00:00Z",
      }),
    );

    render(
      <BreakGlassProvider>
        <BreakGlassStatus />
      </BreakGlassProvider>,
    );

    expect(screen.getByText("active")).toBeTruthy();
    expect(screen.getByText("valid")).toBeTruthy();
    expect(screen.getByText("token-1")).toBeTruthy();

    await act(async () => {
      vi.advanceTimersByTime(6000);
    });

    expect(screen.getByText("inactive")).toBeTruthy();
    expect(screen.getByText("none")).toBeTruthy();
    expect(localStorage.getItem("cwi.break_glass")).toBeNull();
  });

  it("formats countdown strings", () => {
    const expires = new Date("2026-02-01T00:01:05Z");
    const now = new Date("2026-02-01T00:00:00Z");
    expect(formatCountdown(expires, now)).toBe("01:05");
  });
});
