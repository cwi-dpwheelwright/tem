import { resolveRuntimeConfig } from "./config";
import { isErrorReportingEnabled, loadOverrides } from "./storage";
import { loadSelectedPackName } from "./packs/selection";

export interface ErrorReportInput {
  category: string;
  summary: string;
  status?: number;
  requestId?: string | null;
  traceId?: string | null;
  detail?: string;
}

const TOKEN_RE = /(bearer\s+)?[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}/gi;
const SECRET_RE = /(authorization|token|password|secret)=([^&\s]+)/gi;

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

function redactValue(value: string | undefined): string | undefined {
  if (!value) {
    return value;
  }
  return value.replace(TOKEN_RE, "[redacted]").replace(SECRET_RE, "$1=[redacted]");
}

function buildPayload(input: ErrorReportInput) {
  const runtime = resolveRuntimeConfig(loadOverrides());
  return {
    category: input.category,
    summary: input.summary,
    status: input.status,
    requestId: input.requestId ?? undefined,
    traceId: input.traceId ?? undefined,
    detail: redactValue(input.detail),
    path: hasWindow() ? window.location.pathname : undefined,
    gatewayBaseUrl: runtime.config.gatewayBaseUrl,
    packName: loadSelectedPackName(),
    buildVersion: runtime.config.buildVersion,
    buildSha: runtime.config.buildSha,
    userAgent: hasWindow() ? navigator.userAgent : undefined,
    timestamp: new Date().toISOString(),
  };
}

export async function reportError(input: ErrorReportInput): Promise<void> {
  if (!hasWindow()) {
    return;
  }
  const runtime = resolveRuntimeConfig(loadOverrides());
  const endpoint = runtime.config.errorReportUrl;
  if (!endpoint || !isErrorReportingEnabled()) {
    return;
  }

  const payload = buildPayload(input);

  try {
    await fetch(endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify(payload),
      keepalive: true,
    });
  } catch {
    // Swallow errors to avoid impacting UX.
  }
}
