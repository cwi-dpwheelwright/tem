export type AuthMode = "oidc" | "token" | "none";

export interface RuntimeConfig {
  gatewayBaseUrl: string;
  tenantId: string | null;
  adminTenantId: string | null;
  userId: string | null;
  authMode: AuthMode;
  authIdleTimeoutMs: number;
  requestTimeoutMs: number;
  policyMode: string;
  topK: number;
  errorReportUrl: string | null;
  buildVersion: string | null;
  buildSha: string | null;
  globalInputFocusEnabled: boolean;
  stickyChatLayoutEnabled: boolean;
}

export interface RuntimeOverrides {
  gatewayBaseUrl?: string | null;
  tenantId?: string | null;
  userId?: string | null;
  authMode?: AuthMode;
  authIdleTimeoutMs?: number | null;
  requestTimeoutMs?: number | null;
  policyMode?: string | null;
  topK?: number | null;
}

export interface RuntimeConfigStatus {
  config: RuntimeConfig;
  missingEnv: string[];
  usedDevDefaults: string[];
}

const DEFAULT_AUTH_IDLE_MS = 30 * 60 * 1000;
const DEFAULT_REQUEST_TIMEOUT_MS = 60000;
const DEFAULT_POLICY_MODE = "observe";
const ALLOWED_POLICY_MODES = new Set(["observe", "recommend", "approve", "auto_within_bounds"]);
const DEFAULT_TOP_K = 5;

const PUBLIC_ENV = {
  NEXT_PUBLIC_CWI_AUTH_MODE: process.env.NEXT_PUBLIC_CWI_AUTH_MODE,
  NEXT_PUBLIC_CWI_GATEWAY_BASE_URL: process.env.NEXT_PUBLIC_CWI_GATEWAY_BASE_URL,
  NEXT_PUBLIC_CWI_DEFAULT_TENANT: process.env.NEXT_PUBLIC_CWI_DEFAULT_TENANT,
  NEXT_PUBLIC_CWI_ADMIN_TENANT: process.env.NEXT_PUBLIC_CWI_ADMIN_TENANT,
  NEXT_PUBLIC_CWI_OIDC_AUTHORITY: process.env.NEXT_PUBLIC_CWI_OIDC_AUTHORITY,
  NEXT_PUBLIC_CWI_OIDC_CLIENT_ID: process.env.NEXT_PUBLIC_CWI_OIDC_CLIENT_ID,
  NEXT_PUBLIC_CWI_OIDC_REDIRECT_URI: process.env.NEXT_PUBLIC_CWI_OIDC_REDIRECT_URI,
  NEXT_PUBLIC_CWI_OIDC_POST_LOGOUT_REDIRECT_URI: process.env.NEXT_PUBLIC_CWI_OIDC_POST_LOGOUT_REDIRECT_URI,
  NEXT_PUBLIC_CWI_OIDC_SCOPES: process.env.NEXT_PUBLIC_CWI_OIDC_SCOPES,
  NEXT_PUBLIC_CWI_POLICY_MODE: process.env.NEXT_PUBLIC_CWI_POLICY_MODE,
  NEXT_PUBLIC_CWI_AUTH_IDLE_TIMEOUT_MS: process.env.NEXT_PUBLIC_CWI_AUTH_IDLE_TIMEOUT_MS,
  NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS: process.env.NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS,
  NEXT_PUBLIC_CWI_TOP_K: process.env.NEXT_PUBLIC_CWI_TOP_K,
  NEXT_PUBLIC_CWI_ERROR_REPORT_URL: process.env.NEXT_PUBLIC_CWI_ERROR_REPORT_URL,
  NEXT_PUBLIC_CWI_BUILD_VERSION: process.env.NEXT_PUBLIC_CWI_BUILD_VERSION,
  NEXT_PUBLIC_CWI_BUILD_SHA: process.env.NEXT_PUBLIC_CWI_BUILD_SHA,
  NEXT_PUBLIC_CWI_GLOBAL_INPUT_FOCUS_ENABLED: process.env.NEXT_PUBLIC_CWI_GLOBAL_INPUT_FOCUS_ENABLED,
  NEXT_PUBLIC_CWI_STICKY_CHAT_LAYOUT_ENABLED: process.env.NEXT_PUBLIC_CWI_STICKY_CHAT_LAYOUT_ENABLED,
} as const;

function readEnv(name: string): string | null {
  const value =
    typeof window === "undefined"
      ? process.env[name]
      : (PUBLIC_ENV as Record<string, string | undefined>)[name];
  if (!value || !value.trim()) {
    return null;
  }
  return value.trim();
}

function resolveBrowserOrigin(): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  const origin = window.location.origin?.trim();
  return origin ? origin : null;
}

function parseNumber(value: string | null, fallback: number): number {
  if (!value) {
    return fallback;
  }
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }
  return parsed;
}

