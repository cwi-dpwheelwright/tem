import { fetchPublicPacks, type PackSummary } from "./catalog";
import type { PackModule, PackAction, VerifiedPack } from "./types";
import { normalizeUrl } from "../config";

interface PackManifestRecord {
  name: string;
  displayName: string;
  version: string;
  hash: string;
  modules: PackModule[];
  dependencies: string[];
}

interface PackVerificationContext {
  packBase: string;
  signal: AbortSignal;
  token?: string | null;
  manifestCache: Map<string, Promise<PackManifestRecord>>;
}

export interface DependencyVerifiedPackCatalog {
  packs: PackSummary[];
  filteredOutPacks: string[];
}

export interface LiveVerifiedPackCatalog {
  packs: VerifiedPack[];
  filteredOutPacks: string[];
}

export interface PackAvailabilityReason {
  code: string;
  count?: number | null;
  message: string;
}

export interface PackAvailabilitySummary {
  generated_utc: string;
  status: string;
  available_pack_count: number;
  filtered_pack_count: number;
  unindexed_pack_count: number;
  indexed_pack_count: number;
  discovered_pack_count: number;
  reasons: PackAvailabilityReason[];
}

const RUNTIME_SYSTEM_PACK_NAMES = new Set([
  "cwi-communication-core",
  "cwi-first-response-optimizer",
  "cwi-response-orchestrator",
  "cwi-response-refinement",
  "cwi-response-templates",
  "cwi-routing-priority-policy",
  "cwi-language-normalization-policy",
]);

function resolvePackBaseUrl(baseUrl: string): string {
  const base = normalizeUrl(baseUrl);
  if (!base) {
    return "/api/packs";
  }
  if (base.endsWith("/packs")) {
    return base;
  }
  if (base.endsWith("/api")) {
    return `${base}/packs`;
  }
  return `${base}/packs`;
}

async function fetchText(url: string, signal: AbortSignal, token?: string | null): Promise<string> {
  const headers: Record<string, string> = { Accept: "text/plain" };
  const bearer = token?.trim();
  if (bearer) {
    headers.Authorization = `Bearer ${bearer}`;
  }
  const response = await fetch(url, {
    method: "GET",
    headers,
    signal,
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`Request failed (${response.status}).`);
  }
  return await response.text();
}

async function fetchJson(url: string, signal: AbortSignal, token?: string | null): Promise<unknown> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const bearer = token?.trim();
  if (bearer) {
    headers.Authorization = `Bearer ${bearer}`;
  }
  const response = await fetch(url, {
    method: "GET",
    headers,
    signal,
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`Request failed (${response.status}).`);
  }
  const text = await response.text();
  if (!text) {
    return null;
  }
  try {
    return JSON.parse(text);
  } catch {
    throw new Error("Invalid JSON payload.");
  }
}

function normalizePackName(name: string): string {
  return name.trim().toLowerCase();
}

function normalizePackClassificationName(name: string): string {
  return normalizePackName(name).replace(/[\s_]+/g, "-");
}

function isRuntimeSystemPackName(name: string): boolean {
  return RUNTIME_SYSTEM_PACK_NAMES.has(normalizePackClassificationName(name));
}

function resolvePackSummaryId(pack: PackSummary): string | null {
  const packId = pack.pack_id?.trim();
  if (packId) {
    return packId;
  }

  const packName = pack.name?.trim();
  return packName || null;
}

function stableStringify(value: unknown): string {
  if (value === null || value === undefined) {
    return "null";
  }
  if (typeof value === "number") {
    return Number.isFinite(value) ? JSON.stringify(value) : "null";
  }
  if (typeof value === "string" || typeof value === "boolean") {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return `[${value.map((item) => stableStringify(item)).join(",")}]`;
  }
  if (typeof value === "object") {
    const record = value as Record<string, unknown>;
    const keys = Object.keys(record).sort();
    const entries = keys.map((key) => `${JSON.stringify(key)}:${stableStringify(record[key])}`);
    return `{${entries.join(",")}}`;
  }
  return "null";
}

