import { beforeEach, describe, expect, it } from "vitest";
import {
  clearAllVerifiedPacks,
  clearVerifiedPack,
  loadVerifiedPack,
  loadVerifiedPacks,
  saveVerifiedPack,
} from "@/lib/packs/store";

describe("verified pack store", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("loads dependency-verified packs from storage", () => {
    saveVerifiedPack({
      name: "valid-pack",
      displayName: "Valid Pack",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: ["shared-pack"],
    });

    expect(loadVerifiedPack("valid-pack")).toMatchObject({
      name: "valid-pack",
      verificationScope: "root_and_dependencies",
      verifiedDependencies: ["shared-pack"],
    });
  });

  it("drops legacy root-only cache entries", () => {
    window.localStorage.setItem("cwi.user.web.pack.verified.legacy-pack", JSON.stringify({
      name: "legacy-pack",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
    }));

    expect(loadVerifiedPack("legacy-pack")).toBeNull();
    expect(window.localStorage.getItem("cwi.user.web.pack.verified.legacy-pack")).toBeNull();
  });

  it("filters invalid cache entries out of the verified pack list", () => {
    saveVerifiedPack({
      name: "valid-pack",
      displayName: "Valid Pack",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    });
    window.localStorage.setItem("cwi.user.web.pack.verified.legacy-pack", JSON.stringify({
      name: "legacy-pack",
      version: "1.0.0",
      hash: "def456",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
    }));

    expect(loadVerifiedPacks().map((pack) => pack.name)).toEqual(["valid-pack"]);
    expect(window.localStorage.getItem("cwi.user.web.pack.verified.legacy-pack")).toBeNull();
  });

  it("clears the legacy verified pack cache in one pass", () => {
    saveVerifiedPack({
      name: "valid-pack",
      displayName: "Valid Pack",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    });
    saveVerifiedPack({
      name: "other-pack",
      displayName: "Other Pack",
      version: "1.1.0",
      hash: "def456",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    });

    clearAllVerifiedPacks();

    expect(loadVerifiedPacks()).toEqual([]);
  });

  it("clears verified packs by name", () => {
    saveVerifiedPack({
      name: "valid-pack",
      displayName: "Valid Pack",
      version: "1.0.0",
      hash: "abc123",
      verifiedAt: "2026-04-05T00:00:00Z",
      modules: [],
      verificationScope: "root_and_dependencies",
      verifiedDependencies: [],
    });

    clearVerifiedPack("valid-pack");

    expect(loadVerifiedPack("valid-pack")).toBeNull();
  });
});
