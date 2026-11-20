import { getRuntimeConfigStatus } from "@/lib/config";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

export type PackIndexEntry = {
  pack_id: string;
  name?: string;
  latest?: string;
  versions?: string[];
  visibility?: string;
};

export type PackIndex = {
  packs: PackIndexEntry[];
};

export type PackDetails = {
  pack_id: string;
  pack_version?: string;
  name?: string;
  modules?: unknown[];
  visibility?: string;
};

export type PackCatalogItem = {
  entry: PackIndexEntry;
  details?: PackDetails;
  anchor?: string;
};

function getBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.packsBaseUrl ?? null;
}

async function fetchText(path: string): Promise<ApiResult<string>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "Packs base URL is not configured." };
  }
  const response = await fetch(`${baseUrl}${path}`);
  const text = await response.text();
  if (!response.ok) {
    return { ok: false, status: response.status, error: text || response.statusText };
  }
  return { ok: true, status: response.status, data: text };
}

async function fetchJson<T>(path: string): Promise<ApiResult<T>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "Packs base URL is not configured." };
  }
  const response = await fetch(`${baseUrl}${path}`);
  const text = await response.text();
  if (!response.ok) {
    return { ok: false, status: response.status, error: text || response.statusText };
  }
  if (!text) {
    return { ok: true, status: response.status };
  }
  try {
    return { ok: true, status: response.status, data: JSON.parse(text) as T };
  } catch {
    return { ok: false, status: response.status, error: "Invalid JSON response." };
  }
}

export async function getPackIndex(): Promise<ApiResult<PackIndex>> {
  const primary = await fetchJson<PackIndex>("/packs");
  if (primary.ok) {
    return primary;
  }
  return fetchJson<PackIndex>("/packs/index.json");
}

export async function getPackDetails(packId: string): Promise<ApiResult<PackDetails>> {
  return fetchJson<PackDetails>(`/packs/${encodeURIComponent(packId)}`);
}

export async function getPackAnchor(packId: string): Promise<ApiResult<string>> {
  return fetchText(`/packs/${encodeURIComponent(packId)}/anchor.txt`);
}
