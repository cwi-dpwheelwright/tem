import { getRuntimeConfigStatus } from "@/lib/config";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
};

function getBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.apiBaseUrl ?? null;
}

async function postJson<T>(
  path: string,
  payload: unknown,
  accessToken?: string | null,
  breakGlassToken?: string | null,
  tenantId?: string | null,
): Promise<ApiResult<T>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const headers = new Headers();
  headers.set("Content-Type", "application/json");
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }
  if (breakGlassToken) {
    headers.set("X-Break-Glass-Token", breakGlassToken);
  }
  if (tenantId) {
    headers.set("X-Tenant-Id", tenantId);
  }

  const response = await fetch(`${baseUrl}${path}` , {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });

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

export async function retirePack(
  request: {
    tenantId?: string;
    packId: string;
    actorUserId: string;
    reason?: string;
  },
  accessToken?: string | null,
  breakGlassToken?: string | null,
): Promise<ApiResult<unknown>> {
  return postJson(
    "/api/packs/retire",
    {
      tenantId: request.tenantId,
      packId: request.packId,
      actorUserId: request.actorUserId,
      reason: request.reason,
    },
    accessToken,
    breakGlassToken,
    request.tenantId ?? null,
  );
}

export async function deletePack(
  request: {
    tenantId?: string;
    packId: string;
    packVersion?: string;
    actorUserId: string;
    reason?: string;
  },
  accessToken?: string | null,
  breakGlassToken?: string | null,
): Promise<ApiResult<unknown>> {
  return postJson(
    "/api/packs/delete",
    {
      tenantId: request.tenantId,
      packId: request.packId,
      packVersion: request.packVersion,
      actorUserId: request.actorUserId,
      reason: request.reason,
    },
    accessToken,
    breakGlassToken,
    request.tenantId ?? null,
  );
}
