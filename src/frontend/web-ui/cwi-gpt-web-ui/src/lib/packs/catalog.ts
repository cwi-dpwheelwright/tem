import { normalizeUrl } from "../config";

export interface PackSummary {
  pack_id?: string;
  name?: string;
  latest?: string;
}

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

async function readJson(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) {
    return null;
  }
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

export async function fetchPublicPacks(
  baseUrl: string,
  requestTimeoutMs?: number,
  token?: string | null,
): Promise<PackSummary[]> {
  const controller = new AbortController();
  const timeout = requestTimeoutMs && requestTimeoutMs > 0
    ? setTimeout(() => controller.abort(), requestTimeoutMs)
    : null;

  try {
    const url = `${resolvePackBaseUrl(baseUrl)}/index.json`;
    const headers: Record<string, string> = { Accept: "application/json" };
    const bearer = token?.trim();
    if (bearer) {
      headers.Authorization = `Bearer ${bearer}`;
    }
    const response = await fetch(url, {
      method: "GET",
      headers,
      signal: controller.signal,
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Pack catalog request failed (${response.status}).`);
    }

    const payload = await readJson(response);
    if (Array.isArray(payload)) {
      return payload as PackSummary[];
    }
    if (payload && typeof payload === "object" && Array.isArray((payload as { packs?: unknown }).packs)) {
      return (payload as { packs: PackSummary[] }).packs;
    }
    return [];
  } finally {
    if (timeout) {
      clearTimeout(timeout);
    }
  }
}
