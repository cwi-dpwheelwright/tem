import { afterEach, describe, expect, it, vi } from "vitest";
import {
  fetchDependencyVerifiedPublicPacks,
  fetchLiveVerifiedPacks,
  fetchVerifiedPack,
  isPackUnavailableError,
} from "@/lib/packs/client";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function textResponse(body: string, status = 200): Response {
  return new Response(body, {
    status,
    headers: { "Content-Type": "text/plain" },
  });
}

describe("pack client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("verifies the full dependency chain and records transitive dependencies", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/root-pack/pack.json")) {
        return jsonResponse({
          pack_id: "root-pack",
          pack_version: "1.0.0",
          name: "Root Pack",
          modules: [{ id: "root-module", actions: [] }],
          dependencies: {
            packs: [
              { pack_id: "child-pack" },
              { pack_id: "leaf-pack" },
            ],
          },
        });
      }
      if (url.endsWith("/api/packs/child-pack/pack.json")) {
        return jsonResponse({
          pack_id: "child-pack",
          pack_version: "1.1.0",
          name: "Child Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "leaf-pack" }],
          },
        });
      }
      if (url.endsWith("/api/packs/leaf-pack/pack.json")) {
        return jsonResponse({
          pack_id: "leaf-pack",
          pack_version: "2.0.0",
          name: "Leaf Pack",
          modules: [],
        });
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    const verified = await fetchVerifiedPack("/api", "root-pack", 5000);

    expect(verified.name).toBe("root-pack");
    expect(verified.displayName).toBe("Root Pack");
    expect(verified.version).toBe("1.0.0");
    expect(verified.verificationScope).toBe("root_and_dependencies");
    expect(verified.verifiedDependencies).toEqual(["child-pack", "leaf-pack"]);
    expect(verified.modules).toHaveLength(1);
  });

  it("fails verification when a dependency pack is missing", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/root-pack/pack.json")) {
        return jsonResponse({
          pack_id: "root-pack",
          pack_version: "1.0.0",
          name: "Root Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "missing-pack" }],
          },
        });
      }
      if (url.endsWith("/api/packs/missing-pack/pack.json")) {
        return textResponse("not found", 404);
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    await expect(fetchVerifiedPack("/api", "root-pack", 5000)).rejects.toThrow(
      "Dependency pack unavailable: missing-pack.",
    );
  });

  it("filters catalog entries down to dependency-verified packs only", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/index.json")) {
        return jsonResponse([
          { pack_id: "valid-pack", name: "Valid Pack", latest: "aaa" },
          { pack_id: "broken-pack", name: "Broken Pack", latest: "bbb" },
        ]);
      }
      if (url.endsWith("/api/packs/valid-pack/pack.json")) {
        return jsonResponse({
          pack_id: "valid-pack",
          pack_version: "1.0.0",
          name: "Valid Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "shared-pack" }],
          },
        });
      }
      if (url.endsWith("/api/packs/broken-pack/pack.json")) {
        return jsonResponse({
          pack_id: "broken-pack",
          pack_version: "1.0.0",
          name: "Broken Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "missing-pack" }],
          },
        });
      }
      if (url.endsWith("/api/packs/shared-pack/pack.json")) {
        return jsonResponse({
          pack_id: "shared-pack",
          pack_version: "1.0.0",
          name: "Shared Pack",
          modules: [],
        });
      }
      if (url.endsWith("/api/packs/missing-pack/pack.json")) {
        return textResponse("not found", 404);
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    const result = await fetchDependencyVerifiedPublicPacks("/api", 5000);

    expect(result.packs.map((pack) => pack.pack_id)).toEqual(["valid-pack"]);
    expect(result.filteredOutPacks).toEqual(["broken-pack"]);
  });

  it("hides runtime system packs from the dependency-verified public selector catalog", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/index.json")) {
        return jsonResponse([
          { pack_id: "domain-pack", name: "Domain Pack", latest: "1.0.0" },
          { pack_id: "cwi-communication-core", name: "Communication Core", latest: "1.0.0" },
          { name: "CWI_Response_Templates", latest: "1.0.0" },
        ]);
      }
      if (url.endsWith("/api/packs/domain-pack/pack.json")) {
        return jsonResponse({
          pack_id: "domain-pack",
          pack_version: "1.0.0",
          name: "Domain Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "cwi-response-orchestrator" }],
          },
        });
      }
      if (url.endsWith("/api/packs/cwi-response-orchestrator/pack.json")) {
        return jsonResponse({
          pack_id: "cwi-response-orchestrator",
          pack_version: "1.0.0",
          name: "Response Orchestrator",
          modules: [],
        });
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    const result = await fetchDependencyVerifiedPublicPacks("/api", 5000);
    const fetchedUrls = fetchMock.mock.calls.map(([input]) => String(input));

    expect(result.packs.map((pack) => pack.pack_id ?? pack.name)).toEqual(["domain-pack"]);
    expect(result.filteredOutPacks).toEqual([]);
    expect(fetchedUrls.some((url) => url.endsWith("/api/packs/cwi-communication-core/pack.json"))).toBe(false);
    expect(fetchedUrls.some((url) => url.endsWith("/api/packs/CWI_Response_Templates/pack.json"))).toBe(false);
    expect(fetchedUrls.some((url) => url.endsWith("/api/packs/cwi-response-orchestrator/pack.json"))).toBe(true);
  });

  it("loads full live verified pack records for selection surfaces", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/index.json")) {
        return jsonResponse([
          { pack_id: "valid-pack", name: "Valid Pack", latest: "1.0.0" },
          { pack_id: "broken-pack", name: "Broken Pack", latest: "1.0.0" },
        ]);
      }
      if (url.endsWith("/api/packs/valid-pack/pack.json")) {
        return jsonResponse({
          pack_id: "valid-pack",
          pack_version: "1.0.0",
          name: "Valid Pack",
          modules: [{ id: "module-a", title: "Operations", actions: [] }],
        });
      }
      if (url.endsWith("/api/packs/broken-pack/pack.json")) {
        return jsonResponse({
          pack_id: "broken-pack",
          pack_version: "1.0.0",
          name: "Broken Pack",
          modules: [],
          dependencies: {
            packs: [{ pack_id: "missing-pack" }],
          },
        });
      }
      if (url.endsWith("/api/packs/missing-pack/pack.json")) {
        return textResponse("not found", 404);
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    const result = await fetchLiveVerifiedPacks("/api", 5000);

    expect(result.packs).toHaveLength(1);
    expect(result.packs[0]).toMatchObject({
      name: "valid-pack",
      displayName: "Valid Pack",
      version: "1.0.0",
      verificationScope: "root_and_dependencies",
    });
    expect(result.filteredOutPacks).toEqual(["broken-pack"]);
  });

  it("hides runtime system packs from live selection surfaces without blocking runtime dependencies", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/packs/index.json")) {
        return jsonResponse([
          { pack_id: "valid-pack", name: "Valid Pack", latest: "1.0.0" },
          { pack_id: "cwi-language-normalization-policy", name: "Language Normalization", latest: "1.0.0" },
          { pack_id: "cwi-routing-priority-policy", name: "Routing Priority", latest: "1.0.0" },
        ]);
      }
      if (url.endsWith("/api/packs/valid-pack/pack.json")) {
        return jsonResponse({
          pack_id: "valid-pack",
          pack_version: "1.0.0",
          name: "Valid Pack",
          modules: [{ id: "module-a", title: "Operations", actions: [] }],
          dependencies: {
            packs: ["cwi-language-normalization-policy"],
          },
        });
      }
      if (url.endsWith("/api/packs/cwi-language-normalization-policy/pack.json")) {
        return jsonResponse({
          pack_id: "cwi-language-normalization-policy",
          pack_version: "1.0.0",
          name: "Language Normalization",
          modules: [],
        });
      }
      if (url.endsWith("/anchor.txt")) {
        return textResponse("", 404);
      }

      return textResponse("not found", 404);
    });

    vi.stubGlobal("fetch", fetchMock);

    const result = await fetchLiveVerifiedPacks("/api", 5000);
    const fetchedUrls = fetchMock.mock.calls.map(([input]) => String(input));

    expect(result.packs).toHaveLength(1);
    expect(result.packs[0]).toMatchObject({
      name: "valid-pack",
      verifiedDependencies: ["cwi-language-normalization-policy"],
    });
    expect(result.filteredOutPacks).toEqual([]);
    expect(fetchedUrls.some((url) => url.endsWith("/api/packs/cwi-routing-priority-policy/pack.json"))).toBe(false);
  });

  it("classifies missing dependency failures as pack availability errors", () => {
    expect(isPackUnavailableError(new Error("Dependency pack unavailable: core-pack. Request failed (404)."))).toBe(true);
    expect(isPackUnavailableError(new Error("Request failed (401)."))).toBe(false);
  });
});
