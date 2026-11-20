import { getRuntimeConfigStatus } from "@/lib/config";

export type ApiResult<T> = {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
  raw?: string;
};

export type PackUploadInput = {
  file: File;
  packId?: string;
  packVersion?: string;
  notes?: string;
  dryRun?: boolean;
  tenantId?: string;
  breakGlassToken?: string;
};

function getBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.apiBaseUrl ?? null;
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

export async function ingestPack(
  input: PackUploadInput,
  accessToken?: string | null,
): Promise<ApiResult<unknown>> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) {
    return { ok: false, status: 0, error: "API base URL is not configured." };
  }

  const form = new FormData();
  form.set("bundle", input.file);
  if (input.packId) {
    form.set("pack_id", input.packId);
  }
  if (input.packVersion) {
    form.set("pack_version", input.packVersion);
  }
  if (input.notes) {
    form.set("notes", input.notes);
  }
  if (input.dryRun) {
    form.set("dry_run", "true");
  }
  if (input.tenantId) {
    form.set("tenant_id", input.tenantId);
  }

  const url = new URL("/api/ingest/skillpack", baseUrl);
  if (input.dryRun) {
    url.searchParams.set("dry_run", "true");
  }

  const headers = new Headers();
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }
  if (input.tenantId) {
    headers.set("X-Tenant-Id", input.tenantId);
  }
  if (input.breakGlassToken) {
    headers.set("X-Break-Glass-Token", input.breakGlassToken);
  }

  const response = await fetch(url.toString(), {
    method: "POST",
    body: form,
    headers,
  });
  const text = await response.text();

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: text || response.statusText,
      raw: text,
    };
  }

  const parsed = parseJson(text);
  if (parsed) {
    return { ok: true, status: response.status, data: parsed };
  }

  return { ok: true, status: response.status, raw: text };
}