function parseBool(value: string | null, fallback: boolean): boolean {
  if (!value) {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === "1" || normalized === "true" || normalized === "yes") {
    return true;
  }
  if (normalized === "0" || normalized === "false" || normalized === "no") {
    return false;
  }
  return fallback;
}

function normalizePolicyMode(value: string | null | undefined): string {
  const normalized = value?.trim().toLowerCase();
  if (!normalized || normalized === "standard") {
    return DEFAULT_POLICY_MODE;
  }
  return ALLOWED_POLICY_MODES.has(normalized) ? normalized : DEFAULT_POLICY_MODE;
}

export function normalizeUrl(value: string): string {
  if (!value) {
    return value;
  }
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }
  return trimmed.endsWith("/") ? trimmed.slice(0, -1) : trimmed;
}

function detectAuthMode(): AuthMode {
  const explicit = readEnv("NEXT_PUBLIC_CWI_AUTH_MODE");
  if (explicit === "oidc" || explicit === "token") {
    return explicit;
  }
  const hasOidc = Boolean(readEnv("NEXT_PUBLIC_CWI_OIDC_AUTHORITY")) && Boolean(readEnv("NEXT_PUBLIC_CWI_OIDC_CLIENT_ID"));
  return hasOidc ? "oidc" : "token";
}

export function resolveRuntimeConfig(overrides?: RuntimeOverrides | null): RuntimeConfigStatus {
  const missingEnv: string[] = [];
  const usedDevDefaults: string[] = [];
  const envGatewayBaseUrl =
    readEnv("NEXT_PUBLIC_CWI_GATEWAY_BASE_URL") ||
    readEnv("CWI_GATEWAY_BASE_URL");
  const gatewayBaseUrl =
    overrides?.gatewayBaseUrl?.trim() ||
    envGatewayBaseUrl ||
    resolveBrowserOrigin() ||
    "";

  const envTenantId = readEnv("NEXT_PUBLIC_CWI_DEFAULT_TENANT");
  const envAdminTenantId = readEnv("NEXT_PUBLIC_CWI_ADMIN_TENANT");
  const tenantId =
    overrides?.tenantId ??
    envTenantId ??
    null;
  const adminTenantId = envAdminTenantId ?? tenantId;
  const userId = overrides?.userId ?? null;
  const detectedAuthMode = detectAuthMode();
  const overrideAuthMode = overrides?.authMode;
  const authMode =
    overrideAuthMode === "oidc" || overrideAuthMode === "token"
      ? overrideAuthMode
      : detectedAuthMode;
  const authIdleTimeoutMs = overrides?.authIdleTimeoutMs ?? parseNumber(readEnv("NEXT_PUBLIC_CWI_AUTH_IDLE_TIMEOUT_MS"), DEFAULT_AUTH_IDLE_MS);
  const requestTimeoutMs = overrides?.requestTimeoutMs ?? parseNumber(readEnv("NEXT_PUBLIC_CWI_REQUEST_TIMEOUT_MS"), DEFAULT_REQUEST_TIMEOUT_MS);
  const policyMode = normalizePolicyMode(overrides?.policyMode ?? readEnv("NEXT_PUBLIC_CWI_POLICY_MODE"));
  const topK = overrides?.topK ?? parseNumber(readEnv("NEXT_PUBLIC_CWI_TOP_K"), DEFAULT_TOP_K);
  const errorReportUrl = readEnv("NEXT_PUBLIC_CWI_ERROR_REPORT_URL");
  const buildVersion = readEnv("NEXT_PUBLIC_CWI_BUILD_VERSION");
  const buildSha = readEnv("NEXT_PUBLIC_CWI_BUILD_SHA");
  const globalInputFocusEnabled = parseBool(readEnv("NEXT_PUBLIC_CWI_GLOBAL_INPUT_FOCUS_ENABLED"), true);
  const stickyChatLayoutEnabled = parseBool(readEnv("NEXT_PUBLIC_CWI_STICKY_CHAT_LAYOUT_ENABLED"), true);

  if (!envGatewayBaseUrl) {
    missingEnv.push("NEXT_PUBLIC_CWI_GATEWAY_BASE_URL");
  }
  if (!envTenantId) {
    missingEnv.push("NEXT_PUBLIC_CWI_DEFAULT_TENANT");
  }

  return {
    config: {
      gatewayBaseUrl: normalizeUrl(gatewayBaseUrl),
      tenantId,
      adminTenantId,
      userId,
      authMode,
      authIdleTimeoutMs,
      requestTimeoutMs,
      policyMode,
      topK,
      errorReportUrl,
      buildVersion,
      buildSha,
      globalInputFocusEnabled,
      stickyChatLayoutEnabled,
    },
    missingEnv,
    usedDevDefaults,
  };
}

export function getRuntimeConfigStatus(): RuntimeConfigStatus {
  return resolveRuntimeConfig();
}
