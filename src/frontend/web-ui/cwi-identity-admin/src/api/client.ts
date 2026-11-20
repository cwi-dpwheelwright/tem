export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

const TOKEN_KEY = "cwi.identity.admin.token";
const BASE_URL = import.meta.env.VITE_IDENTITY_BASE_URL || "";
const ORCHESTRATOR_BASE_URL = import.meta.env.VITE_ORCHESTRATOR_BASE_URL || "";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

export function getBaseUrl(): string {
  return BASE_URL;
}

export function setBaseUrl(_baseUrl: string): void {
  // Base URL is configured at build time; ignore runtime overrides.
}

async function request<T>(path: string, options: RequestInit): Promise<ApiResult<T>> {
  return requestFromBase<T>(getBaseUrl(), path, options);
}

async function requestFromBase<T>(baseUrl: string, path: string, options: RequestInit): Promise<ApiResult<T>> {
  const token = getToken();
  const headers = new Headers(options.headers || {});
  headers.set("Content-Type", "application/json");
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  try {
    const response = await fetch(`${baseUrl}${path}`, { ...options, headers });
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
  } catch (error) {
    return {
      ok: false,
      status: 0,
      error: error instanceof Error ? error.message : "Request failed."
    };
  }
}

export async function apiGet<T>(path: string): Promise<ApiResult<T>> {
  return request<T>(path, { method: "GET" });
}

export async function orchestratorApiGet<T>(path: string): Promise<ApiResult<T>> {
  if (!ORCHESTRATOR_BASE_URL) {
    return {
      ok: false,
      status: 0,
      error: "Orchestrator API base URL is not configured."
    };
  }

  return requestFromBase<T>(ORCHESTRATOR_BASE_URL, path, { method: "GET" });
}

export async function apiPost<T>(path: string, payload?: unknown): Promise<ApiResult<T>> {
  return request<T>(path, { method: "POST", body: payload ? JSON.stringify(payload) : undefined });
}

export async function apiPatch<T>(path: string, payload?: unknown): Promise<ApiResult<T>> {
  return request<T>(path, { method: "PATCH", body: payload ? JSON.stringify(payload) : undefined });
}

export async function apiPut<T>(path: string, payload?: unknown): Promise<ApiResult<T>> {
  return request<T>(path, { method: "PUT", body: payload ? JSON.stringify(payload) : undefined });
}
