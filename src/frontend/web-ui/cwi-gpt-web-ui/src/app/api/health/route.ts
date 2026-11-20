import { NextResponse } from "next/server";
import { resolveGatewayBaseUrl } from "@/app/api/_shared/gateway";
import { isDirectLlmEnabled, resolveLlmConfig, resolveLlmHealthUrl } from "@/app/api/_shared/llm";
import { getRuntimeConfigStatus, normalizeUrl } from "@/lib/config";

const DEFAULT_TIMEOUT_MS = 2000;

async function probeUrl(url: string) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);

  try {
    const response = await fetch(url, {
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

function resolveGatewayHealthUrl(gatewayBaseUrl: string): string {
  const normalizedGateway = normalizeUrl(gatewayBaseUrl);
  return `${normalizedGateway}/api/health`;
}

export async function GET() {
  const configStatus = getRuntimeConfigStatus();
  const gatewayBaseUrl = resolveGatewayBaseUrl();
  const directLlmEnabled = isDirectLlmEnabled();
  const llmConfig = directLlmEnabled ? resolveLlmConfig() : null;

  const gatewayCheck = gatewayBaseUrl
    ? await probeUrl(resolveGatewayHealthUrl(gatewayBaseUrl))
    : { ok: false, detail: "gateway_base_url_missing" };

  const llmCheck = llmConfig
    ? await probeUrl(resolveLlmHealthUrl(llmConfig))
    : null;

  const ready = gatewayCheck.ok;

  return NextResponse.json({
    ok: true,
    ready,
    checks: {
      config: {
        status: configStatus.missingEnv.length === 0 ? "ok" : "warn",
        detail: configStatus.missingEnv.length === 0 ? null : configStatus.missingEnv.join(", "),
        missing_env: configStatus.missingEnv,
        used_dev_defaults: configStatus.usedDevDefaults,
        auth_mode: configStatus.config.authMode,
        gateway_base_url: configStatus.config.gatewayBaseUrl || null,
      },
      api: {
        status: gatewayCheck.ok ? "ok" : "down",
        detail: gatewayCheck.detail,
      },
      llm: llmCheck
        ? {
            status: llmCheck.ok ? "ok" : "down",
            detail: llmCheck.detail,
          }
        : directLlmEnabled
          ? {
              status: "down",
              detail: "llm_config_missing",
            }
          : {
              status: "disabled",
              detail: "Direct LLM is disabled; requests use the gateway/orchestrator.",
            },
    },
    timestamp: new Date().toISOString(),
  });
}