async function sha256Hex(content: string): Promise<string> {
  const data = new TextEncoder().encode(content);
  if (globalThis.crypto?.subtle) {
    const digest = await globalThis.crypto.subtle.digest("SHA-256", data);
    const bytes = Array.from(new Uint8Array(digest));
    return bytes.map((b) => b.toString(16).padStart(2, "0")).join("");
  }

  const crypto = await import("crypto");
  return crypto.createHash("sha256").update(data).digest("hex");
}

function mapPackModules(rawModules: unknown): PackModule[] {
  if (!Array.isArray(rawModules)) {
    return [];
  }

  return rawModules.map((module) => {
    const mod = module as Record<string, unknown>;
    const actionsRaw = Array.isArray(mod.actions) ? mod.actions : [];
    const actions: PackAction[] = actionsRaw.map((action) => {
      const act = action as Record<string, unknown>;
      return {
        id: String(act.id ?? act.action_id ?? act.name ?? "action"),
        label: String(act.label ?? act.name ?? act.id ?? "Action"),
        actionType: String(act.actionType ?? act.type ?? act.action_type ?? "custom"),
        args: (act.args as Record<string, string | number | boolean | null>) ?? {},
      };
    });

    return {
      id: String(mod.id ?? mod.module_id ?? mod.name ?? "module"),
      title: typeof mod.title === "string" ? mod.title : undefined,
      surface: typeof mod.surface === "string" ? mod.surface : undefined,
      actions,
    };
  });
}

function extractDependencies(rawDependencies: unknown): string[] {
  if (!rawDependencies) {
    return [];
  }

  if (Array.isArray(rawDependencies)) {
    return extractDependencyArray(rawDependencies);
  }

  if (typeof rawDependencies === "object") {
    const dependencyRecord = rawDependencies as Record<string, unknown>;
    if (Array.isArray(dependencyRecord.packs)) {
      return extractDependencyArray(dependencyRecord.packs);
    }
    if (Array.isArray(dependencyRecord.items)) {
      return extractDependencyArray(dependencyRecord.items);
    }
  }

  return [];
}

function extractDependencyArray(items: unknown[]): string[] {
  const dependencies: string[] = [];
  for (const item of items) {
    if (typeof item === "string" && item.trim()) {
      dependencies.push(item.trim());
      continue;
    }

    if (!item || typeof item !== "object") {
      continue;
    }

    const record = item as Record<string, unknown>;
    const candidate =
      typeof record.pack_id === "string" && record.pack_id.trim()
        ? record.pack_id.trim()
        : typeof record.name === "string" && record.name.trim()
          ? record.name.trim()
          : typeof record.pack === "string" && record.pack.trim()
            ? record.pack.trim()
            : null;

    if (candidate) {
      dependencies.push(candidate);
    }
  }

  return dependencies;
}

function createVerificationContext(
  baseUrl: string,
  controller: AbortController,
  token?: string | null,
): PackVerificationContext {
  return {
    packBase: resolvePackBaseUrl(baseUrl),
    signal: controller.signal,
    token,
    manifestCache: new Map<string, Promise<PackManifestRecord>>(),
  };
}

function createTimeoutController(requestTimeoutMs?: number): {
  controller: AbortController;
  clear: () => void;
} {
  const controller = new AbortController();
  const timeout = requestTimeoutMs && requestTimeoutMs > 0
    ? setTimeout(() => controller.abort(), requestTimeoutMs)
    : null;

  return {
    controller,
    clear: () => {
      if (timeout) {
        clearTimeout(timeout);
      }
    },
  };
}

