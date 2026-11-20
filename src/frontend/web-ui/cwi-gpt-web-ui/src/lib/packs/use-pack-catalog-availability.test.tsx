import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AuthMode } from "@/lib/config";
import { usePackCatalogAvailability } from "@/lib/packs/use-pack-catalog-availability";

const resolveAuthTokenMock = vi.fn();
const fetchDependencyVerifiedPublicPacksMock = vi.fn();
const fetchPackAvailabilitySummaryMock = vi.fn();

vi.mock("@/lib/auth", () => ({
  resolveAuthToken: (...args: unknown[]) => resolveAuthTokenMock(...args),
}));

vi.mock("@/lib/packs/client", () => ({
  fetchDependencyVerifiedPublicPacks: (...args: unknown[]) =>
    fetchDependencyVerifiedPublicPacksMock(...args),
  fetchPackAvailabilitySummary: (...args: unknown[]) =>
    fetchPackAvailabilitySummaryMock(...args),
}));

function TestHarness(props: {
  authMode?: AuthMode;
  authIdleTimeoutMs?: number;
  requestTimeoutMs?: number;
  onMissingAuth?: () => void;
}) {
  const { status, isEmpty, emptyStateSummary } = usePackCatalogAvailability({
    authMode: props.authMode ?? "token",
    authIdleTimeoutMs: props.authIdleTimeoutMs ?? 1000,
    requestTimeoutMs: props.requestTimeoutMs ?? 1000,
    onMissingAuth: props.onMissingAuth,
  });

  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="empty">{String(isEmpty)}</span>
      <span data-testid="summary-status">{emptyStateSummary?.status ?? "none"}</span>
      <span data-testid="summary-reason-count">{String(emptyStateSummary?.reasons.length ?? 0)}</span>
    </div>
  );
}

describe("usePackCatalogAvailability", () => {
  beforeEach(() => {
    resolveAuthTokenMock.mockReset();
    fetchDependencyVerifiedPublicPacksMock.mockReset();
    fetchPackAvailabilitySummaryMock.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
    cleanup();
  });

  it("marks the catalog empty when pack-service availability reports no valid packs", async () => {
    resolveAuthTokenMock.mockResolvedValue("token");
    fetchPackAvailabilitySummaryMock.mockResolvedValue({
      status: "empty",
      available_pack_count: 0,
      filtered_pack_count: 2,
      unindexed_pack_count: 1,
      indexed_pack_count: 3,
      discovered_pack_count: 4,
      generated_utc: "2026-04-15T00:00:00Z",
      reasons: [{ code: "missing_dependencies", message: "Dependencies missing.", count: 2 }],
    });

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId("status").textContent).toBe("empty"));

    expect(screen.getByTestId("empty").textContent).toBe("true");
    expect(screen.getByTestId("summary-status").textContent).toBe("empty");
    expect(screen.getByTestId("summary-reason-count").textContent).toBe("1");
  });

  it("marks the catalog available when pack-service availability reports valid packs", async () => {
    resolveAuthTokenMock.mockResolvedValue("token");
    fetchPackAvailabilitySummaryMock.mockResolvedValue({
      status: "available",
      available_pack_count: 1,
      filtered_pack_count: 2,
      unindexed_pack_count: 1,
      indexed_pack_count: 3,
      discovered_pack_count: 4,
      generated_utc: "2026-04-15T00:00:00Z",
      reasons: [{ code: "missing_dependencies", message: "Dependencies missing.", count: 2 }],
    });

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId("status").textContent).toBe("available"));

    expect(screen.getByTestId("empty").textContent).toBe("false");
    expect(screen.getByTestId("summary-status").textContent).toBe("none");
  });

  it("falls back to client-side verification when the availability endpoint is unavailable", async () => {
    resolveAuthTokenMock.mockResolvedValue("token");
    fetchPackAvailabilitySummaryMock.mockRejectedValue(new Error("404"));
    fetchDependencyVerifiedPublicPacksMock.mockResolvedValue({
      packs: [],
      filteredOutPacks: ["broken-pack", "missing-pack"],
    });

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId("status").textContent).toBe("empty"));

    expect(screen.getByTestId("empty").textContent).toBe("true");
    expect(screen.getByTestId("summary-status").textContent).toBe("none");
  });

  it("does not block on fetch errors", async () => {
    resolveAuthTokenMock.mockResolvedValue("token");
    fetchPackAvailabilitySummaryMock.mockRejectedValue(new Error("network"));
    fetchDependencyVerifiedPublicPacksMock.mockRejectedValue(new Error("network"));

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId("status").textContent).toBe("error"));

    expect(screen.getByTestId("empty").textContent).toBe("false");
  });

  it("invokes missing-auth handling instead of showing the empty-state modal", async () => {
    const onMissingAuth = vi.fn();
    resolveAuthTokenMock.mockResolvedValue(null);

    render(<TestHarness onMissingAuth={onMissingAuth} />);

    await act(async () => {
      await Promise.resolve();
    });

    expect(onMissingAuth).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("status").textContent).toBe("error");
  });
});
