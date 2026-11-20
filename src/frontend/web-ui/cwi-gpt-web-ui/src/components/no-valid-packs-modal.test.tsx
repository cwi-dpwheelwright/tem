import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { NoValidPacksModal } from "@/components/no-valid-packs-modal";

afterEach(() => {
  cleanup();
});

describe("NoValidPacksModal", () => {
  it("renders a non-dismissible blocker message with exact pack-service reasons", () => {
    render(
      <NoValidPacksModal
        availabilitySummary={{
          generated_utc: "2026-04-15T00:00:00Z",
          status: "empty",
          available_pack_count: 0,
          filtered_pack_count: 2,
          unindexed_pack_count: 1,
          indexed_pack_count: 3,
          discovered_pack_count: 4,
          reasons: [
            {
              code: "missing_dependencies",
              message: "2 indexed packs were filtered because required dependency packs are unavailable.",
              count: 2,
            },
            {
              code: "unindexed_packs",
              message: "1 pack exists on disk but is not indexed for serving.",
              count: 1,
            },
          ],
        }}
      />,
    );

    expect(
      screen.getByRole("dialog", { name: "No valid packs available" }),
    ).toBeTruthy();
    expect(screen.getByText(/pack service validation reported these exact availability problems/i)).toBeTruthy();
    expect(screen.getByText(/pack service found 3 indexed packs, 2 filtered packs, 1 unindexed pack, and 4 packs on disk/i)).toBeTruthy();
    expect(screen.getByText(/2 indexed packs were filtered because required dependency packs are unavailable/i)).toBeTruthy();
    expect(screen.getByText(/1 pack exists on disk but is not indexed for serving/i)).toBeTruthy();
    expect(screen.getByText(/please contact the site administrator/i)).toBeTruthy();
    expect(screen.getByText(/pack service currently reports 0 packs available, 2 filtered packs, and 1 unindexed pack/i)).toBeTruthy();
    expect(screen.queryByRole("button", { name: /close/i })).toBeNull();
  });

  it("falls back to the generic reasons when no pack-service summary is available", () => {
    render(<NoValidPacksModal />);

    expect(screen.getByText(/dependency-verified packs right now/i)).toBeTruthy();
    expect(screen.getByText(/no packs are published for this site/i)).toBeTruthy();
    expect(screen.getByText(/missing required dependency packs/i)).toBeTruthy();
    expect(screen.getByText(/metadata or schema is invalid/i)).toBeTruthy();
    expect(screen.getByText(/packs exist on disk but are not indexed for serving/i)).toBeTruthy();
    expect(
      screen.getByText(/publishes or repairs at least one dependency-verified pack/i),
    ).toBeTruthy();
  });
});