export async function fetchPackAvailabilitySummary(
  baseUrl: string,
  requestTimeoutMs?: number,
  token?: string | null,
): Promise<PackAvailabilitySummary> {
  const timeout = createTimeoutController(requestTimeoutMs);

  try {
    const packBase = resolvePackBaseUrl(baseUrl);
    const payload = await fetchJson(`${packBase}/availability`, timeout.controller.signal, token);
    if (!payload || typeof payload !== "object") {
      throw new Error("Pack availability summary is missing.");
    }

    const summary = payload as Partial<PackAvailabilitySummary>;
    return {
      generated_utc: typeof summary.generated_utc === "string" ? summary.generated_utc : new Date(0).toISOString(),
      status: typeof summary.status === "string" ? summary.status : "empty",
      available_pack_count: typeof summary.available_pack_count === "number" ? summary.available_pack_count : 0,
      filtered_pack_count: typeof summary.filtered_pack_count === "number" ? summary.filtered_pack_count : 0,
      unindexed_pack_count: typeof summary.unindexed_pack_count === "number" ? summary.unindexed_pack_count : 0,
      indexed_pack_count: typeof summary.indexed_pack_count === "number" ? summary.indexed_pack_count : 0,
      discovered_pack_count: typeof summary.discovered_pack_count === "number" ? summary.discovered_pack_count : 0,
      reasons: Array.isArray(summary.reasons)
        ? summary.reasons
            .filter((reason): reason is PackAvailabilityReason => !!reason && typeof reason === "object")
            .map((reason) => ({
              code: typeof reason.code === "string" ? reason.code : "unknown",
              count: typeof reason.count === "number" ? reason.count : null,
              message: typeof reason.message === "string" ? reason.message : "Pack availability details are unavailable.",
            }))
        : [],
    };
  } finally {
    timeout.clear();
  }
}

async function loadPackManifestRecord(
  packName: string,
  context: PackVerificationContext,
): Promise<PackManifestRecord> {
  const normalizedName = normalizePackName(packName);
  const cached = context.manifestCache.get(normalizedName);
  if (cached) {
    return await cached;
  }

  const promise = (async () => {
    const packUrl = `${context.packBase}/${encodeURIComponent(packName)}/pack.json`;
    const anchorUrl = `${context.packBase}/${encodeURIComponent(packName)}/anchor.txt`;

    const packJson = await fetchJson(packUrl, context.signal, context.token);
    if (!packJson || typeof packJson !== "object") {
      throw new Error("Pack metadata is missing.");
    }

    const packRecord = packJson as Record<string, unknown>;
    const canonical = stableStringify(packRecord);
    const computedHash = await sha256Hex(canonical);

    let anchor: string | null = null;
    try {
      anchor = (await fetchText(anchorUrl, context.signal, context.token)).trim();
    } catch {
      anchor = null;
    }

    if (anchor && anchor !== computedHash) {
      throw new Error("Pack anchor mismatch.");
    }

    const packId = String(packRecord.pack_id ?? packName);
    const displayName = typeof packRecord.name === "string" && packRecord.name.trim()
      ? packRecord.name
      : packId;
    const version = String(packRecord.pack_version ?? packRecord.version ?? "unknown");

    return {
      name: packId,
      displayName,
      version,
      hash: anchor || computedHash,
      modules: mapPackModules(packRecord.modules),
      dependencies: extractDependencies(packRecord.dependencies),
    };
  })();

  context.manifestCache.set(normalizedName, promise);

  try {
    return await promise;
  } catch (error) {
    context.manifestCache.delete(normalizedName);
    throw error;
  }
}

async function verifyDependencyChain(
  packName: string,
  context: PackVerificationContext,
  visited: Set<string>,
  verifiedDependencies: Map<string, string>,
): Promise<PackManifestRecord> {
  const normalizedName = normalizePackName(packName);
  if (visited.has(normalizedName)) {
    return await loadPackManifestRecord(packName, context);
  }

  visited.add(normalizedName);
  const manifest = await loadPackManifestRecord(packName, context);

  for (const dependency of manifest.dependencies) {
    try {
      const dependencyManifest = await verifyDependencyChain(
        dependency,
        context,
        visited,
        verifiedDependencies,
      );
      verifiedDependencies.set(
        normalizePackName(dependencyManifest.name),
        dependencyManifest.name,
      );
    } catch (error) {
      const detail = error instanceof Error ? error.message : "Pack dependency fetch failed.";
      throw new Error(`Dependency pack unavailable: ${dependency}. ${detail}`);
    }
  }

  return manifest;
}

