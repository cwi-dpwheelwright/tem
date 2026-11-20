import { describe, expect, it } from "vitest";
import {
  buildReconciledVerifiedPack,
  findMatchingPackSummary,
  isPackSelectionAvailable,
  resolveCanonicalPackSelection,
  shouldReconcileLegacyPackSelection,
} from "@/lib/packs/reconcile";

describe("pack reconciliation", () => {
  it("detects legacy display-name selections", () => {
    expect(shouldReconcileLegacyPackSelection("DreamSpace Prompt", {
      name: "DreamSpace Prompt",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    })).toBe(true);
  });

  it("resolves the canonical pack id from the catalog using the verified hash", () => {
    const resolved = resolveCanonicalPackSelection(
      "DreamSpace Prompt",
      {
        name: "DreamSpace Prompt",
        version: "1.0.0",
        hash: "ABC123",
        verifiedAt: "2026-04-05T00:00:00Z",
        modules: [],
        verificationScope: "root_and_dependencies",
        verifiedDependencies: [],
      },
      [
        { pack_id: "dreamspace-prompt", name: "DreamSpace Prompt", latest: "abc123" },
      ],
    );

    expect(resolved).toBe("dreamspace-prompt");
  });

  it("returns null when the selection is already canonical", () => {
    const resolved = resolveCanonicalPackSelection(
      "dreamspace-prompt",
      {
        name: "dreamspace-prompt",
        version: "1.0.0",
        hash: "abc123",
        verifiedAt: "2026-04-05T00:00:00Z",
        modules: [],
        verificationScope: "root_and_dependencies",
        verifiedDependencies: [],
      },
      [
        { pack_id: "dreamspace-prompt", name: "DreamSpace Prompt", latest: "abc123" },
      ],
    );

    expect(resolved).toBeNull();
  });

  it("reports unavailable selections when no catalog match exists", () => {
    expect(isPackSelectionAvailable(
      "fixture-pack",
      {
        name: "fixture-pack",
        version: "1.0.0",
        hash: "abc123",
        verifiedAt: "2026-04-05T00:00:00Z",
        modules: [],
        verificationScope: "root_and_dependencies",
        verifiedDependencies: [],
      },
      [
        { pack_id: "dreamspace-prompt", name: "DreamSpace Prompt", latest: "def456" },
      ],
    )).toBe(false);
  });

  it("matches catalog entries by exact selected id even without a cached pack", () => {
    const match = findMatchingPackSummary(
      "dreamspace-prompt",
      null,
      [
        { pack_id: "dreamspace-prompt", name: "DreamSpace Prompt", latest: "abc123" },
      ],
    );

    expect(match?.pack_id).toBe("dreamspace-prompt");
  });

  it("preserves the previous display name when rebuilding the verified pack", () => {
    const updated = buildReconciledVerifiedPack({
      name: "DreamSpace Prompt",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    }, "dreamspace-prompt");

    expect(updated.name).toBe("dreamspace-prompt");
    expect(updated.displayName).toBe("DreamSpace Prompt");
  });
});
