export type HomeNodeLinkPayloadPreview =
  | { kind: "empty" }
  | { kind: "invalid"; reason: string }
  | {
      kind: "canonical_json";
      nodeId?: string;
      tailscaleIp?: string;
      tenantId?: string;
      identifierVersion?: string;
      matchedVia: "node_id" | "tailscale_ip";
    }
  | {
      kind: "legacy_node_id";
      nodeId: string;
      matchedVia: "node_id";
    }
  | {
      kind: "legacy_tailscale_ip";
      tailscaleIp: string;
      matchedVia: "tailscale_ip";
    };

type ReferenceDetails = {
  nodeId?: string;
  tailscaleIp?: string;
  tenantId?: string;
  identifierVersion?: string;
  matchedVia: "node_id" | "tailscale_ip";
};

const WRAPPED_PAYLOAD_KEYS = ["payload", "qr_payload", "qrPayload", "data", "node"] as const;

export function parseHomeNodeLinkPayload(rawPayload: string): HomeNodeLinkPayloadPreview {
  const trimmed = rawPayload.trim();
  if (!trimmed) {
    return { kind: "empty" };
  }

  if (looksLikeStructuredPayload(trimmed)) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      const details = extractReferenceDetails(parsed);
      if (!details) {
        return { kind: "invalid", reason: "The QR payload JSON is missing node_id or tailscale_ip." };
      }

      return {
        kind: "canonical_json",
        nodeId: details.nodeId,
        tailscaleIp: details.tailscaleIp,
        tenantId: details.tenantId,
        identifierVersion: details.identifierVersion,
        matchedVia: details.matchedVia,
      };
    } catch {
      return {
        kind: "invalid",
        reason: "The QR payload JSON is malformed. Paste the JSON from /payload.json or scan the node QR again.",
      };
    }
  }

  if (!looksLikeLegacyRawIdentifier(trimmed)) {
    return {
      kind: "invalid",
      reason: "Paste the canonical JSON payload, a raw node ID, or a legacy Tailscale IP.",
    };
  }

  if (looksLikeIpAddress(trimmed)) {
    return { kind: "legacy_tailscale_ip", tailscaleIp: trimmed, matchedVia: "tailscale_ip" };
  }

  return { kind: "legacy_node_id", nodeId: trimmed, matchedVia: "node_id" };
}

function extractReferenceDetails(payload: unknown, root?: Record<string, unknown>): ReferenceDetails | null {
  if (!isRecord(payload)) {
    return null;
  }

  const container = root ?? payload;
  const nodeId = readString(payload, "node_id", "nodeId", "home_node_id", "homeNodeId");
  let tailscaleIp = readString(payload, "tailscale_ip", "tailscaleIp");
  const identifier = readString(payload, "identifier");

  if (!nodeId && !tailscaleIp && identifier) {
    if (looksLikeIpAddress(identifier)) {
      tailscaleIp = identifier;
    } else {
      const tenantId = readString(payload, "tenant_id", "tenantId") ?? readString(container, "tenant_id", "tenantId") ?? undefined;
      const identifierVersion =
        readString(payload, "identifier_version", "identifierVersion") ??
        readString(container, "identifier_version", "identifierVersion") ??
        undefined;

      return {
        nodeId: identifier,
        tenantId,
        identifierVersion,
        matchedVia: "node_id",
      };
    }
  }

  if (nodeId || tailscaleIp) {
    const tenantId = readString(payload, "tenant_id", "tenantId") ?? readString(container, "tenant_id", "tenantId") ?? undefined;
    const identifierVersion =
      readString(payload, "identifier_version", "identifierVersion") ??
      readString(container, "identifier_version", "identifierVersion") ??
      undefined;

    return {
      nodeId: nodeId ?? undefined,
      tailscaleIp: tailscaleIp ?? undefined,
      tenantId,
      identifierVersion,
      matchedVia: nodeId ? "node_id" : "tailscale_ip",
    };
  }

  for (const key of WRAPPED_PAYLOAD_KEYS) {
    const child = payload[key];
    if (typeof child === "string" && looksLikeStructuredPayload(child)) {
      try {
        const nested = JSON.parse(child) as unknown;
        const nestedDetails = extractReferenceDetails(nested, container);
        if (nestedDetails) {
          return nestedDetails;
        }
      } catch {
        return null;
      }
    }

    const nestedDetails = extractReferenceDetails(child, container);
    if (nestedDetails) {
      return nestedDetails;
    }
  }

  return null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readString(source: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
  }

  return null;
}

function looksLikeStructuredPayload(value: string): boolean {
  const trimmed = value.trimStart();
  return trimmed.startsWith("{") || trimmed.startsWith("[");
}

function looksLikeLegacyRawIdentifier(value: string): boolean {
  return !/\s/.test(value) &&
    !value.includes("{") &&
    !value.includes("}") &&
    !value.includes("[") &&
    !value.includes("]") &&
    !value.includes("\"") &&
    !value.includes("://");
}

function looksLikeIpAddress(value: string): boolean {
  return /^(?:\d{1,3}\.){3}\d{1,3}$/.test(value) || /^[0-9a-f:.]+$/i.test(value);
}
