import { NextResponse } from "next/server";
import { getRuntimeConfigStatus } from "@/lib/config";

const DEFAULT_TIMEOUT_MS = 2000;

async function probeApi(baseUrl: string) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);

  try {
    const response = await fetch(`${baseUrl}/api/health`, {
      signal: controller.signal,
      headers: { Accept: "application/json" },
    });
    return {
      ok: response.ok,
      detail: response.ok ? null : `HTTP ${response.status}`,
    };
  } catch (err) {
    const message = err instanceof Error ? err.name : "probe_failed";
    return { ok: false, detail: message };
  } finally {
    clearTimeout(timeout);
  }
}

export async function GET() {
  const configStatus = getRuntimeConfigStatus();

  let apiCheck: { ok: boolean; detail: string | null } = {
    ok: false,
    detail: "api_base_url_missing",
  };

  if (configStatus.config?.apiBaseUrl) {
    apiCheck = await probeApi(configStatus.config.apiBaseUrl);
  } else if (configStatus.missing.length > 0) {
    apiCheck = { ok: false, detail: "config_missing" };
  }

  const ready = configStatus.missing.length === 0 && apiCheck.ok;

  return NextResponse.json({
    ok: true,
    ready,
    checks: {
      config: {
        status: configStatus.missing.length === 0 ? "ok" : "down",
        detail: configStatus.missing.length === 0 ? null : "config_missing",
      },
      api: {
        status: apiCheck.ok ? "ok" : "down",
        detail: apiCheck.detail,
      },
    },
    timestamp: new Date().toISOString(),
  });
}
