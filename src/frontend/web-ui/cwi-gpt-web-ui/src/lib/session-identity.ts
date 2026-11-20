import { resolveRuntimeConfig, type AuthMode, type RuntimeOverrides } from "./config";
import { decodeJwtPayload, getFirstStringClaim, DEFAULT_USER_ID_CLAIMS, DEFAULT_TENANT_CLAIMS } from "./jwt";

const KEY_PREFIX = "cwi.user.web.";
const TOKEN_KEY = `${KEY_PREFIX}token.access`;
const OVERRIDES_KEY = `${KEY_PREFIX}overrides`;

export interface ResolvedSessionIdentity {
  authMode: AuthMode;
  tenantId: string | null;
  userId: string | null;
  tokenTenantId: string | null;
  tokenUserId: string | null;
  storageScope: string;
}

function hasWindow(): boolean {
  return typeof window !== "undefined";
}

function readLocal(key: string): string | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function readSession(key: string): string | null {
  if (!hasWindow()) {
    return null;
  }
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function loadStoredOverrides(): RuntimeOverrides {
  const raw = readLocal(OVERRIDES_KEY);
  if (!raw) {
    return {};
  }
  try {
    return JSON.parse(raw) as RuntimeOverrides;
  } catch {
    return {};
  }
}

function loadStoredToken(): string | null {
  return readSession(TOKEN_KEY);
}

function encodeScopeSegment(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return encodeURIComponent(trimmed || fallback);
}

export function buildStorageScope(tenantId: string | null, userId: string | null): string {
  return `tenant:${encodeScopeSegment(tenantId, "public")}:user:${encodeScopeSegment(userId, "anonymous")}`;
}

export function resolveSessionIdentity(options: {
  authMode: AuthMode;
  tenantId: string | null;
  runtimeUserId?: string | null;
  token?: string | null;
}): ResolvedSessionIdentity {
  const claims = decodeJwtPayload(options.token ?? null);
  const tokenTenantId = getFirstStringClaim(claims, DEFAULT_TENANT_CLAIMS);
  const tokenUserId = getFirstStringClaim(claims, DEFAULT_USER_ID_CLAIMS);

  const tenantId =
    options.authMode === "none"
      ? options.tenantId || "public"
      : tokenTenantId || options.tenantId;
  const userId =
    options.authMode === "none"
      ? options.runtimeUserId || "anonymous"
      : tokenUserId;

  return {
    authMode: options.authMode,
    tenantId,
    userId,
    tokenTenantId,
    tokenUserId,
    storageScope: buildStorageScope(tenantId, userId),
  };
}

export function resolveCurrentSessionIdentity(
  overrides?: RuntimeOverrides | null,
  token?: string | null,
): ResolvedSessionIdentity {
  const runtime = resolveRuntimeConfig(overrides ?? loadStoredOverrides());
  return resolveSessionIdentity({
    authMode: runtime.config.authMode,
    tenantId: runtime.config.tenantId,
    runtimeUserId: runtime.config.userId,
    token: token ?? loadStoredToken(),
  });
}

export function getScopedStorageKey(
  baseKey: string,
  identity: Pick<ResolvedSessionIdentity, "tenantId" | "userId" | "storageScope"> = resolveCurrentSessionIdentity(),
): string {
  return `${baseKey}.${identity.storageScope || buildStorageScope(identity.tenantId, identity.userId)}`;
}
