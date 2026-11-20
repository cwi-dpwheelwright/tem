import { resolveIdentityBaseUrl } from "@/lib/identity/base-url";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

export type ErrorPayload = {
  error?: string;
  message?: string;
  details?: string[];
};

function readError(text: string): string {
  if (!text) {
    return "Request failed.";
  }
  try {
    const parsed = JSON.parse(text) as ErrorPayload;
    if (parsed.message) {
      return parsed.message;
    }
    if (parsed.error) {
      return parsed.error;
    }
    if (parsed.details && parsed.details.length > 0) {
      return parsed.details.join(" ");
    }
  } catch {
    return text;
  }
  return text;
}

async function request<T>(path: string, options: RequestInit, accessToken?: string | null): Promise<ApiResult<T>> {
  const headers = new Headers(options.headers ?? {});
  headers.set("Content-Type", "application/json");
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  const baseUrl = resolveIdentityBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "Identity base URL is not configured." };
  }
  const response = await fetch(`${baseUrl}${path}`, { ...options, headers });
  const text = await response.text();

  if (!response.ok) {
    return { ok: false, status: response.status, error: readError(text) || response.statusText };
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

export async function apiGet<T>(path: string, accessToken?: string | null): Promise<ApiResult<T>> {
  return request<T>(path, { method: "GET" }, accessToken);
}

export async function apiPost<T>(path: string, payload?: unknown, accessToken?: string | null): Promise<ApiResult<T>> {
  return request<T>(path, { method: "POST", body: payload ? JSON.stringify(payload) : undefined }, accessToken);
}

export async function apiPatch<T>(path: string, payload?: unknown, accessToken?: string | null): Promise<ApiResult<T>> {
  return request<T>(path, { method: "PATCH", body: payload ? JSON.stringify(payload) : undefined }, accessToken);
}

export async function apiPut<T>(path: string, payload?: unknown, accessToken?: string | null): Promise<ApiResult<T>> {
  return request<T>(path, { method: "PUT", body: payload ? JSON.stringify(payload) : undefined }, accessToken);
}
