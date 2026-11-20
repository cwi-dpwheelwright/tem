import { act, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { shouldRefreshRemoteHistory, useRemoteHistoryRefresh } from "@/lib/history-sync";

function TestHarness(props: {
  enabled: boolean;
  isRemoteHistory: boolean;
  refresh: () => void | Promise<void>;
  intervalMs?: number;
}) {
  useRemoteHistoryRefresh(props);
  return null;
}

describe("history-sync", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "visible",
    });
    vi.useRealTimers();
  });

  it("refreshes remote history on an interval when visible", () => {
    const refresh = vi.fn();
    render(<TestHarness enabled isRemoteHistory refresh={refresh} intervalMs={5000} />);

    act(() => {
      vi.advanceTimersByTime(5000);
    });

    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("does not refresh while the document is hidden", () => {
    const refresh = vi.fn();
    render(<TestHarness enabled isRemoteHistory refresh={refresh} intervalMs={5000} />);

    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "hidden",
    });

    act(() => {
      vi.advanceTimersByTime(5000);
    });

    expect(refresh).not.toHaveBeenCalled();
  });

  it("refreshes when the document becomes visible again", () => {
    const refresh = vi.fn();
    render(<TestHarness enabled isRemoteHistory refresh={refresh} intervalMs={5000} />);

    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "hidden",
    });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });
    expect(refresh).not.toHaveBeenCalled();

    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "visible",
    });
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });

    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("only enables remote refresh for enabled shared history", () => {
    expect(shouldRefreshRemoteHistory({
      enabled: true,
      isRemoteHistory: true,
      visibilityState: "visible",
    })).toBe(true);

    expect(shouldRefreshRemoteHistory({
      enabled: false,
      isRemoteHistory: true,
      visibilityState: "visible",
    })).toBe(false);

    expect(shouldRefreshRemoteHistory({
      enabled: true,
      isRemoteHistory: false,
      visibilityState: "visible",
    })).toBe(false);
  });
});
