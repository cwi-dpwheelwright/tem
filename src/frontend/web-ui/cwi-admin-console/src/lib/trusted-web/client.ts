import { getRuntimeConfigStatus } from "@/lib/config";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

export type TrustedWebSourceConfig = {
  source_id?: string;
  display_name?: string;
  allowed_domains: string[];
  seed_urls: string[];
  sitemap_urls: string[];
  rss_urls: string[];
  include_path_prefixes: string[];
  exclude_path_prefixes: string[];
};

export type TrustedWebProfileConfig = {
  description?: string;
  mode: "trusted_sites_only";
  max_results?: number;
  sources: TrustedWebSourceConfig[];
};

export type TrustedWebConfig = {
  enabled: boolean;
  version: number;
  default_profile: string;
  profiles: Record<string, TrustedWebProfileConfig>;
  source?: string;
  updated_utc?: string;
  updated_by?: string;
};

function getBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.apiBaseUrl ?? null;
}

function buildHeaders(accessToken?: string | null): Headers {
  const headers = new Headers();
  headers.set("Content-Type", "application/json");
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }
  return headers;
}

async function parseResponse<T>(response: Response): Promise<ApiResult<T>> {
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
    return { ok: true, status: response.status };
  }
}

export async function getTrustedWebConfig(
  accessToken?: string | null,
): Promise<ApiResult<TrustedWebConfig>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const url = new URL("/api/admin/trusted-web", baseUrl);
  const response = await fetch(url, {
    headers: buildHeaders(accessToken),
  });
  return parseResponse<TrustedWebConfig>(response);
}

export async function saveTrustedWebConfig(
  config: TrustedWebConfig,
  accessToken?: string | null,
): Promise<ApiResult<TrustedWebConfig>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const url = new URL("/api/admin/trusted-web", baseUrl);
  const response = await fetch(url, {
    method: "PUT",
    headers: buildHeaders(accessToken),
    body: JSON.stringify(config),
  });
  return parseResponse<TrustedWebConfig>(response);
}
