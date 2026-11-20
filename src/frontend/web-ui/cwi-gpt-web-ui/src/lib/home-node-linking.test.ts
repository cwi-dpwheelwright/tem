import { describe, expect, it } from "vitest";
import { parseHomeNodeLinkPayload } from "@/lib/home-node-linking";

describe("home-node-linking", () => {
  it("parses canonical QR payload JSON", () => {
    expect(
      parseHomeNodeLinkPayload(`{
        "tenant_id": "tenant-1",
        "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
        "identifier_version": "machine-id-sha256-v1"
      }`),
    ).toEqual({
      kind: "canonical_json",
      tenantId: "tenant-1",
      nodeId: "home-032c311b95ac9a14a81f0b3fa2520ae9",
      identifierVersion: "machine-id-sha256-v1",
      matchedVia: "node_id",
    });
  });

  it("parses canonical QR payload JSON without tenant metadata", () => {
    expect(
      parseHomeNodeLinkPayload(`{
        "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
        "identifier_version": "machine-id-sha256-v1"
      }`),
    ).toEqual({
      kind: "canonical_json",
      tenantId: undefined,
      nodeId: "home-032c311b95ac9a14a81f0b3fa2520ae9",
      identifierVersion: "machine-id-sha256-v1",
      matchedVia: "node_id",
    });
  });

  it("parses wrapped payload JSON", () => {
    expect(
      parseHomeNodeLinkPayload(`{
        "tenant_id": "tenant-1",
        "payload": {
          "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9"
        }
      }`),
    ).toEqual({
      kind: "canonical_json",
      tenantId: "tenant-1",
      nodeId: "home-032c311b95ac9a14a81f0b3fa2520ae9",
      identifierVersion: undefined,
      matchedVia: "node_id",
    });
  });

  it("flags structured payloads that are missing identifiers", () => {
    expect(
      parseHomeNodeLinkPayload(`{
        "tenant_id": "tenant-1",
        "identifier_version": "machine-id-sha256-v1"
      }`),
    ).toEqual({
      kind: "invalid",
      reason: "The QR payload JSON is missing node_id or tailscale_ip.",
    });
  });

  it("accepts a raw legacy node id", () => {
    expect(parseHomeNodeLinkPayload("home-032c311b95ac9a14a81f0b3fa2520ae9")).toEqual({
      kind: "legacy_node_id",
      nodeId: "home-032c311b95ac9a14a81f0b3fa2520ae9",
      matchedVia: "node_id",
    });
  });
});