function buildVerifiedPack(
  manifest: PackManifestRecord,
  verifiedDependencies: Map<string, string>,
): VerifiedPack {
  verifiedDependencies.delete(normalizePackName(manifest.name));

  return {
    name: manifest.name,
    displayName: manifest.displayName,
    version: manifest.version,
    hash: manifest.hash,
    verifiedAt: new Date().toISOString(),
    modules: manifest.modules,
    verificationScope: "root_and_dependencies",
    verifiedDependencies: Array.from(verifiedDependencies.values()).sort((left, right) =>
      left.localeCompare(right),
    ),
  };
}

export function isPackUnavailableError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const message = error.message.toLowerCase();
  return (
    message.includes("dependency pack unavailable:") ||
    message.includes("request failed (404)") ||
    message.includes("request failed (410)") ||
    message.includes("pack metadata is missing") ||
    message.includes("pack anchor mismatch") ||
    message.includes("invalid json payload")
  );
}

export async function fetchVerifiedPack(
  baseUrl: string,
  packName: string,
  requestTimeoutMs?: number,
  token?: string | null,
): Promise<VerifiedPack> {
  const timeout = createTimeoutController(requestTimeoutMs);
  const context = createVerificationContext(baseUrl, timeout.controller, token);

  try {
    const visited = new Set<string>();
    const verifiedDependencies = new Map<string, string>();
    const manifest = await verifyDependencyChain(packName, context, visited, verifiedDependencies);
    return buildVerifiedPack(manifest, verifiedDependencies);
  } finally {
    timeout.clear();
  }
}

export async function fetchDependencyVerifiedPublicPacks(
  baseUrl: string,
  requestTimeoutMs?: number,
  token?: string | null,
): Promise<DependencyVerifiedPackCatalog> {
  const packs = await fetchPublicPacks(baseUrl, requestTimeoutMs, token);
  const timeout = createTimeoutController(requestTimeoutMs);
  const context = createVerificationContext(baseUrl, timeout.controller, token);

  try {
    const verified: PackSummary[] = [];
    const filteredOutPacks: string[] = [];

    for (const pack of packs) {
      const packId = resolvePackSummaryId(pack);
      if (!packId) {
        continue;
      }
      if (isRuntimeSystemPackName(packId)) {
        continue;
      }

      try {
        await verifyDependencyChain(packId, context, new Set<string>(), new Map<string, string>());
        verified.push(pack);
      } catch (error) {
        if (isPackUnavailableError(error)) {
          filteredOutPacks.push(packId);
          continue;
        }

        throw error;
      }
    }

    return { packs: verified, filteredOutPacks };
  } finally {
    timeout.clear();
  }
}

export async function fetchLiveVerifiedPacks(
  baseUrl: string,
  requestTimeoutMs?: number,
  token?: string | null,
): Promise<LiveVerifiedPackCatalog> {
  const packs = await fetchPublicPacks(baseUrl, requestTimeoutMs, token);
  const timeout = createTimeoutController(requestTimeoutMs);
  const context = createVerificationContext(baseUrl, timeout.controller, token);

  try {
    const verified: VerifiedPack[] = [];
    const filteredOutPacks: string[] = [];

    for (const pack of packs) {
      const packId = resolvePackSummaryId(pack);
      if (!packId) {
        continue;
      }
      if (isRuntimeSystemPackName(packId)) {
        continue;
      }

      try {
        const verifiedDependencies = new Map<string, string>();
        const manifest = await verifyDependencyChain(packId, context, new Set<string>(), verifiedDependencies);
        verified.push(buildVerifiedPack(manifest, verifiedDependencies));
      } catch (error) {
        if (isPackUnavailableError(error)) {
          filteredOutPacks.push(packId);
          continue;
        }

        throw error;
      }
    }

    return { packs: verified, filteredOutPacks };
  } finally {
    timeout.clear();
  }
}

