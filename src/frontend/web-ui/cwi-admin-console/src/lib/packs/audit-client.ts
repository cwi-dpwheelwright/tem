import { getRuntimeConfigStatus } from "@/lib/config";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

export type PackAuditQuery = {
  tenantId?: string;
  packId?: string;
  actorUserId?: string;
  eventType?: string;
  startUtc?: string;
  endUtc?: string;
  limit?: number;
};

function getBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.apiBaseUrl ?? null;
}

export async function getPackAuditLog(
  query: PackAuditQuery,
  accessToken?: string | null,
  breakGlassToken?: string | null,
): Promise<ApiResult<unknown>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const url = new URL("/api/packs/audit", baseUrl);
  if (query.packId) {
    url.searchParams.set("pack_id", query.packId);
  }
  if (query.actorUserId) {
    url.searchParams.set("actor_id", query.actorUserId);
  }
  if (query.eventType) {
    url.searchParams.set("event_type", query.eventType);
  }
  if (query.startUtc) {
    url.searchParams.set("start_utc", query.startUtc);
  }
  if (query.endUtc) {
    url.searchParams.set("end_utc", query.endUtc);
  }
  if (query.limit) {
    url.searchParams.set("limit", query.limit.toString());
  }

  const headers = new Headers();
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }
  if (breakGlassToken) {
    headers.set("X-Break-Glass-Token", breakGlassToken);
  }
  if (query.tenantId) {
    headers.set("X-Tenant-Id", query.tenantId);
  }

  const response = await fetch(url.toString(), { method: "GET", headers });
  const text = await response.text();

  if (!response.ok) {
    return { ok: false, status: response.status, error: text || response.statusText };
  }

  if (!text) {
    return { ok: true, status: response.status };
  }

  try {
    return { ok: true, status: response.status, data: JSON.parse(text) as unknown };
  } catch {
    return { ok: true, status: response.status, data: text };
  }
}
